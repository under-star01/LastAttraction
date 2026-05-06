using Mirror;
using UnityEngine;

public class SurvivorMove : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Transform cameraYawRoot;
    [SerializeField] private Transform cameraPitchRoot;
    [SerializeField] private Transform modelRoot;
    [SerializeField] private Animator animator;

    [Header("속도")]
    [SerializeField] private float walkSpeed = 2.3f;
    [SerializeField] private float runSpeed = 4f;
    [SerializeField] private float crouchSpeed = 1.2f;
    [SerializeField] private float crawlSpeed = 0.6f;
    [SerializeField] private float turnSpeed = 15f;

    [Header("카메라")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float minPitch = -60f;
    [SerializeField] private float maxPitch = 60f;

    [Header("발소리")]
    [SerializeField] private AudioKey footstepKey = AudioKey.SurvivorFootstep;
    [SerializeField] private float minFootstepInterval = 0.2f;

    [Header("컨트롤러 높이")]
    [SerializeField] private float standHeight = 1.8f;
    [SerializeField] private Vector3 standCenter = new Vector3(0f, 0.9f, 0f);
    [SerializeField] private float crouchHeight = 0.9f;
    [SerializeField] private Vector3 crouchCenter = new Vector3(0f, 0.45f, 0f);

    private CharacterController controller;
    private SurvivorInput input;
    private SurvivorInteractor interactor;
    private SurvivorState state;
    private SurvivorActionState act;
    private SurvivorMoveState moveState;
    private SurvivorCameraSkill camSkill;

    private float localYaw;
    private float localPitch;
    private float yVelocity;
    private float lastFootstepTime;

    private Transform escapeTarget;
    private Vector2 serverMoveInput;
    private bool serverWantsRun;
    private bool serverWantsCrouch;
    private float serverYaw;
    private bool isMoveLocked;
    private float serverPitch;

    [SyncVar] private float syncedYaw;
    [SyncVar] private float syncedPitch;
    [SyncVar] private float syncedModelYaw;

    // 외부 스크립트가 이동을 잠글 때 사용한다.
    public void SetMoveLock(bool value)
    {
        isMoveLocked = value;

        if (isLocalPlayer && !isServer)
            CmdSetMoveLock(value);
    }

    [Command]
    private void CmdSetMoveLock(bool value)
    {
        isMoveLocked = value;
    }

    // 외부에서 특정 방향을 바라보게 할 때 사용한다.
    // 예: 상호작용 시작 전에 오브젝트 쪽으로 정렬
    public void FaceDirection(Vector3 dir)
    {
        dir.y = 0f;

        if (dir.sqrMagnitude <= 0.001f)
            return;

        if (isServer)
        {
            ApplyFace(dir.normalized);
            RpcFace(dir.normalized);
        }
        else if (isLocalPlayer)
        {
            CmdFace(dir.normalized);
        }
    }

    [Command]
    private void CmdFace(Vector3 dir)
    {
        if (dir.sqrMagnitude <= 0.001f)
            return;

        ApplyFace(dir.normalized);
        RpcFace(dir.normalized);
    }

    [ClientRpc]
    private void RpcFace(Vector3 dir)
    {
        if (isServer)
            return;

        ApplyFace(dir.normalized);
    }

    private void ApplyFace(Vector3 dir)
    {
        if (modelRoot == null)
            return;

        modelRoot.rotation = Quaternion.LookRotation(dir);
        syncedModelYaw = modelRoot.eulerAngles.y;
    }

    public void SetModelLayer(int layer)
    {
        if (modelRoot == null)
            return;

        SetModelLayerRecursive(modelRoot, layer);
    }

    // modelRoot 아래를 돌면서 몸 모델만 레이어를 바꾼다.
    // 카메라 모델 관련 레이어는 유지해서 손에 붙어 있어도 같이 숨겨지지 않게 한다.
    private void SetModelLayerRecursive(Transform target, int layer)
    {
        if (target == null)
            return;

        int camLocalLayer = LayerMask.NameToLayer("CamLocal");
        int camWorldLayer = LayerMask.NameToLayer("CamWorld");
        int hideSelfLayer = LayerMask.NameToLayer("HideSelf");

        // 카메라 모델 관련 레이어는 유지한다.
        if (target.gameObject.layer == camLocalLayer ||
            target.gameObject.layer == camWorldLayer ||
            target.gameObject.layer == hideSelfLayer)
        {
            return;
        }

        target.gameObject.layer = layer;

        for (int i = 0; i < target.childCount; i++)
            SetModelLayerRecursive(target.GetChild(i), layer);
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        input = GetComponent<SurvivorInput>();
        interactor = GetComponent<SurvivorInteractor>();
        state = GetComponent<SurvivorState>();
        act = GetComponent<SurvivorActionState>();
        moveState = GetComponent<SurvivorMoveState>();
        camSkill = GetComponent<SurvivorCameraSkill>();

        if (animator == null && modelRoot != null)
            animator = modelRoot.GetComponentInChildren<Animator>();

        if (animator != null)
            animator.applyRootMotion = false;
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        if (cameraYawRoot != null)
            localYaw = cameraYawRoot.localEulerAngles.y;

        ApplyCam();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (modelRoot != null)
            syncedModelYaw = modelRoot.eulerAngles.y;

        if (cameraYawRoot != null)
            syncedYaw = cameraYawRoot.localEulerAngles.y;

        DontDestroyOnLoad(gameObject);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (isLocalPlayer)
        {
            UpdateLook();
            SendInput();
            ApplyCam();
            ApplyModel();
        }
        else
        {
            ApplyRemoteLook();
            ApplyModel();
        }
    }

    private void FixedUpdate()
    {
        if (!isServer)
            return;

        MoveTick();
    }

    // 로컬 카메라 회전 입력 처리
    private void UpdateLook()
    {
        if (input == null || !input.enabled)
            return;

        Vector2 look = input.Look;

        localYaw += look.x * mouseSensitivity;
        localPitch -= look.y * mouseSensitivity;
        localPitch = Mathf.Clamp(localPitch, minPitch, maxPitch);
    }

    // 로컬 카메라 루트에 회전 반영
    private void ApplyCam()
    {
        if (cameraYawRoot != null)
            cameraYawRoot.localRotation = Quaternion.Euler(0f, localYaw, 0f);

        if (cameraPitchRoot != null)
            cameraPitchRoot.localRotation = Quaternion.Euler(localPitch, 0f, 0f);
    }

    // 원격 플레이어 카메라 회전 반영
    private void ApplyRemoteLook()
    {
        if (cameraYawRoot != null)
            cameraYawRoot.localRotation = Quaternion.Euler(0f, syncedYaw, 0f);

        if (cameraPitchRoot != null)
            cameraPitchRoot.localRotation = Quaternion.Euler(syncedPitch, 0f, 0f);
    }

    // 동기화된 모델 y 회전값 적용
    private void ApplyModel()
    {
        if (modelRoot == null)
            return;

        Vector3 euler = modelRoot.eulerAngles;
        euler.y = syncedModelYaw;
        modelRoot.eulerAngles = euler;
    }

    // 현재 입력을 서버로 전달
    private void SendInput()
    {
        if (input == null || !input.enabled)
            return;

        CmdSetMoveInput(
            input.Move,
            input.IsRunning,
            input.IsCrouching,
            localYaw,
            localPitch
        );
    }

    [Command]
    private void CmdSetMoveInput(Vector2 moveInput, bool wantsRun, bool wantsCrouch, float yaw, float pitch)
    {
        serverMoveInput = moveInput;
        serverWantsRun = wantsRun;
        serverWantsCrouch = wantsCrouch;
        serverYaw = yaw;
        serverPitch = pitch;

        syncedYaw = yaw;
        syncedPitch = pitch;
    }

    // 현재 카메라 스킬 사용 중인지 확인한다.
    private bool IsUsingCameraSkill()
    {
        if (camSkill != null && camSkill.IsUse)
            return true;

        if (act != null && act.IsCamSkill)
            return true;

        return false;
    }

    // 서버에서 실제 이동 처리
    [Server]
    private void MoveTick()
    {
        if (controller == null || !controller.enabled)
            return;

        if (escapeTarget != null)
        {
            EscapeMoveTick();
            return;
        }

        bool isDowned = state != null && state.IsDowned;
        bool isDead = state != null && state.IsDead;
        bool isBusy = act != null && act.IsBusy;

        // 움직일 수 없으면 중력만 적용한다.
        if (isMoveLocked || isBusy || isDead)
        {
            GravityOnly();

            if (moveState != null)
                moveState.SetMoveState(SurvivorLocomotionState.Idle, false);

            return;
        }

        // 다운 상태는 기어가는 이동만 가능하다.
        if (isDowned)
        {
            Crawl(serverMoveInput, serverYaw);
            return;
        }

        bool useCamSkill = IsUsingCameraSkill();

        // Hold 상호작용 중에는 새로 앉기 시작 금지
        bool canCrouch = interactor == null || !interactor.IsInteracting;

        // 카메라 스킬 사용 중에는 앉기 입력을 무시한다.
        // 앉은 상태에서 스킬 시작 자체는 SurvivorCameraSkill에서 막는다.
        if (useCamSkill)
            canCrouch = false;

        bool isCrouching = canCrouch && serverWantsCrouch;

        if (isCrouching)
            SetSize(crouchHeight, crouchCenter);
        else
            SetSize(standHeight, standCenter);

        MoveNormal(serverMoveInput, serverWantsRun, isCrouching, serverYaw);
    }

    // 일반 이동 처리
    [Server]
    private void MoveNormal(Vector2 moveInput, bool wantsRun, bool isCrouching, float yaw)
    {
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward = yawRot * Vector3.forward;
        Vector3 right = yawRot * Vector3.right;

        Vector3 move = forward * moveInput.y + right * moveInput.x;

        if (move.magnitude > 1f)
            move.Normalize();

        bool isMoving = move.sqrMagnitude > 0.001f;

        bool useCamSkill = IsUsingCameraSkill();

        // 카메라 스킬 중에는 달리기 금지
        bool isRunning = isMoving && !isCrouching && wantsRun && !useCamSkill;

        float speed = walkSpeed;

        if (isCrouching)
            speed = crouchSpeed;
        else if (isRunning)
            speed = runSpeed;

        if (controller.isGrounded)
            yVelocity = -1f;
        else
            yVelocity += Physics.gravity.y * Time.fixedDeltaTime;

        Vector3 finalMove = move * speed;
        finalMove.y = yVelocity;

        controller.Move(finalMove * Time.fixedDeltaTime);

        // 카메라 스킬 중이면 몸을 카메라 정면 방향으로 유지한다.
        if (useCamSkill)
        {
            Vector3 camDir = yawRot * Vector3.forward;
            RotateCam(camDir);
        }
        else
        {
            RotateMove(move, isMoving);
        }

        // 이동 상태 애니메이터 값 갱신
        if (moveState != null)
        {
            if (isCrouching)
            {
                moveState.SetMoveState(SurvivorLocomotionState.Crouch, isMoving);
            }
            else if (!isMoving)
            {
                moveState.SetMoveState(SurvivorLocomotionState.Idle, false);
            }
            else if (isRunning)
            {
                moveState.SetMoveState(SurvivorLocomotionState.Run, true);
            }
            else
            {
                moveState.SetMoveState(SurvivorLocomotionState.Walk, true);
            }
        }
    }

    // 다운 상태 이동
    [Server]
    private void Crawl(Vector2 moveInput, float yaw)
    {
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward = yawRot * Vector3.forward;
        Vector3 right = yawRot * Vector3.right;

        Vector3 move = forward * moveInput.y + right * moveInput.x;

        if (move.magnitude > 1f)
            move.Normalize();

        bool isMoving = move.sqrMagnitude > 0.001f;

        if (controller.isGrounded)
            yVelocity = -1f;
        else
            yVelocity += Physics.gravity.y * Time.fixedDeltaTime;

        Vector3 finalMove = move * crawlSpeed;
        finalMove.y = yVelocity;

        controller.Move(finalMove * Time.fixedDeltaTime);

        if (isMoving && modelRoot != null)
        {
            Quaternion targetRot = Quaternion.LookRotation(move);
            modelRoot.rotation = Quaternion.Slerp(
                modelRoot.rotation,
                targetRot,
                turnSpeed * Time.fixedDeltaTime
            );

            syncedModelYaw = modelRoot.eulerAngles.y;
        }

        if (moveState != null)
            moveState.SetMoveState(SurvivorLocomotionState.Crawl, isMoving);
    }

    // 일반 상태에서는 이동 방향을 바라보게 한다.
    [Server]
    private void RotateMove(Vector3 move, bool isMoving)
    {
        if (!isMoving || modelRoot == null)
            return;

        Quaternion targetRot = Quaternion.LookRotation(move);
        modelRoot.rotation = Quaternion.Slerp(
            modelRoot.rotation,
            targetRot,
            turnSpeed * Time.fixedDeltaTime
        );

        syncedModelYaw = modelRoot.eulerAngles.y;
    }

    // 카메라 스킬 중에는 카메라가 보는 앞 방향을 바라보게 한다.
    [Server]
    private void RotateCam(Vector3 dir)
    {
        if (modelRoot == null)
            return;

        dir.y = 0f;

        if (dir.sqrMagnitude <= 0.001f)
            return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
        modelRoot.rotation = Quaternion.Slerp(
            modelRoot.rotation,
            targetRot,
            turnSpeed * Time.fixedDeltaTime
        );

        syncedModelYaw = modelRoot.eulerAngles.y;
    }

    // 이동 불가 상태에서 떨어지지 않도록 중력만 처리한다.
    [Server]
    private void GravityOnly()
    {
        if (controller == null || !controller.enabled)
            return;

        if (controller.isGrounded)
            yVelocity = -1f;
        else
            yVelocity += Physics.gravity.y * Time.fixedDeltaTime;

        Vector3 gravityMove = new Vector3(0f, yVelocity, 0f);
        controller.Move(gravityMove * Time.fixedDeltaTime);
    }

    // 서기 / 앉기 컨트롤러 크기 반영
    [Server]
    private void SetSize(float height, Vector3 center)
    {
        controller.height = height;
        controller.center = center;
    }

    // 트리거 애니메이션 실행
    public void PlayAnimation(string triggerName)
    {
        if (isServer)
        {
            ApplyAnim(triggerName);
            RpcAnim(triggerName);
        }
        else if (isLocalPlayer)
        {
            CmdAnim(triggerName);
        }
    }

    [Command]
    private void CmdAnim(string triggerName)
    {
        ApplyAnim(triggerName);
        RpcAnim(triggerName);
    }

    [ClientRpc]
    private void RpcAnim(string triggerName)
    {
        if (isServer)
            return;

        ApplyAnim(triggerName);
    }

    private void ApplyAnim(string triggerName)
    {
        if (animator == null)
            return;

        animator.SetTrigger(triggerName);
    }

    // 볼트 bool 애니메이션
    public void SetVaulting(bool value)
    {
        if (isServer)
        {
            ApplyVault(value);
            RpcVault(value);
        }
        else if (isLocalPlayer)
        {
            CmdVault(value);
        }
    }

    [Command]
    private void CmdVault(bool value)
    {
        ApplyVault(value);
        RpcVault(value);
    }

    [ClientRpc]
    private void RpcVault(bool value)
    {
        if (isServer)
            return;

        ApplyVault(value);
    }

    private void ApplyVault(bool value)
    {
        if (animator != null)
            animator.SetBool("IsVaulting", value);
    }

    // searching bool 애니메이션
    public void SetSearching(bool value)
    {
        if (isServer)
        {
            ApplySearch(value);
            RpcSearch(value);
        }
        else if (isLocalPlayer)
        {
            CmdSearch(value);
        }
    }

    [Command]
    private void CmdSearch(bool value)
    {
        ApplySearch(value);
        RpcSearch(value);
    }

    [ClientRpc]
    private void RpcSearch(bool value)
    {
        if (isServer)
            return;

        ApplySearch(value);
    }

    private void ApplySearch(bool value)
    {
        if (animator != null)
            animator.SetBool("IsSearching", value);
    }

    // 카메라 스킬 상체 애니메이션 bool
    public void SetCamAnim(bool value)
    {
        if (isServer)
        {
            ApplyCamAnim(value);
            RpcCamAnim(value);
        }
        else if (isLocalPlayer)
        {
            CmdCamAnim(value);
        }
    }

    [Command]
    private void CmdCamAnim(bool value)
    {
        ApplyCamAnim(value);
        RpcCamAnim(value);
    }

    [ClientRpc]
    private void RpcCamAnim(bool value)
    {
        if (isServer)
            return;

        ApplyCamAnim(value);
    }

    private void ApplyCamAnim(bool value)
    {
        if (animator != null)
            animator.SetBool("IsCameraSkill", value);
    }

    // 스턴 bool 애니메이션
    public void SetStunned(bool value)
    {
        if (isServer)
        {
            ApplyStunned(value);
            RpcStunned(value);
        }
        else if (isLocalPlayer)
        {
            CmdStunned(value);
        }
    }

    [Command]
    private void CmdStunned(bool value)
    {
        ApplyStunned(value);
        RpcStunned(value);
    }

    [ClientRpc]
    private void RpcStunned(bool value)
    {
        if (isServer)
            return;

        ApplyStunned(value);
    }

    private void ApplyStunned(bool value)
    {
        if (animator != null)
            animator.SetBool("IsStunned", value);
    }

    // 이동 애니메이션을 즉시 Idle 쪽으로 돌릴 때 사용
    public void StopAnimation()
    {
        if (isServer)
        {
            if (moveState != null)
                moveState.SetMoveState(SurvivorLocomotionState.Idle, false);
        }
        else if (isLocalPlayer)
        {
            CmdStopAnim();
        }
    }

    [Command]
    private void CmdStopAnim()
    {
        if (moveState != null)
            moveState.SetMoveState(SurvivorLocomotionState.Idle, false);
    }

    // Animation Event에서 호출할 함수
    // Walk / Run 애니메이션의 발이 땅에 닿는 프레임에 이 함수를 이벤트로 넣는다.
    public void Footstep()
    {
        // 모든 클라이언트의 Animator에서 Animation Event가 호출될 수 있다.
        // 그래서 실제 서버 요청은 내 로컬 플레이어만 보내야 중복 재생이 안 된다.
        if (!isLocalPlayer)
            return;

        // 블렌드 트리에서 Walk / Run 이벤트가 동시에 호출될 수 있으므로
        // 너무 짧은 간격의 발소리는 무시한다.
        if (Time.time < lastFootstepTime + minFootstepInterval)
            return;

        lastFootstepTime = Time.time;

        CmdPlayFootstep();
    }

    // 로컬 플레이어가 서버에 발소리 재생을 요청한다.
    [Command]
    private void CmdPlayFootstep()
    {
        NetworkAudioManager.PlayAudioForEveryone(
            footstepKey,
            AudioDimension.Sound3D,
            transform.position
        );
    }

    [Server]
    public void BeginEscape(Transform target)
    {
        if (target == null)
            return;

        if (escapeTarget != null)
            return;

        escapeTarget = target;
        RpcApplyEscapeView();
        TargetDisableSurvivorInput(connectionToClient);

        // 기존 이동 잠금 변수 재사용
        isMoveLocked = true;

        // 입력값 제거
        serverMoveInput = Vector2.zero;
        serverWantsRun = false;
        serverWantsCrouch = false;

        // 탈출 상태 적용
        if (state != null)
            state.SetEscape();

        // 앉은 상태였다면 서 있는 크기로 복구
        SetSize(standHeight, standCenter);

        // 목표 방향 바라보기
        Vector3 dir = escapeTarget.position - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude > 0.001f)
        {
            ApplyFace(dir.normalized);
            RpcFace(dir.normalized);
        }
    }

    [ClientRpc]
    private void RpcApplyEscapeView()
    {
        if (!isLocalPlayer)
            return;

        if (camSkill != null)
            camSkill.ApplyEscapeView();
    }

    [TargetRpc]
    private void TargetDisableSurvivorInput(NetworkConnectionToClient target)
    {
        if (input != null)
            input.enabled = false;

        Debug.Log("[SurvivorMove] 탈출 플레이어 SurvivorInput 비활성화");
    }

    [Server]
    private void EscapeMoveTick()
    {
        Vector3 toTarget = escapeTarget.position - transform.position;
        toTarget.y = 0f;

        if (toTarget.magnitude <= 0.25f)
        {
            EscapeArrive();
            return;
        }

        Vector3 moveDir = toTarget.normalized;

        if (controller.isGrounded)
            yVelocity = -1f;
        else
            yVelocity += Physics.gravity.y * Time.fixedDeltaTime;

        float speed = runSpeed;

        if (state != null && state.IsDowned)
            speed = crawlSpeed;

        Vector3 finalMove = moveDir * speed;
        finalMove.y = yVelocity;

        controller.Move(finalMove * Time.fixedDeltaTime);

        if (modelRoot != null)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            modelRoot.rotation = Quaternion.Slerp(
                modelRoot.rotation,
                targetRot,
                turnSpeed * Time.fixedDeltaTime
            );

            syncedModelYaw = modelRoot.eulerAngles.y;
        }

        if (moveState != null)
            moveState.SetMoveState(SurvivorLocomotionState.Run, true);
    }

    [Server]
    private void EscapeArrive()
    {
        escapeTarget = null;

        serverMoveInput = Vector2.zero;
        serverWantsRun = false;
        serverWantsCrouch = false;

        isMoveLocked = true;

        if (moveState != null)
            moveState.SetMoveState(SurvivorLocomotionState.Idle, false);

        Debug.Log("[SurvivorMove] Escape arrived.");
    }
}