using Mirror;
using UnityEngine;

public class KillerMove : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private GameObject killerCamera; // Transform 대신 Camera 컴포넌트로 변경 권장
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private Animator animator;
    [SerializeField] private AudioListener audioListener;

    [Header("속도 설정")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float lungeMultiplier = 1.6f;
    [SerializeField] private float penaltyMultiplier = 0.4f;
    [SerializeField] private float rageSpeedMultiplier = 1.1f;
    [SerializeField] private float lookSensitivity = 0.2f;
    [SerializeField] private float followSpeed = 25f;

    private CharacterController controller;
    private KillerInput input;
    private KillerState state;

    private float localYaw, localPitch, yVelocity;
    private Vector2 serverMoveInput;
    private float serverYaw;

    [SyncVar] private float syncedYaw, syncedPitch, syncedMoveSpeed;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        input = GetComponent<KillerInput>();
        state = GetComponent<KillerState>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        // [중요] 프리팹이 생성되는 순간 모든 카메라와 오디오를 끕니다.
        if (killerCamera != null)
        {
            killerCamera.SetActive(false);
            killerCamera.gameObject.tag = "Untagged"; // MainCamera 태그 분쟁 방지
        }
        if (audioListener != null) audioListener.enabled = false;
    }

    public override void OnStartLocalPlayer()
    {
        // 내 킬러만 카메라와 오디오 활성화
        if (killerCamera != null)
        {
            killerCamera.SetActive(true);
            killerCamera.gameObject.tag = "MainCamera";
        }
        if (audioListener != null) audioListener.enabled = true;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (isLocalPlayer)
        {
            if (state.CanLook) UpdateLocalLook();
            CmdSetMoveInput(input.Move, localYaw, localPitch);

            if (animator != null)
                animator.SetFloat("Speed", input.Move.magnitude, 0.1f, Time.deltaTime);
        }
        else
        {
            ApplyRemoteLook();
            if (animator != null)
                animator.SetFloat("Speed", syncedMoveSpeed, 0.1f, Time.deltaTime);
        }
    }

    private void FixedUpdate() { if (isServer) ServerTickMovement(); }

    private void UpdateLocalLook()
    {
        localYaw += input.Look.x * lookSensitivity;
        localPitch = Mathf.Clamp(localPitch - input.Look.y * lookSensitivity, -80f, 80f);
        transform.rotation = Quaternion.Euler(0f, localYaw, 0f);
        killerCamera.transform.localRotation = Quaternion.Euler(localPitch, 0f, 0f);

        if (cameraTarget != null)
            killerCamera.transform.position = Vector3.Lerp(killerCamera.transform.position, cameraTarget.position, Time.deltaTime * followSpeed);
    }

    private void ApplyRemoteLook()
    {
        transform.rotation = Quaternion.Euler(0f, syncedYaw, 0f);
        killerCamera.transform.localRotation = Quaternion.Euler(syncedPitch, 0f, 0f);
    }

    [Command]
    private void CmdSetMoveInput(Vector2 move, float yaw, float pitch)
    {
        serverMoveInput = move; serverYaw = yaw; syncedYaw = yaw; syncedPitch = pitch;
    }

    [Server]
    private void ServerTickMovement()
    {
        if (controller == null || !controller.enabled) return;

        if (!state.CanMove)
        {
            ApplyGravityOnlyServer();
            syncedMoveSpeed = 0f;
            return;
        }

        float speed = moveSpeed;
        if (state.IsRaging) speed *= rageSpeedMultiplier;
        if (state.CurrentCondition == KillerCondition.Lunging) speed *= lungeMultiplier;
        else if (state.CurrentCondition == KillerCondition.Recovering) speed *= penaltyMultiplier;

        transform.rotation = Quaternion.Euler(0f, serverYaw, 0f);
        Vector3 moveDir = transform.right * serverMoveInput.x + transform.forward * serverMoveInput.y;
        if (moveDir.magnitude > 1f) moveDir.Normalize();

        if (controller.isGrounded) yVelocity = -1f;
        else yVelocity += Physics.gravity.y * Time.fixedDeltaTime;

        Vector3 finalMove = moveDir * speed;
        finalMove.y = yVelocity;
        controller.Move(finalMove * Time.fixedDeltaTime);

        syncedMoveSpeed = serverMoveInput.magnitude;
    }

    [Server]
    private void ApplyGravityOnlyServer()
    {
        if (controller.isGrounded) yVelocity = -1f;
        else yVelocity += Physics.gravity.y * Time.fixedDeltaTime;
        controller.Move(new Vector3(0, yVelocity, 0) * Time.fixedDeltaTime);
    }
}