using Mirror;
using UnityEngine;

public class SurvivorMove : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Transform cameraYawRoot;
    [SerializeField] private Transform cameraPitchRoot;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener playerListener;
    [SerializeField] private Transform modelRoot;
    [SerializeField] private Animator animator;

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
    private SurvivorActionState act;
    private SurvivorMoveState moveState;
    private SurvivorCameraSkill camSkill;

    private float localYaw;
    private float localPitch;
    private float yVelocity;

    private bool isMoveLocked;

    private Vector2 serverMoveInput;
    private bool serverWantsRun;
    private bool serverWantsCrouch;
    private float serverYaw;
    private float serverPitch;

    [SyncVar] private float syncedYaw;
    [SyncVar] private float syncedPitch;
    [SyncVar] private float syncedModelYaw;

    // 외부 스크립트가 이동을 잠글 때 사용
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

    // 외부에서 특정 방향을 바라보게 할 때 사용
    // 예: 상호작용 시작 전에 오브젝트 쪽 정렬
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

        if (playerCamera != null)
            playerCamera.enabled = false;

        if (playerListener != null)
            playerListener.enabled = false;
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

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
        if (input == null)
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

        syncedYaw = yaw;
        syncedPitch = pitch;
    }

    // 서버에서 실제 이동 처리
    [Server]
    private void MoveTick()
    {
        if (controller == null || !controller.enabled)
            return;

        bool isDowned = state != null && state.IsDowned;
        bool isDead = state != null && state.IsDead;
        bool isBusy = act != null && act.IsBusy;

        // 움직일 수 없으면 중력만 적용
        if (isMoveLocked || isBusy || isDead)
        {
            GravityOnly();

            if (moveState != null)
                moveState.SetMoveState(SurvivorLocomotionState.Idle, false);

            return;
        }

        // 다운 상태는 crawl 이동
        if (isDowned)
        {
            Crawl(serverMoveInput, serverYaw);
            return;
        }

        // Hold 상호작용 중에는 새로 앉기 시작 금지
        bool canCrouch = interactor == null || !interactor.IsInteracting;
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
        bool isRunning = isMoving && !isCrouching && wantsRun;

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

        // 카메라 스킬 중이면 몸을 카메라 정면 방향으로 유지
        // camSkill 또는 actionState 둘 중 하나라도 true면 스킬 상태로 본다.
        bool useCamSkill = false;

        if (camSkill != null && camSkill.IsUse)
            useCamSkill = true;

        if (act != null && act.IsCamSkill)
            useCamSkill = true;

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
            if (!isMoving)
                moveState.SetMoveState(SurvivorLocomotionState.Idle, false);
            else if (isCrouching)
                moveState.SetMoveState(SurvivorLocomotionState.Crouch, true);
            else if (isRunning)
                moveState.SetMoveState(SurvivorLocomotionState.Run, true);
            else
                moveState.SetMoveState(SurvivorLocomotionState.Walk, true);
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

    // 일반 상태에서는 이동 방향을 바라보게 함
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

    // 카메라 스킬 중에는 카메라가 보는 앞 방향을 바라보게 함
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

    // 이동 불가 상태에서 떨어지지 않도록 중력만 처리
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

    // 이동 애니메이션을 즉시 idle 쪽으로 돌릴 때 사용
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
}