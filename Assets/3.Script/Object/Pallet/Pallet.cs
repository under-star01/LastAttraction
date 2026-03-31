using System.Collections;
using UnityEngine;

public class Pallet : MonoBehaviour, IInteractable
{
    // 판자는 버튼 1번 눌러서 즉시 실행되는 타입
    public InteractType InteractType => InteractType.Press;

    [Header("참조")]
    [SerializeField] private Animator animator;
    [SerializeField] private Collider standingCollider; // 세워진 상태 콜라이더
    [SerializeField] private Collider droppedCollider;  // 드랍한 콜라이더
    [SerializeField] private Collider interactTrigger;  // 상호작용 트리거
    [SerializeField] private Transform leftDropStandPoint;  // 왼쪽 위치
    [SerializeField] private Transform rightDropStandPoint; // 오른쪽 위치

    [Header("시간")]
    [SerializeField] private float moveToPointTime = 0.15f; // 드롭 위치로 부드럽게 이동하는 시간
    [SerializeField] private float dropActionTime = 1f; // 판자 내리는 연출 시간

    [Header("밀어내기")]
    [SerializeField] private float pushDistance = 1.2f; // 겹친 대상을 얼마나 밀어낼지

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
        Transform targetPoint = GetSideDropPoint();
        if (targetPoint == null)
            yield break;

        isDropping = true;

        LockMovement(true);
        FaceToDrop();

        yield return MoveToDropPointRoutine(targetPoint);

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

        // 판자가 내려간 뒤 근처에 겹친 생존자/살인마를 밖으로 밀어냄
        PushOut();

        Debug.Log($"{name} 판자 드롭");
    }

    // 플레이어를 판자 내리기용 자리로 부드럽게 이동
    private IEnumerator MoveToDropPointRoutine(Transform targetPoint)
    {
        if (currentInteractor == null || targetPoint == null)
            yield break;

        CharacterController controller = currentInteractor.GetComponent<CharacterController>();
        if (controller == null)
            controller = currentInteractor.GetComponentInParent<CharacterController>();

        Transform survivorTransform = currentInteractor.transform;

        Vector3 startPos = survivorTransform.position;
        Vector3 endPos = targetPoint.position;

        float elapsed = 0f;

        if (controller != null)
            controller.enabled = false;

        while (elapsed < moveToPointTime)
        {
            elapsed += Time.deltaTime;
            float t = moveToPointTime > 0f ? elapsed / moveToPointTime : 1f;
            t = Mathf.Clamp01(t);

            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            survivorTransform.position = Vector3.Lerp(startPos, endPos, smoothT);

            yield return null;
        }

        survivorTransform.position = endPos;

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

    // 판자 근처에 있는 생존자/살인마를 간단하게 밀어내는 함수
    private void PushOut()
    {
        if (droppedCollider == null)
            return;

        // 판자 콜라이더 범위 안에 있는 모든 콜라이더를 찾음
        Collider[] hits = Physics.OverlapBox(
            droppedCollider.bounds.center,
            droppedCollider.bounds.extents,
            droppedCollider.transform.rotation
        );

        foreach (Collider hit in hits)
        {
            // 자기 자신 판자 콜라이더는 무시
            if (hit == droppedCollider)
                continue;

            // 판자 자식 오브젝트들도 무시
            if (hit.transform.IsChildOf(transform))
                continue;

            // 생존자나 살인마만 처리
            if (!hit.CompareTag("Survivor") && !hit.CompareTag("Killer"))
                continue;

            PushSingleActor(hit.transform);
        }
    }

    // 대상 1명을 판자 밖으로 보내는 함수
    private void PushSingleActor(Transform target)
    {
        // 대상이 판자의 앞쪽에 있는지 뒤쪽에 있는지 계산
        Vector3 toTarget = target.position - transform.position;
        float dot = Vector3.Dot(transform.forward, toTarget);

        // 앞쪽에 있으면 forward 방향으로, 뒤쪽에 있으면 -forward 방향으로 밀어냄
        Vector3 pushDir = dot >= 0f ? transform.forward : -transform.forward;
        pushDir.y = 0f;
        pushDir.Normalize();

        CharacterController controller = target.GetComponent<CharacterController>();
        if (controller == null)
            controller = target.GetComponentInParent<CharacterController>();

        // CharacterController가 있으면 잠깐 껐다가 위치 이동 후 다시 켬
        if (controller != null)
            controller.enabled = false;

        target.position += pushDir * pushDistance;

        if (controller != null)
            controller.enabled = true;
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