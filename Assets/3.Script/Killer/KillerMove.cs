using Mirror;
using UnityEngine;

public class KillerMove : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Transform killerCamera;       // 상하 회전용 카메라
    [SerializeField] private Transform cameraTarget;       // 카메라가 따라갈 위치
    [SerializeField] private Animator animator;
    [SerializeField] private AudioListener audioListener;

    [Header("속도 설정")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float lungeMultiplier = 1.6f;
    [SerializeField] private float penaltyMultiplier = 0.4f;
    [SerializeField] private float lookSensitivity = 0.2f;
    [SerializeField] private float followSpeed = 25f;

    private CharacterController controller;
    private KillerInput input;
    private KillerState state;

    // 로컬 카메라 회전값
    private float localYaw;
    private float localPitch;
    private float yVelocity;

    // 서버 입력 저장용
    private Vector2 serverMoveInput;
    private float serverYaw;

    // 동기화 변수
    [SyncVar] private float syncedYaw;
    [SyncVar] private float syncedPitch;
    [SyncVar] private float syncedMoveSpeed;

    public float SyncedMoveSpeed => syncedMoveSpeed;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        input = GetComponent<KillerInput>();
        state = GetComponent<KillerState>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // 초기 상태에서 카메라/오디오 리스너 비활성화
        if (killerCamera != null) killerCamera.gameObject.SetActive(false);
        if (audioListener != null) audioListener.enabled = false;
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // 내 킬러만 카메라와 리스너 활성화
        if (killerCamera != null) killerCamera.gameObject.SetActive(true);
        if (audioListener != null) audioListener.enabled = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (isLocalPlayer)
        {
            // 1. 로컬 시야 회전 처리 (CanLook 상태일 때만)
            if (state.CanLook)
            {
                UpdateLocalLook();
                ApplyLocalCamera();
            }

            // 2. 입력값 서버 전송
            SendInputToServer();
        }
        else
        {
            // 3. 원격 플레이어 동기화
            ApplyRemoteLook();
            //ApplyRemoteAnimator();
        }
    }

    private void FixedUpdate()
    {
        // 실제 물리 이동은 서버에서만 처리
        if (isServer)
        {
            ServerTickMovement();
        }
    }

    private void UpdateLocalLook()
    {
        Vector2 lookInput = input.Look;
        localYaw += lookInput.x * lookSensitivity;
        localPitch = Mathf.Clamp(localPitch - lookInput.y * lookSensitivity, -80f, 80f);
    }

    private void ApplyLocalCamera()
    {
        // 킬러는 몸체가 Yaw에 따라 회전함
        transform.rotation = Quaternion.Euler(0f, localYaw, 0f);
        // 카메라는 Pitch만 담당
        killerCamera.localRotation = Quaternion.Euler(localPitch, 0f, 0f);

        if (cameraTarget != null)
            killerCamera.position = Vector3.Lerp(killerCamera.position, cameraTarget.position, Time.deltaTime * followSpeed);
    }

    private void ApplyRemoteLook()
    {
        // 타인 화면에서는 동기화된 값 적용
        transform.rotation = Quaternion.Euler(0f, syncedYaw, 0f);
        killerCamera.localRotation = Quaternion.Euler(syncedPitch, 0f, 0f);
    }

    private void SendInputToServer()
    {
        CmdSetMoveInput(input.Move, localYaw, localPitch);
    }

    [Command]
    private void CmdSetMoveInput(Vector2 moveInput, float yaw, float pitch)
    {
        serverMoveInput = moveInput;
        serverYaw = yaw;

        // 동기화 변수 갱신
        syncedYaw = yaw;
        syncedPitch = pitch;
    }

    [Server]
    private void ServerTickMovement()
    {
        if (controller == null || !controller.enabled) return;

        // 이동 가능 상태가 아니면 중력만 적용
        if (!state.CanMove)
        {
            ApplyGravityOnlyServer();
            //UpdateAnimatorServer(0f);
            syncedMoveSpeed = 0f;
            return;
        }

        MoveServer(serverMoveInput, serverYaw);
    }

    [Server]
    private void MoveServer(Vector2 moveInput, float yaw)
    {
        // 킬러의 현재 상태에 따른 속도 계산
        float speed = moveSpeed;
        if (state.CurrentCondition == KillerCondition.Lunging) speed *= lungeMultiplier;
        else if (state.CurrentCondition == KillerCondition.Recovering) speed *= penaltyMultiplier;

        // 회전값 적용
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;

        if (move.magnitude > 1f) move.Normalize();

        // 중력 처리
        if (controller.isGrounded) yVelocity = -1f;
        else yVelocity += Physics.gravity.y * Time.fixedDeltaTime;

        Vector3 finalMove = move * speed;
        finalMove.y = yVelocity;

        controller.Move(finalMove * Time.fixedDeltaTime);

        // 애니메이션 속도 동기화
        //UpdateAnimatorServer(moveInput.magnitude);
        syncedMoveSpeed = moveInput.magnitude;
    }

    [Server]
    private void ApplyGravityOnlyServer()
    {
        if (controller.isGrounded) yVelocity = -1f;
        else yVelocity += Physics.gravity.y * Time.fixedDeltaTime;

        controller.Move(new Vector3(0, yVelocity, 0) * Time.fixedDeltaTime);
    }
}