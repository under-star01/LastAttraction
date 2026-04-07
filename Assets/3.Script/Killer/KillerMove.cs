using UnityEngine;

public class KillerMove : MonoBehaviour
{
    [Header("Settings")]
    public float moveSpeed = 5f;
    public float lookSensitivity = 0.2f;
    public float lungeMultiplier = 1.6f;
    public float penaltyMultiplier = 0.4f;

    [Header("Camera")]
    public Transform killerCamera;
    public Transform cameraTarget;
    public float followSpeed = 25f;

    private CharacterController controller;
    private KillerInput input;
    private KillerState state;
    private Animator animator; // 애니메이터 참조 추가
    private float cameraPitch;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        input = GetComponent<KillerInput>();
        state = GetComponent<KillerState>();

        // 자식 오브젝트에서 애니메이터를 찾아옵니다.
        animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (state.CanMove) HandleMovement();
        if (state.CanLook) HandleLook();

        // 매 프레임 애니메이션 파라미터를 업데이트합니다.
        UpdateAnimation();
    }

    private void HandleMovement()
    {
        // [추가] 컨트롤러가 꺼져 있다면(판자 상호작용 중 등) 이동 로직을 실행하지 않음
    if (controller == null || !controller.enabled) 
    {
        return; 
    }
        float speed = moveSpeed;
        if (state.CurrentCondition == KillerCondition.Lunging) speed *= lungeMultiplier;
        else if (state.CurrentCondition == KillerCondition.Recovering) speed *= penaltyMultiplier;

        Vector3 move = transform.right * input.Move.x + transform.forward * input.Move.y;
        // 이동 방향 벡터가 0이 아닐 때만 실제 이동 수행
        if (move.sqrMagnitude > 0.001f)
        {
            controller.Move(move * speed * Time.deltaTime);
        }
    }

    private void HandleLook()
    {
        transform.Rotate(Vector3.up * input.Look.x * lookSensitivity);
        cameraPitch = Mathf.Clamp(cameraPitch - input.Look.y * lookSensitivity, -80f, 80f);
        killerCamera.localRotation = Quaternion.Euler(cameraPitch, 0, 0);

        if (cameraTarget != null)
            killerCamera.position = Vector3.Lerp(killerCamera.position, cameraTarget.position, Time.deltaTime * followSpeed);
    }

    // --- [추가된 부분] ---
    private void UpdateAnimation()
    {
        if (animator == null) return;

        float targetSpeed = 0f;

        // 평상시나 런지 중일 때만 WASD 입력을 애니메이션에 반영
        if (state.CurrentCondition == KillerCondition.Idle ||
            state.CurrentCondition == KillerCondition.Lunging)
        {
            targetSpeed = input.Move.magnitude;
        }
        // 그 외(Hit, Breaking, Vaulting)에는 강제로 0을 전달하여 걷기 모션 차단
        animator.SetFloat("Speed", targetSpeed, 0.1f, Time.deltaTime);
    }
}