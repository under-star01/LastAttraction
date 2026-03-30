using UnityEngine;

public class SurvivorMove : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private Transform cameraYawRoot;   // 좌우 회전용 루트
    [SerializeField] private Transform cameraPitchRoot; // 상하 회전용 루트
    [SerializeField] private Camera playerCamera;       // 이동 방향 기준이 되는 카메라
    [SerializeField] private Transform modelRoot;       // 플레이어 모델 회전용

    [Header("속도")]
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float runSpeed = 4f;
    [SerializeField] private float crouchSpeed = 1f;
    [SerializeField] private float turnSpeed = 15f;

    [Header("카메라")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float minPitch = -60f;
    [SerializeField] private float maxPitch = 60f;

    [Header("앉기")]
    [SerializeField] private float standHeight = 1.8f;
    [SerializeField] private float crouchHeight = 1.1f;

    private CharacterController controller;
    private SurvivorInput input;

    private float cameraYaw;     // 좌우 회전 값
    private float pitch;         // 상하 회전 값
    private float yVelocity;     // 중력용 y속도

    // true면 이동만 막힘
    // 중요한 점: Look()는 계속 돌기 때문에 마우스 회전은 살아 있음
    private bool isMoveLocked;

    // 외부(증거조사, 판자 등)에서 이동 잠금/해제할 때 사용
    public void SetMoveLock(bool value)
    {
        isMoveLocked = value;
    }

    // 플레이어 모델을 특정 방향으로 바라보게 함
    public void FaceDirection(Vector3 dir)
    {
        dir.y = 0f;

        if (dir.sqrMagnitude <= 0.001f)
            return;

        if (modelRoot != null)
            modelRoot.rotation = Quaternion.LookRotation(dir.normalized);
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        input = GetComponent<SurvivorInput>();

        controller.height = standHeight;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        // 이동이 막혀도 마우스 회전은 항상 가능하게 유지
        Look();

        // 이동 잠금 상태면 걷기/달리기/앉기는 막고 중력만 적용
        if (isMoveLocked)
        {
            ApplyGravityOnly();
            return;
        }

        Move();
        Crouch();
    }

    // 마우스로 카메라 회전
    private void Look()
    {
        Vector2 look = input.Look;

        cameraYaw += look.x * mouseSensitivity;
        pitch -= look.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        if (cameraYawRoot != null)
            cameraYawRoot.localRotation = Quaternion.Euler(0f, cameraYaw, 0f);

        if (cameraPitchRoot != null)
            cameraPitchRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    // 이동 처리
    private void Move()
    {
        Vector2 moveInput = input.Move;

        // 카메라 기준 앞/오른쪽 방향으로 이동
        Vector3 forward = playerCamera.transform.forward;
        Vector3 right = playerCamera.transform.right;

        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        Vector3 move = forward * moveInput.y + right * moveInput.x;

        if (move.magnitude > 1f)
            move.Normalize();

        // 상태에 따라 속도 결정
        float speed = walkSpeed;

        if (input.IsCrouching)
            speed = crouchSpeed;
        else if (input.IsRunning)
            speed = runSpeed;

        // 중력 처리
        if (controller.isGrounded)
            yVelocity = -1f;
        else
            yVelocity += Physics.gravity.y * Time.deltaTime;

        Vector3 finalMove = move;
        finalMove.y = yVelocity;

        controller.Move(finalMove * speed * Time.deltaTime);

        // 움직이는 방향으로 모델 회전
        if (move.sqrMagnitude > 0.001f && modelRoot != null)
        {
            Quaternion targetRot = Quaternion.LookRotation(move);

            modelRoot.rotation = Quaternion.Slerp(
                modelRoot.rotation,
                targetRot,
                turnSpeed * Time.deltaTime
            );
        }
    }

    // 이동 잠금 상태에서도 바닥에 붙고 낙하는 되게 하기 위함
    private void ApplyGravityOnly()
    {
        if (controller.isGrounded)
            yVelocity = -1f;
        else
            yVelocity += Physics.gravity.y * Time.deltaTime;

        Vector3 gravityMove = new Vector3(0f, yVelocity, 0f);
        controller.Move(gravityMove * Time.deltaTime);
    }

    // 앉기 높이 변경
    private void Crouch()
    {
        if (input.IsCrouching)
            controller.height = crouchHeight;
        else
            controller.height = standHeight;
    }
}