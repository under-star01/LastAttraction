using Mirror;
using UnityEngine;

public class SurvivorMove : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Transform cameraYawRoot;   // 좌우 회전용 루트
    [SerializeField] private Transform cameraPitchRoot; // 상하 회전용 루트
    [SerializeField] private Camera playerCamera;       // 로컬 플레이어 카메라
    [SerializeField] private AudioListener playerListener; // 로컬 플레이어 오디오 리스너
    [SerializeField] private Transform modelRoot;       // 캐릭터 모델 회전용
    [SerializeField] private Animator animator;         // 애니메이터

    [Header("속도")]
    [SerializeField] private float walkSpeed = 2.3f;
    [SerializeField] private float runSpeed = 4f;
    [SerializeField] private float crouchSpeed = 1.2f;
    [SerializeField] private float crawlSpeed = 0.45f;
    [SerializeField] private float turnSpeed = 15f;

    [Header("카메라")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float minPitch = -60f;
    [SerializeField] private float maxPitch = 60f;

    [Header("컨트롤러 높이")]
    [SerializeField] private float standHeight = 1.8f;
    [SerializeField] private Vector3 standCenter = new Vector3(0f, 0.9f, 0f);
    [SerializeField] private float crouchHeight = 0.9f;
    [SerializeField] private Vector3 crouchCenter = new Vector3(0f, 0.45f, 0f);

    private CharacterController controller;
    private SurvivorInput input;
    private SurvivorInteractor interactor;
    private SurvivorState state;

    // 로컬 카메라 회전값
    private float localYaw;
    private float localPitch;

    // 서버 실제 이동 처리용 값
    private float yVelocity;

    // 이동 잠금 여부
    // 중요:
    // 이동은 서버에서 처리하므로 이 값도 서버 쪽에 반영되어야 진짜로 움직임이 막힌다.
    private bool isMoveLocked;

    // 클라이언트가 서버에 보낸 최신 입력값 저장용
    // 서버는 FixedUpdate에서 이 값을 사용해서 실제 이동을 처리한다.
    private Vector2 serverMoveInput;
    private bool serverWantsRun;
    private bool serverWantsCrouch;
    private float serverYaw;
    private float serverPitch;

    // 원격 플레이어 표시용 동기화 값
    [SyncVar] private float syncedYaw;
    [SyncVar] private float syncedPitch;
    [SyncVar] private float syncedModelYaw;

    [SyncVar] private float syncedMoveSpeed;
    [SyncVar] private bool syncedIsCrouching;
    [SyncVar] private bool syncedIsDowned;

    // 외부에서 이동 잠금 on/off 할 때 호출
    public void SetMoveLock(bool value)
    {
        // 로컬에서는 즉시 반영
        // 그래야 체감상 바로 잠기는 느낌이 난다.
        isMoveLocked = value;

        // 클라이언트 플레이어라면 서버에도 전달
        // 이게 없으면 서버는 계속 이동 허용 상태라 실제로는 움직여질 수 있다.
        if (isLocalPlayer && !isServer)
        {
            CmdSetMoveLock(value);
        }
    }

    // 로컬에서 요청한 이동 잠금을 서버에도 반영
    [Command]
    private void CmdSetMoveLock(bool value)
    {
        isMoveLocked = value;
    }

    // 플레이어를 특정 방향으로 바라보게 할 때 사용
    public void FaceDirection(Vector3 dir)
    {
        dir.y = 0f;

        if (dir.sqrMagnitude <= 0.001f)
            return;

        // 서버면 바로 처리 후 전체 클라에 반영
        if (isServer)
        {
            ApplyFaceDirection(dir.normalized);
            RpcFaceDirection(dir.normalized);
        }
        // 로컬 플레이어면 서버에 요청
        else if (isLocalPlayer)
        {
            CmdFaceDirection(dir.normalized);
        }
    }

    [Command]
    private void CmdFaceDirection(Vector3 dir)
    {
        if (dir.sqrMagnitude <= 0.001f)
            return;

        ApplyFaceDirection(dir.normalized);
        RpcFaceDirection(dir.normalized);
    }

    [ClientRpc]
    private void RpcFaceDirection(Vector3 dir)
    {
        // 서버는 이미 직접 적용했으니 중복 적용 방지
        if (isServer)
            return;

        ApplyFaceDirection(dir.normalized);
    }

    // 실제 방향 적용
    private void ApplyFaceDirection(Vector3 dir)
    {
        if (modelRoot != null)
        {
            modelRoot.rotation = Quaternion.LookRotation(dir);
            syncedModelYaw = modelRoot.eulerAngles.y;
        }
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        input = GetComponent<SurvivorInput>();
        interactor = GetComponent<SurvivorInteractor>();
        state = GetComponent<SurvivorState>();

        if (animator == null && modelRoot != null)
            animator = modelRoot.GetComponentInChildren<Animator>();

        if (animator != null)
            animator.applyRootMotion = false;

        // 시작할 때는 모든 카메라/리스너 꺼두고
        // 로컬 플레이어일 때만 켜준다.
        if (playerCamera != null)
            playerCamera.enabled = false;

        if (playerListener != null)
            playerListener.enabled = false;
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // 내 플레이어만 카메라/오디오 사용
        if (playerCamera != null)
            playerCamera.enabled = true;

        if (playerListener != null)
            playerListener.enabled = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 내 플레이어가 아니면 카메라/오디오 꺼둠
        if (!isLocalPlayer)
        {
            if (playerCamera != null)
                playerCamera.enabled = false;

            if (playerListener != null)
                playerListener.enabled = false;
        }
    }

    private void Update()
    {
        if (isLocalPlayer)
        {
            // 로컬 플레이어는 카메라 회전 처리 + 입력 서버 전송
            UpdateLocalLook();
            SendInputToServer();
            ApplyLocalCamera();
            ApplyModelRotation();
        }
        else
        {
            // 원격 플레이어는 동기화된 값으로만 표시
            ApplyRemoteLook();
            ApplyRemoteAnimator();
            ApplyModelRotation();
        }
    }

    private void FixedUpdate()
    {
        // 실제 이동은 서버에서만 처리
        if (!isServer)
            return;

        ServerTickMovement();
    }

    // 로컬 마우스 회전값 누적
    private void UpdateLocalLook()
    {
        if (input == null)
            return;

        Vector2 look = input.Look;

        localYaw += look.x * mouseSensitivity;
        localPitch -= look.y * mouseSensitivity;
        localPitch = Mathf.Clamp(localPitch, minPitch, maxPitch);
    }

    // 로컬 카메라 적용
    private void ApplyLocalCamera()
    {
        if (cameraYawRoot != null)
            cameraYawRoot.localRotation = Quaternion.Euler(0f, localYaw, 0f);

        if (cameraPitchRoot != null)
            cameraPitchRoot.localRotation = Quaternion.Euler(localPitch, 0f, 0f);
    }

    // 원격 플레이어 회전 표시
    private void ApplyRemoteLook()
    {
        if (cameraYawRoot != null)
            cameraYawRoot.localRotation = Quaternion.Euler(0f, syncedYaw, 0f);

        if (cameraPitchRoot != null)
            cameraPitchRoot.localRotation = Quaternion.Euler(syncedPitch, 0f, 0f);
    }

    // 모델 회전 적용
    private void ApplyModelRotation()
    {
        if (modelRoot == null)
            return;

        Vector3 euler = modelRoot.eulerAngles;
        euler.y = syncedModelYaw;
        modelRoot.eulerAngles = euler;
    }

    // 원격 플레이어 애니메이터 적용
    private void ApplyRemoteAnimator()
    {
        if (animator == null)
            return;

        // MoveSpeed는 약간 부드럽게 보간
        animator.SetFloat("MoveSpeed", syncedMoveSpeed, 0.1f, Time.deltaTime);
        animator.SetBool("IsCrouching", syncedIsCrouching);
        animator.SetBool("IsDowned", syncedIsDowned);
    }

    // 로컬 입력을 서버에 전송
    private void SendInputToServer()
    {
        if (input == null)
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

        // 원격 플레이어 표시용 회전값 동기화
        syncedYaw = yaw;
        syncedPitch = pitch;
    }

    // 서버에서 실제 이동 처리
    [Server]
    private void ServerTickMovement()
    {
        if (controller == null || !controller.enabled)
            return;

        bool isDowned = state != null && state.IsDowned;
        bool isBusy = state != null && state.IsBusy;
        bool isImprisoned = state != null && state.IsImprisoned;

        // 이동 잠김 상태거나 다운 연출 중이거나 감옥 상태면 이동 금지
        // 이동만 막고 마우스는 됨
        if (isMoveLocked || isBusy || isImprisoned)
        {
            ApplyGravityOnlyServer();
            UpdateAnimatorServer(0f, false, isDowned);
            return;
        }

        // 다운 상태면 기어가기 이동
        if (isDowned)
        {
            CrawlMoveServer(serverMoveInput, serverYaw);
            return;
        }

        // 상호작용 중이면 앉기 금지
        bool canCrouch = interactor == null || !interactor.IsInteracting;
        bool isCrouching = canCrouch && serverWantsCrouch;

        // 현재 자세에 맞게 컨트롤러 높이 변경
        if (isCrouching)
            SetSizeServer(crouchHeight, crouchCenter);
        else
            SetSizeServer(standHeight, standCenter);

        MoveServer(serverMoveInput, serverWantsRun, isCrouching, serverYaw);
    }

    // 일반 이동
    [Server]
    private void MoveServer(Vector2 moveInput, bool wantsRun, bool isCrouching, float yaw)
    {
        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward = yawRotation * Vector3.forward;
        Vector3 right = yawRotation * Vector3.right;

        Vector3 move = forward * moveInput.y + right * moveInput.x;

        if (move.magnitude > 1f)
            move.Normalize();

        bool isMoving = move.sqrMagnitude > 0.001f;
        bool isRunning = isMoving && !isCrouching && wantsRun;

        float speed = walkSpeed;
        float animSpeed = 0f;

        if (isCrouching)
        {
            speed = crouchSpeed;

            if (isMoving)
                animSpeed = 0.25f;
        }
        else if (isRunning)
        {
            speed = runSpeed;
            animSpeed = 1f;
        }
        else if (isMoving)
        {
            speed = walkSpeed;
            animSpeed = 0.5f;
        }

        if (controller.isGrounded)
            yVelocity = -1f;
        else
            yVelocity += Physics.gravity.y * Time.fixedDeltaTime;

        Vector3 finalMove = move * speed;
        finalMove.y = yVelocity;

        controller.Move(finalMove * Time.fixedDeltaTime);

        RotateModelServer(move, isMoving);
        UpdateAnimatorServer(animSpeed, isCrouching, false);
    }

    // 다운 상태 이동
    [Server]
    private void CrawlMoveServer(Vector2 moveInput, float yaw)
    {
        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward = yawRotation * Vector3.forward;
        Vector3 right = yawRotation * Vector3.right;

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

        float animSpeed = 0f;

        if (isMoving)
            animSpeed = 0.2f;

        UpdateAnimatorServer(animSpeed, false, true);
    }

    // 이동 방향으로 모델 회전
    [Server]
    private void RotateModelServer(Vector3 move, bool isMoving)
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

    // 이동은 못 하더라도 중력은 계속 적용
    [Server]
    private void ApplyGravityOnlyServer()
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

    // 현재 자세에 맞게 CharacterController 크기 변경
    [Server]
    private void SetSizeServer(float height, Vector3 center)
    {
        controller.height = height;
        controller.center = center;
    }

    // 서버에서 애니메이터 상태 갱신 + 원격 표시용 SyncVar 갱신
    [Server]
    private void UpdateAnimatorServer(float targetMoveSpeed, bool isCrouching, bool isDowned)
    {
        syncedMoveSpeed = targetMoveSpeed;
        syncedIsCrouching = isCrouching;
        syncedIsDowned = isDowned;

        if (animator == null)
            return;

        animator.SetFloat("MoveSpeed", targetMoveSpeed, 0.1f, Time.deltaTime);
        animator.SetBool("IsCrouching", isCrouching);
        animator.SetBool("IsDowned", isDowned);
    }

    // 애니메이션 트리거 실행
    public void PlayAnimation(string triggerName)
    {
        if (isServer)
        {
            ApplyPlayAnimation(triggerName);
            RpcPlayAnimation(triggerName);
        }
        else if (isLocalPlayer)
        {
            CmdPlayAnimation(triggerName);
        }
    }

    [Command]
    private void CmdPlayAnimation(string triggerName)
    {
        ApplyPlayAnimation(triggerName);
        RpcPlayAnimation(triggerName);
    }

    [ClientRpc]
    private void RpcPlayAnimation(string triggerName)
    {
        if (isServer)
            return;

        ApplyPlayAnimation(triggerName);
    }

    private void ApplyPlayAnimation(string triggerName)
    {
        if (animator == null)
            return;

        animator.SetTrigger(triggerName);
    }

    // 볼트 상태 on/off
    public void SetVaulting(bool value)
    {
        if (isServer)
        {
            ApplySetVaulting(value);
            RpcSetVaulting(value);
        }
        else if (isLocalPlayer)
        {
            CmdSetVaulting(value);
        }
    }

    [Command]
    private void CmdSetVaulting(bool value)
    {
        ApplySetVaulting(value);
        RpcSetVaulting(value);
    }

    [ClientRpc]
    private void RpcSetVaulting(bool value)
    {
        if (isServer)
            return;

        ApplySetVaulting(value);
    }

    private void ApplySetVaulting(bool value)
    {
        if (animator != null)
            animator.SetBool("IsVaulting", value);
    }

    // 조사/힐 같은 searching 상태 on/off
    public void SetSearching(bool value)
    {
        if (isServer)
        {
            ApplySetSearching(value);
            RpcSetSearching(value);
        }
        else if (isLocalPlayer)
        {
            CmdSetSearching(value);
        }
    }

    [Command]
    private void CmdSetSearching(bool value)
    {
        ApplySetSearching(value);
        RpcSetSearching(value);
    }

    [ClientRpc]
    private void RpcSetSearching(bool value)
    {
        if (isServer)
            return;

        ApplySetSearching(value);
    }

    private void ApplySetSearching(bool value)
    {
        if (animator != null)
            animator.SetBool("IsSearching", value);
    }

    // 이동 애니메이션 정지
    public void StopAnimation()
    {
        bool isDowned = state != null && state.IsDowned;

        if (isServer)
        {
            UpdateAnimatorServer(0f, false, isDowned);
        }
        else if (isLocalPlayer)
        {
            CmdStopAnimation(isDowned);
        }
    }

    [Command]
    private void CmdStopAnimation(bool isDowned)
    {
        UpdateAnimatorServer(0f, false, isDowned);
    }
}