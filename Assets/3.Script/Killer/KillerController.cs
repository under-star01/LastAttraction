using UnityEngine;
using UnityEngine.InputSystem;

public class KillerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float lookSensitivity = 0.2f;
    public float lungeSpeed = 1.6f;   // 런지 시 가속 배율
    public float penaltySpeed = 0.4f;  // 후딜레이 시 감속 배율

    [Header("Lunge & Penalty Settings")]
    public float maxLungeDuration = 1.2f;        // 최대 런지 시간
    public float hitFailPenalty = 2.0f;      // 허공 휘두름 시 (누른 시간 * 배율) 페널티
    public float hitSuccessPenalty = 2.5f;       // 생존자 타격 시 고정 후딜레이
    public float wallHitPenalty = 3.0f;          // 벽 충돌 시 고정 후딜레이

    [Header("Hit Detection")]
    public Transform attackPoint;       // 공격 판정 중심점
    public float attackRadius = 1.0f;   // 공격 범위 반지름
    public LayerMask survivorLayer;     // 생존자 레이어
    public LayerMask obstacleLayer;     // 벽/구조물 레이어

    [Header("Camera Stabilization")]
    public Transform cameraTarget; // 살인마 머리 쪽 근처의 빈 오브젝트 또는 특정 뼈
    public float followSpeed = 15f; // 카메라가 머리를 따라가는 부드러움 정도

    private CharacterController controller;
    private float cameraPitch = 0f;
    private Vector2 moveInput;
    private Vector2 lookInput;

    // 상태 관리 변수
    private bool isLunging = false;
    private bool isRecovering = false;
    private bool hasHitTarget = false;
    private float currentLungeTime = 0f;
    private float currentPenaltyTime = 0f;

    private Animator animator;

    public Transform killerCamera;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        if (TestMng.inputSys == null) return;

        // 1. 입력 값 업데이트
        moveInput = TestMng.inputSys.Killer.Move.ReadValue<Vector2>();
        lookInput = TestMng.inputSys.Killer.Look.ReadValue<Vector2>();

        // 2. 공격 로직 처리
        HandleLungeInput();
        HandleRecoveryTimer();

        // 3. 이동 및 회전 처리
        HandleMovement();
        HandleLook();

        UpdateAnimationSpeed();
    }

    private void UpdateAnimationSpeed()
    {
        if (animator == null) return;

        float moveInputMagnitude = moveInput.sqrMagnitude;

        animator.SetFloat("Speed", moveInputMagnitude);
        animator.SetBool("isLunging", isLunging);
    }

    private void HandleLungeInput()
    {
        if (isRecovering) return;

        // 공격 지속 (버튼 유지)
        if (TestMng.inputSys.Killer.Attack.IsPressed())
        {
            if (!isLunging) StartLunge();

            currentLungeTime += Time.deltaTime;
            currentLungeTime = Mathf.Clamp(currentLungeTime, 0.1f, maxLungeDuration);

            // 돌진 중 실시간 판정 체크
            CheckHitDetection();

            if (currentLungeTime >= maxLungeDuration || hasHitTarget) EndLunge();
        }
        // 버튼 뗌 (공격 종료)
        else if (isLunging)
        {
            EndLunge();
        }
    }

    private void HandleRecoveryTimer()
    {
        if (isRecovering)
        {
            currentPenaltyTime -= Time.deltaTime;
            if (currentPenaltyTime <= 0)
            {
                isRecovering = false;
            }
        }
    }

    private void StartLunge()
    {
        isLunging = true;
        hasHitTarget = false;
        currentLungeTime = 0f;
        Debug.Log("런지 시작!");

        //if (animator != null) animator.SetTrigger("Attack");
    }

    private void CheckHitDetection()
    {
        if (hasHitTarget) return;

        // 1. 벽/장애물 체크
        if (Physics.CheckSphere(attackPoint.position, attackRadius * 0.5f, obstacleLayer))
        {
            OnWallHit();
            return;
        }

        // 2. 생존자 체크
        Collider[] hitSurvivors = Physics.OverlapSphere(attackPoint.position, attackRadius, survivorLayer);
        if (hitSurvivors.Length > 0)
        {
            OnSurvivorHit(hitSurvivors[0].gameObject);
        }
    }

    private void OnSurvivorHit(GameObject survivor)
    {
        hasHitTarget = true;
        currentPenaltyTime = hitSuccessPenalty;
        Debug.Log($"{survivor.name} 타격 성공! 페널티: {hitSuccessPenalty}s");
    }

    private void OnWallHit()
    {
        hasHitTarget = true;
        currentPenaltyTime = wallHitPenalty;
        Debug.Log($"벽 충돌! 페널티: {wallHitPenalty}s");
    }

    private void EndLunge()
    {
        isLunging = false;
        isRecovering = true;

        // 아무것도 맞추지 못했을 때만 누른 시간에 비례하여 페널티 부여
        if (animator != null) animator.SetTrigger("Attack");

        if (!hasHitTarget)
        {
            currentPenaltyTime = currentLungeTime * hitFailPenalty;
            Debug.Log($"헛손질! 후딜레이: {currentPenaltyTime}초");
        }
    }

    private void HandleMovement()
    {
        float finalSpeed = moveSpeed;

        // 상태에 따른 속도 변화 적용
        if (isLunging) finalSpeed *= lungeSpeed;
        else if (isRecovering) finalSpeed *= penaltySpeed;

        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
        controller.Move(move * finalSpeed * Time.deltaTime);
    }

    //private void HandleLook()
    //{
    //    // 런지 중에는 회전 감도를 낮추고 싶다면 여기서 조절 가능합니다.
    //    float currentSensitivity = lookSensitivity;
    //    if (isLunging) currentSensitivity *= 0.5f;

    //    transform.Rotate(Vector3.up * lookInput.x * currentSensitivity);

    //    cameraPitch -= lookInput.y * currentSensitivity;
    //    cameraPitch = Mathf.Clamp(cameraPitch, -80f, 80f);
    //    killerCamera.localRotation = Quaternion.Euler(cameraPitch, 0, 0);
    //}

    private void HandleLook()
    {
        // 1. 몸체 회전 (좌우)
        transform.Rotate(Vector3.up * lookInput.x * lookSensitivity);

        // 2. 카메라 상하 회전 값 계산
        cameraPitch -= lookInput.y * lookSensitivity;
        cameraPitch = Mathf.Clamp(cameraPitch, -80f, 80f);

        // 3. 카메라의 회전은 애니메이션의 영향을 받지 않고 마우스 입력으로만 결정!
        killerCamera.localRotation = Quaternion.Euler(cameraPitch, 0, 0);

        // 4. [중요] 카메라의 위치를 머리 타겟 위치로 부드럽게 이동 (회전은 무시)
        if (cameraTarget != null)
        {
            killerCamera.position = Vector3.Lerp(killerCamera.position, cameraTarget.position, Time.deltaTime * followSpeed);
        }
    }

    // 판정 범위 시각화
    private void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRadius);
    }
}