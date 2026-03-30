using System.Collections;
using UnityEngine;

public class Pallet : MonoBehaviour, IInteractable
{
    // 판자는 버튼 1번 눌러서 즉시 실행되는 타입
    public InteractType InteractType => InteractType.Press;

    [Header("참조")]
    [SerializeField] private Animator animator;
    [SerializeField] private Collider standingCollider; // 세워진 상태 충돌
    [SerializeField] private Collider droppedCollider;  // 넘어간 뒤 충돌
    [SerializeField] private Collider interactTrigger;  // 상호작용 범위
    [SerializeField] private Transform leftDropStandPoint;  // 왼쪽 접근 위치
    [SerializeField] private Transform rightDropStandPoint; // 오른쪽 접근 위치

    [Header("시간")]
    [SerializeField] private float dropActionTime = 1f; // 판자 내리는 연출 시간

    private bool isDropped;            // 이미 내려갔는지
    private bool isDropping;           // 현재 내리는 중인지
    private SurvivorInteractor currentInteractor; // 현재 범위 안 플레이어
    private bool isLeftSide;           // 플레이어가 왼쪽에서 접근했는지

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (standingCollider != null)
            standingCollider.enabled = true;

        if (droppedCollider != null)
            droppedCollider.enabled = false;
    }

    // 상호작용 시작
    public void BeginInteract()
    {
        if (isDropped || isDropping)
            return;

        StartCoroutine(DropRoutine());
    }

    public void EndInteract()
    {
        // Press 타입이라 종료 처리는 필요 없음
    }

    // 판자 내리는 전체 흐름
    private IEnumerator DropRoutine()
    {
        isDropping = true;

        // 플레이어 위치와 방향을 연출용 자리로 맞춤
        ToDropPoint();
        FaceToDrop();
        LockMovement(true);

        if (animator != null)
            animator.SetTrigger("Drop");

        yield return new WaitForSeconds(dropActionTime);

        Drop();

        LockMovement(false);
        isDropping = false;
    }

    // 실제 판자 상태 변경
    private void Drop()
    {
        isDropped = true;

        if (standingCollider != null)
            standingCollider.enabled = false;

        if (droppedCollider != null)
            droppedCollider.enabled = true;

        if (interactTrigger != null)
            interactTrigger.enabled = false;

        Debug.Log($"{name} 판자 드롭");
    }

    // 플레이어를 판자 내리기용 자리로 이동
    private void ToDropPoint()
    {
        if (currentInteractor == null)
            return;

        Transform targetPoint = GetSideDropPoint();
        if (targetPoint == null)
            return;

        CharacterController controller = currentInteractor.GetComponent<CharacterController>();
        if (controller == null)
            controller = currentInteractor.GetComponentInParent<CharacterController>();

        Transform survivorTransform = currentInteractor.transform;

        // CharacterController 켠 상태로 순간이동하면 문제날 수 있어서 잠깐 끔
        if (controller != null)
            controller.enabled = false;

        survivorTransform.position = targetPoint.position;

        if (controller != null)
            controller.enabled = true;
    }

    // 현재 플레이어가 왼쪽/오른쪽 어느 쪽인지 판정
    private Transform GetSideDropPoint()
    {
        if (currentInteractor == null)
            return null;

        Vector3 localPos = transform.InverseTransformPoint(currentInteractor.transform.position);
        isLeftSide = localPos.x < 0f;

        if (isLeftSide)
            return leftDropStandPoint != null ? leftDropStandPoint : rightDropStandPoint;
        else
            return rightDropStandPoint != null ? rightDropStandPoint : leftDropStandPoint;
    }

    // 판자 내리는 방향으로 플레이어를 바라보게 함
    private void FaceToDrop()
    {
        if (currentInteractor == null)
            return;

        Vector3 lookDir = isLeftSide ? transform.right : -transform.right;
        lookDir.y = 0f;

        if (lookDir.sqrMagnitude <= 0.001f)
            return;

        SurvivorMove move = currentInteractor.GetComponent<SurvivorMove>();
        if (move == null)
            move = currentInteractor.GetComponentInParent<SurvivorMove>();

        if (move != null)
            move.FaceDirection(lookDir.normalized);
        else
            currentInteractor.transform.rotation = Quaternion.LookRotation(lookDir.normalized);
    }

    // 판자 내리는 동안 이동 잠금
    private void LockMovement(bool value)
    {
        if (currentInteractor == null)
            return;

        SurvivorMove move = currentInteractor.GetComponent<SurvivorMove>();
        if (move == null)
            move = currentInteractor.GetComponentInParent<SurvivorMove>();

        if (move != null)
            move.SetMoveLock(value);
    }

    // 범위 진입 시 상호작용 가능 등록
    private void OnTriggerEnter(Collider other)
    {
        if (isDropped || isDropping)
            return;

        if (!other.CompareTag("Survivor"))
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        if (interactor != null)
        {
            currentInteractor = interactor;
            interactor.SetInteractable(this);
            Debug.Log($"{name} 범위 진입");
        }
    }

    // 범위 이탈 시 상호작용 해제
    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Survivor"))
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        if (interactor != null)
        {
            interactor.ClearInteractable(this);

            if (currentInteractor == interactor)
                currentInteractor = null;

            Debug.Log($"{name} 범위 이탈");
        }
    }
}