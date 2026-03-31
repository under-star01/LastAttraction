using UnityEngine;
using UnityEngine.InputSystem; // 뉴 인풋 시스템 사용을 위해 필수!

public class KillerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float lookSensitivity = 0.2f;

    private CharacterController controller;
    private KillerInput killerInput; // 생성된 C# 클래스 이름 (본인이 설정한 이름 확인)
    private Vector2 moveInput;
    private Vector2 lookInput;
    private float cameraPitch = 0f; // 상하 회전 값 저장용

    public Transform killerCamera; // 살인마 카메라(1인칭)

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        killerInput = new KillerInput(); // 인풋 인스턴스 생성
    }

    void OnEnable()
    {
        // 1. Killer 액션 맵 활성화
        killerInput.Killer.Enable();

        // 2. 공격(Attack)은 '버튼'이므로 이벤트 방식으로 연결
        killerInput.Killer.Attack.performed += OnAttack;
    }

    void OnDisable()
    {
        killerInput.Killer.Disable();
        killerInput.Killer.Attack.performed -= OnAttack;
    }

    void Update()
    {
        // 매 프레임 입력값 읽어오기
        moveInput = killerInput.Killer.Move.ReadValue<Vector2>();
        lookInput = killerInput.Killer.Look.ReadValue<Vector2>();

        HandleMovement();
        HandleLook();
    }

    private void HandleMovement()
    {
        // 카메라가 바라보는 방향 기준으로 이동 방향 계산
        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
        controller.Move(move * moveSpeed * Time.deltaTime);
    }

    private void HandleLook()
    {
        // 좌우 회전 (살인마 몸체 회전)
        transform.Rotate(Vector3.up * lookInput.x * lookSensitivity);

        // 상하 회전 (카메라만 위아래로 까딱이기)
        cameraPitch -= lookInput.y * lookSensitivity;
        cameraPitch = Mathf.Clamp(cameraPitch, -80f, 80f); // 고개 꺾임 방지
        killerCamera.localRotation = Quaternion.Euler(cameraPitch, 0, 0);
    }

    private void OnAttack(InputAction.CallbackContext context)
    {
        Debug.Log("살인마가 공격을 시도합니다!");
        // 여기에 공격 애니메이션 재생 로직 등을 넣으세요.
    }
}