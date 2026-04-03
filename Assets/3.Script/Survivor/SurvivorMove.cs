using Mirror;
using UnityEngine;

public class SurvivorMove : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Transform cameraYawRoot;   // 좌우 회전용 루트
    [SerializeField] private Transform cameraPitchRoot; // 상하 회전용 루트
    [SerializeField] private Camera playerCamera;       // 로컬 플레이어 카메라
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

    // 로컬 카메라용
    private float localYaw;
    private float localPitch;

    // 서버 이동용
    private float yVelocity;
    private bool isMoveLocked;

    // 원격 플레이어가 바라보는 방향 표시용
    [SyncVar] private float syncedYaw;
    [SyncVar] private float syncedPitch;

    // 서버가 계산한 상태를 원격에게도 반영
    [SyncVar] private float syncedMoveSpeed;
    [SyncVar] private bool syncedIsCrouching;
    [SyncVar] private bool syncedIsDowned;

    public void SetMoveLock(bool value)
    {
        isMoveLocked = value;
    }

    public void FaceDirection(Vector3 dir)
    {
        dir.y = 0f;

        if (dir.sqrMagnitude <= 0.001f)
            return;

        if (isServer)
        {
            ApplyFaceDirection(dir.normalized);
            RpcFaceDirection(dir.normalized);
        }
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
        if (isServer)
            return;

        ApplyFaceDirection(dir.normalized);
    }

    private void ApplyFaceDirection(Vector3 dir)
    {
        if (modelRoot != null)
            modelRoot.rotation = Quaternion.LookRotation(dir);
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
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        if (playerCamera != null)
            playerCamera.enabled = true;

        AudioListener listener = GetComponentInChildren<AudioListener>();
        if (listener != null)
            listener.enabled = true;

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

            AudioListener listener = GetComponentInChildren<AudioListener>();
            if (listener != null)
                listener.enabled = false;
        }
    }

    private void Update()
    {
        if (isLocalPlayer)
        {
            UpdateLocalLook();
            SendMoveInputToServer();
            ApplyLocalCamera();
        }
        else
        {
            ApplyRemoteLook();
            ApplyRemoteAnimator();
        }
    }

    private void UpdateLocalLook()
    {
        if (input == null)
            return;

        Vector2 look = input.Look;

        localYaw += look.x * mouseSensitivity;
        localPitch -= look.y * mouseSensitivity;
        localPitch = Mathf.Clamp(localPitch, minPitch, maxPitch);
    }

    private void ApplyLocalCamera()
    {
        if (cameraYawRoot != null)
            cameraYawRoot.localRotation = Quaternion.Euler(0f, localYaw, 0f);

        if (cameraPitchRoot != null)
            cameraPitchRoot.localRotation = Quaternion.Euler(localPitch, 0f, 0f);
    }

    private void ApplyRemoteLook()
    {
        if (cameraYawRoot != null)
            cameraYawRoot.localRotation = Quaternion.Euler(0f, syncedYaw, 0f);

        if (cameraPitchRoot != null)
            cameraPitchRoot.localRotation = Quaternion.Euler(syncedPitch, 0f, 0f);
    }

    private void ApplyRemoteAnimator()
    {
        if (animator == null)
            return;

        animator.SetFloat("MoveSpeed", syncedMoveSpeed, 0.1f, Time.deltaTime);
        animator.SetBool("IsCrouching", syncedIsCrouching);
        animator.SetBool("IsDowned", syncedIsDowned);
    }

    private void SendMoveInputToServer()
    {
        if (input == null)
            return;

        CmdMove(
            input.Move,
            input.IsRunning,
            input.IsCrouching,
            localYaw,
            localPitch
        );
    }

    [Command]
    private void CmdMove(Vector2 moveInput, bool wantsRun, bool wantsCrouch, float yaw, float pitch)
    {
        syncedYaw = yaw;
        syncedPitch = pitch;

        if (controller == null || !controller.enabled)
            return;

        bool isDowned = state != null && state.IsDowned;
        bool isBusy = state != null && state.IsBusy;

        if (isMoveLocked || isBusy)
        {
            ApplyGravityOnlyServer();
            UpdateAnimatorServer(0f, false, isDowned);
            return;
        }

        if (isDowned)
        {
            CrawlMoveServer(moveInput, yaw);
            return;
        }

        bool canCrouch = interactor == null || !interactor.IsInteracting;
        bool isCrouching = canCrouch && wantsCrouch;

        if (isCrouching)
            SetSizeServer(crouchHeight, crouchCenter);
        else
            SetSizeServer(standHeight, standCenter);

        MoveServer(moveInput, wantsRun, isCrouching, yaw);
    }

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
            yVelocity += Physics.gravity.y * Time.deltaTime;

        Vector3 finalMove = move * speed;
        finalMove.y = yVelocity;

        controller.Move(finalMove * Time.deltaTime);

        RotateModelServer(move, isMoving);
        UpdateAnimatorServer(animSpeed, isCrouching, false);
    }

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
            yVelocity += Physics.gravity.y * Time.deltaTime;

        Vector3 finalMove = move * crawlSpeed;
        finalMove.y = yVelocity;

        controller.Move(finalMove * Time.deltaTime);

        if (isMoving && modelRoot != null)
        {
            Quaternion targetRot = Quaternion.LookRotation(move);
            modelRoot.rotation = Quaternion.Slerp(
                modelRoot.rotation,
                targetRot,
                turnSpeed * Time.deltaTime
            );
        }

        float animSpeed = 0f;

        if (isMoving)
            animSpeed = 0.2f;

        UpdateAnimatorServer(animSpeed, false, true);
    }

    [Server]
    private void RotateModelServer(Vector3 move, bool isMoving)
    {
        if (!isMoving || modelRoot == null)
            return;

        Quaternion targetRot = Quaternion.LookRotation(move);
        modelRoot.rotation = Quaternion.Slerp(
            modelRoot.rotation,
            targetRot,
            turnSpeed * Time.deltaTime
        );
    }

    [Server]
    private void ApplyGravityOnlyServer()
    {
        if (controller == null || !controller.enabled)
            return;

        if (controller.isGrounded)
            yVelocity = -1f;
        else
            yVelocity += Physics.gravity.y * Time.deltaTime;

        Vector3 gravityMove = new Vector3(0f, yVelocity, 0f);
        controller.Move(gravityMove * Time.deltaTime);
    }

    [Server]
    private void SetSizeServer(float height, Vector3 center)
    {
        controller.height = height;
        controller.center = center;
    }

    [Server]
    private void UpdateAnimatorServer(float targetMoveSpeed, bool isCrouching, bool isDowned)
    {
        syncedMoveSpeed = targetMoveSpeed;
        syncedIsCrouching = isCrouching;
        syncedIsDowned = isDowned;

        if (animator == null)
            return;

        animator.SetFloat("MoveSpeed", targetMoveSpeed);
        animator.SetBool("IsCrouching", isCrouching);
        animator.SetBool("IsDowned", isDowned);
    }

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