using Mirror;
using UnityEngine;
using Unity.Cinemachine;
using System.Collections;

public class KillerMove : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Transform CinemachineRoot;
    [SerializeField] private Animator animator;

    [Header("Virtual Cameras")]
    [SerializeField] private CinemachineCamera lobbyCam;
    [SerializeField] private CinemachineCamera normalCam;
    [SerializeField] private CinemachineCamera resultCam;
    [SerializeField] private int activeCameraPriority = 30;
    [SerializeField] private int inactiveCameraPriority = 0;

    [Header("속도 설정")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float lungeMultiplier = 1.6f;
    [SerializeField] private float penaltyMultiplier = 0.4f;
    [SerializeField] private float rageSpeedMultiplier = 1.1f;
    [SerializeField] private float lookSensitivity = 0.2f;

    [Header("살인마 발소리")]
    [SerializeField] private Vector3 footstepPositionOffset = new Vector3(0f, 0.1f, 0f);
    [SerializeField] private float footstepMoveThreshold = 0.1f;

    // 애니메이션 이벤트가 겹쳐서 한 프레임에 여러 번 호출되는 것을 방지하는 최소 간격
    [SerializeField] private float footstepMinInterval = 0.08f;

    private CharacterController controller;
    private KillerInput input;
    private KillerState state;

    private float localYaw, localPitch, yVelocity;

    private Vector2 serverMoveInput;
    private float serverYaw;

    private KillerCondition lastAppliedCondition;
    private bool hasAppliedCondition;
    private bool isResultPlaying;

    private float lastFootstepServerTime;

    [SyncVar] private float syncedYaw, syncedPitch, syncedMoveSpeed;

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        ApplyViewByState();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        DontDestroyOnLoad(gameObject);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        DontDestroyOnLoad(gameObject);
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        input = GetComponent<KillerInput>();
        state = GetComponent<KillerState>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // 프리팹 생성 직후에는 모든 Virtual Camera 우선순위를 낮춰둔다.
        SetCameraPriority(lobbyCam, false);
        SetCameraPriority(normalCam, false);
        SetCameraPriority(resultCam, false);
    }

    private void Update()
    {
        if (isLocalPlayer)
        {
            if (!NetworkClient.active || !NetworkClient.ready)
                return;

            if (state == null || input == null)
                return;

            // 상태가 바뀐 경우에만 카메라 Priority / Cursor 갱신
            if (!hasAppliedCondition || lastAppliedCondition != state.CurrentCondition)
                ApplyViewByState();

            if (state.CanLook)
                UpdateLocalLook();

            Vector2 moveInput = state.CanMove ? input.Move : Vector2.zero;

            // Lobby 상태에서는 Command 자체를 보내지 않음
            if (state.CanMove || state.CanLook)
                CmdSetMoveInput(moveInput, localYaw, localPitch);

            if (animator != null)
                animator.SetFloat("Speed", moveInput.magnitude, 0.1f, Time.deltaTime);
        }
        else
        {
            ApplyRemoteLook();

            if (animator != null)
                animator.SetFloat("Speed", syncedMoveSpeed, 0.1f, Time.deltaTime);
        }
    }

    private void FixedUpdate()
    {
        if (isServer)
            ServerTickMovement();
    }

    private void UpdateLocalLook()
    {
        localYaw += input.Look.x * lookSensitivity;
        localPitch = Mathf.Clamp(localPitch - input.Look.y * lookSensitivity, -80f, 80f);

        transform.rotation = Quaternion.Euler(0f, localYaw, 0f);

        if (CinemachineRoot != null)
            CinemachineRoot.localRotation = Quaternion.Euler(localPitch, 0f, 0f);
    }

    private void ApplyRemoteLook()
    {
        transform.rotation = Quaternion.Euler(0f, syncedYaw, 0f);

        if (CinemachineRoot != null)
            CinemachineRoot.localRotation = Quaternion.Euler(syncedPitch, 0f, 0f);
    }

    private void ApplyViewByState()
    {
        if (isResultPlaying)
            return;

        if (!isLocalPlayer || CinemachineRoot == null)
            return;

        bool isLobby = state.CurrentCondition == KillerCondition.Lobby;
        CinemachineRoot.gameObject.SetActive(true);

        SetCameraPriority(lobbyCam, isLobby);
        SetCameraPriority(normalCam, !isLobby);
        SetCameraPriority(resultCam, false);

        Cursor.lockState = isLobby ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = isLobby;

        lastAppliedCondition = state.CurrentCondition;
        hasAppliedCondition = true;
    }

    private void SetCameraPriority(CinemachineCamera cam, bool isActive)
    {
        if (cam == null)
            return;

        cam.Priority = isActive ? activeCameraPriority : inactiveCameraPriority;
    }

    [Server]
    public void SetInGameStateServer()
    {
        if (state == null)
            return;

        // Lobby -> Idle
        state.ChangeState(KillerCondition.Idle);

        serverMoveInput = Vector2.zero;
        syncedMoveSpeed = 0f;
        lastFootstepServerTime = 0f;

        if (connectionToClient != null)
            TargetRefreshViewByState(connectionToClient);
    }

    [Server]
    public void BeginKillerResult()
    {
        if (isResultPlaying)
            return;

        isResultPlaying = true;

        serverMoveInput = Vector2.zero;
        syncedMoveSpeed = 0f;
        lastFootstepServerTime = 0f;

        if (state != null)
            state.ChangeState(KillerCondition.Idle);

        if (connectionToClient != null)
            TargetDisableKillerInput(connectionToClient);

        StartCoroutine(KillerResultRoutine());

        Debug.Log("[KillerMove] Killer Result 시작");
    }

    [Server]
    private IEnumerator KillerResultRoutine()
    {
        if (connectionToClient == null)
            yield break;

        TargetSetBlackout(connectionToClient, true);
        yield return new WaitForSeconds(1f);

        MoveToKillerResultPoint();

        TargetShowKillerResultViewAndUI(connectionToClient);
        yield return new WaitForSeconds(2f);

        TargetSetBlackout(connectionToClient, false);
    }

    [Server]
    private void MoveToKillerResultPoint()
    {
        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[KillerMove] GameManager.Instance가 없습니다.");
            return;
        }

        Transform resultPoint = GameManager.Instance.GetKillerResultPoint();

        if (resultPoint == null)
        {
            Debug.LogWarning("[KillerMove] Killer 결과 위치를 찾지 못했습니다.");
            return;
        }

        ServerTeleportTo(resultPoint.position, resultPoint.rotation);

        Debug.Log("[KillerMove] Killer 결과 위치로 이동 완료");
    }

    [Server]
    public void CheckAllSurvivorsDeadAndShowResult()
    {
        if (isResultPlaying)
            return;

        SurvivorState[] survivors = FindObjectsByType<SurvivorState>(FindObjectsSortMode.None);

        if (survivors == null || survivors.Length == 0)
            return;

        for (int i = 0; i < survivors.Length; i++)
        {
            if (survivors[i] == null)
                continue;

            if (!survivors[i].IsDead)
                return;
        }

        BeginKillerResult();
    }

    // Animation Event Receiver에서 호출되는 함수
    // 실제 사운드는 바로 로컬에서 재생하지 않고 서버 Command로 요청한다.
    public void PlayKillerFootstepByAnimationEvent()
    {
        // Animation Event는 모든 클라이언트의 Animator에서 호출될 수 있다.
        // 그래서 살인마를 조종하는 로컬 플레이어만 서버에 사운드 요청을 보낸다.
        if (!isLocalPlayer)
            return;

        if (state == null)
            return;

        if (!CanRequestKillerFootstep())
            return;

        CmdPlayKillerFootstepByAnimationEvent();
    }

    private bool CanRequestKillerFootstep()
    {
        if (isResultPlaying)
            return false;

        if (state == null)
            return false;

        // 로비나 강제 행동 상태에서는 발소리를 보내지 않는다.
        if (state.CurrentCondition == KillerCondition.Lobby ||
            state.CurrentCondition == KillerCondition.Hit ||
            state.CurrentCondition == KillerCondition.Vaulting ||
            state.CurrentCondition == KillerCondition.Breaking ||
            state.CurrentCondition == KillerCondition.Incage)
        {
            return false;
        }

        return true;
    }

    [Command]
    private void CmdPlayKillerFootstepByAnimationEvent()
    {
        if (!CanServerPlayKillerFootstep())
            return;

        lastFootstepServerTime = Time.time;

        Vector3 playPosition = transform.position + footstepPositionOffset;

        // 생존자들에게만 3D 살인마 발소리를 들려준다.
        NetworkAudioManager.PlayAudioForSurvivors(
            AudioKey.KillerFootstep,
            AudioDimension.Sound3D,
            playPosition
        );
    }

    [Server]
    private bool CanServerPlayKillerFootstep()
    {
        if (isResultPlaying)
            return false;

        if (state == null)
            return false;

        if (NetworkAudioManager.Instance == null)
            return false;

        // 너무 가까운 시간에 중복 이벤트가 들어오면 무시한다.
        if (Time.time - lastFootstepServerTime < footstepMinInterval)
            return false;

        // 서버 기준으로 이동 입력이 거의 없으면 무시한다.
        // Animation Event를 실수로 Idle 클립에 넣었을 때 발소리가 나는 것을 막기 위한 안전장치다.
        bool isMoving = serverMoveInput.sqrMagnitude >= footstepMoveThreshold * footstepMoveThreshold;

        if (!isMoving && state.CurrentCondition != KillerCondition.Lunging)
            return false;

        // 서버 기준으로도 발소리를 내면 안 되는 상태는 차단한다.
        if (state.CurrentCondition == KillerCondition.Lobby ||
            state.CurrentCondition == KillerCondition.Hit ||
            state.CurrentCondition == KillerCondition.Vaulting ||
            state.CurrentCondition == KillerCondition.Breaking ||
            state.CurrentCondition == KillerCondition.Incage)
        {
            return false;
        }

        return true;
    }

    [TargetRpc]
    private void TargetDisableKillerInput(NetworkConnectionToClient target)
    {
        if (input != null)
            input.enabled = false;

        Debug.Log("[KillerMove] KillerInput 비활성화");
    }

    [TargetRpc]
    private void TargetSetBlackout(NetworkConnectionToClient target, bool value)
    {
        if (ChangeSceneUI.Instance != null)
            ChangeSceneUI.Instance.Show(value);

        Debug.Log($"[KillerMove] 개인 블랙아웃 상태 변경: {value}");
    }

    [TargetRpc]
    private void TargetShowKillerResultViewAndUI(NetworkConnectionToClient target)
    {
        isResultPlaying = true;

        if (CinemachineRoot != null)
            CinemachineRoot.gameObject.SetActive(true);

        SetCameraPriority(lobbyCam, false);
        SetCameraPriority(normalCam, false);
        SetCameraPriority(resultCam, true);

        if (InGameUIManager.Instance != null)
            InGameUIManager.Instance.ShowResultUI();

        Debug.Log("[KillerMove] ResultCam 전환 및 ResultUI 활성화");
    }

    [TargetRpc]
    private void TargetRefreshViewByState(NetworkConnectionToClient target)
    {
        StartCoroutine(RefreshViewNextFrame());
    }

    private IEnumerator RefreshViewNextFrame()
    {
        yield return null;

        hasAppliedCondition = false;
        ApplyViewByState();
    }

    [Command]
    private void CmdSetMoveInput(Vector2 move, float yaw, float pitch)
    {
        serverMoveInput = move;
        serverYaw = yaw;

        syncedYaw = yaw;
        syncedPitch = pitch;
    }

    [Server]
    private void ServerTickMovement()
    {
        if (controller == null || !controller.enabled)
            return;

        if (state == null || !state.CanMove)
        {
            ApplyGravityOnlyServer();
            syncedMoveSpeed = 0f;
            return;
        }

        float speed = moveSpeed;

        if (state.IsRaging)
            speed *= rageSpeedMultiplier;

        if (state.CurrentCondition == KillerCondition.Lunging)
            speed *= lungeMultiplier;
        else if (state.CurrentCondition == KillerCondition.Recovering)
            speed *= penaltyMultiplier;

        transform.rotation = Quaternion.Euler(0f, serverYaw, 0f);

        Vector3 moveDir = transform.right * serverMoveInput.x + transform.forward * serverMoveInput.y;

        if (moveDir.magnitude > 1f)
            moveDir.Normalize();

        if (controller.isGrounded)
            yVelocity = -1f;
        else
            yVelocity += Physics.gravity.y * Time.fixedDeltaTime;

        Vector3 finalMove = moveDir * speed;
        finalMove.y = yVelocity;

        controller.Move(finalMove * Time.fixedDeltaTime);

        syncedMoveSpeed = serverMoveInput.magnitude;
    }

    [Server]
    private void ApplyGravityOnlyServer()
    {
        if (controller == null || !controller.enabled)
            return;

        if (controller.isGrounded)
            yVelocity = -1f;
        else
            yVelocity += Physics.gravity.y * Time.fixedDeltaTime;

        controller.Move(new Vector3(0f, yVelocity, 0f) * Time.fixedDeltaTime);
    }

    [Server]
    public void ServerTeleportTo(Vector3 position, Quaternion rotation)
    {
        float yaw = rotation.eulerAngles.y;

        serverMoveInput = Vector2.zero;
        serverYaw = yaw;

        syncedYaw = yaw;
        syncedPitch = 0f;
        syncedMoveSpeed = 0f;

        localPitch = 0f;
        yVelocity = 0f;
        lastFootstepServerTime = 0f;

        ApplyTeleport(position, rotation);

        if (connectionToClient != null)
            TargetTeleportTo(connectionToClient, position, rotation);
    }

    [TargetRpc]
    private void TargetTeleportTo(NetworkConnectionToClient target, Vector3 position, Quaternion rotation)
    {
        float yaw = rotation.eulerAngles.y;

        localYaw = yaw;
        localPitch = 0f;

        ApplyTeleport(position, rotation);

        if (CinemachineRoot != null)
            CinemachineRoot.localRotation = Quaternion.identity;

        ApplyViewByState();
    }

    private void ApplyTeleport(Vector3 position, Quaternion rotation)
    {
        bool wasControllerEnabled = false;

        if (controller != null)
        {
            wasControllerEnabled = controller.enabled;
            controller.enabled = false;
        }

        transform.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();

        if (controller != null)
            controller.enabled = wasControllerEnabled;
    }
}