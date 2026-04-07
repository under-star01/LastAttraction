using System.Collections;
using UnityEngine;

public class Pallet : MonoBehaviour, IInteractable
{
    public InteractType InteractType => InteractType.Press;

    [Header("참조")]
    [SerializeField] private Animator animator;
    [SerializeField] private Collider standingCollider;
    [SerializeField] private Collider droppedCollider;
    [SerializeField] private Transform leftPoint;
    [SerializeField] private Transform rightPoint;
    [SerializeField] private Vector3 upPoint = new Vector3(0, 0.2f, 0);

    [Header("이동/연출 설정")]
    [SerializeField] private float moveToPointSpeed = 5f;
    [SerializeField] private float dropActionTime = 0.5f;
    [SerializeField] private float vaultSpeed = 4f;
    [SerializeField] private float breakActionTime = 2.0f;

    [Header("판정 및 예외 처리")]
    [SerializeField] private float occupationRadius = 1.0f; // 포인트 점유 반경
    [SerializeField] private float stunTime = 1.2f;

    private bool isDropped;
    private bool isDropping;
    private bool isVaulting;
    private bool isBreaking;
    private bool isLeftSide;

    private SurvivorInteractor currentInteractor;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (standingCollider != null) standingCollider.enabled = true;
        if (droppedCollider != null) droppedCollider.enabled = false;
    }

    // --- [상호작용 시작] ---
    public void BeginInteract(GameObject actor)
    {
        if (isDropping || isVaulting || isBreaking) return;

        Transform myPoint = GetSidePointForActor(actor.transform);

        string opponentTag = actor.CompareTag("Survivor") ? "Killer" : "Survivor";
        if (IsOpponentAtPoint(myPoint, opponentTag))
        {
            Debug.Log($"{opponentTag}가 근처에 있어 상호작용이 불가능합니다.");
            return;
        }

        if (!isDropped)
        {
            if (actor.CompareTag("Survivor")) StartCoroutine(DropRoutine());
        }
        else
        {
            if (actor.CompareTag("Killer")) StartCoroutine(BreakRoutine(actor));
            else if (actor.CompareTag("Survivor")) StartCoroutine(VaultRoutine());
        }
    }

    public void EndInteract() { }

    // --- [루틴: 생존자 판자 내리기] ---
    private IEnumerator DropRoutine()
    {
        isDropping = true;
        Transform sidePoint = GetSidePointForActor(currentInteractor.transform);

        LockMovement(true);
        FaceActorToPallet(currentInteractor.transform, sidePoint == leftPoint);

        CharacterController controller = currentInteractor.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;

        StopAnim();
        yield return MoveActorToPoint(currentInteractor.transform, sidePoint.position, moveToPointSpeed);

        PlayAnim("Drop");
        if (animator != null) animator.SetTrigger("Drop");

        yield return new WaitForSeconds(dropActionTime);

        Drop(); // 상태 변경 및 살인마 정렬 체크 실행

        if (controller != null) controller.enabled = true;
        LockMovement(false);
        isDropping = false;
    }

    // --- [루틴: 살인마 판자 파괴] ---
    private IEnumerator BreakRoutine(GameObject killer)
    {
        isBreaking = true;
        KillerState kState = killer.GetComponent<KillerState>();
        CharacterController kController = killer.GetComponent<CharacterController>();
        Animator kAnimator = killer.GetComponentInChildren<Animator>();

        kState.ChangeState(KillerCondition.Breaking);

        if (kController != null) kController.enabled = false;
        yield return null; // 물리 연산 동기화를 위해 한 프레임 대기

        Transform sidePoint = GetSidePointForActor(killer.transform);
        yield return MoveActorToPoint(killer.transform, sidePoint.position, moveToPointSpeed);
        FaceActorToPallet(killer.transform, sidePoint == leftPoint);

        if (kAnimator != null) kAnimator.SetTrigger("Break");
        if (animator != null) animator.SetTrigger("Break");

        yield return new WaitForSeconds(breakActionTime);

        if (kController != null) kController.enabled = true;
        kState.ChangeState(KillerCondition.Idle);
        Destroy(gameObject);
    }

    // --- [루틴: 살인마 판자에 맞았을 때 정렬] ---
    private IEnumerator KillerHitAlignRoutine(GameObject killer, KillerInteractor kInteract)
    {
        CharacterController kController = killer.GetComponent<CharacterController>();
        if (kController == null) kController = killer.GetComponentInParent<CharacterController>();

        // 물리 제어를 끄고 정해진 포인트로 정렬
        if (kController != null) kController.enabled = false;
        yield return null;

        Transform sidePoint = GetSidePointForActor(killer.transform);
        yield return MoveActorToPoint(killer.transform, sidePoint.position, moveToPointSpeed);
        FaceActorToPallet(killer.transform, sidePoint == leftPoint);

        // 정렬 완료 후 스턴 애니메이션 및 상태 적용
        kInteract.ApplyHitStun(stunTime);

        yield return new WaitForSeconds(stunTime);
        if (kController != null) kController.enabled = true;
    }

    // --- [루틴: 생존자 판자 넘기] ---
    private IEnumerator VaultRoutine()
    {
        isVaulting = true;
        Transform sidePoint = GetSidePointForActor(currentInteractor.transform);
        Transform oppositePoint = (sidePoint == leftPoint) ? rightPoint : leftPoint;

        LockMovement(true);
        FaceActorToPallet(currentInteractor.transform, sidePoint == leftPoint);

        CharacterController controller = currentInteractor.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;

        Vector3 start = sidePoint.position + upPoint;
        Vector3 arrive = oppositePoint.position + upPoint;

        StopAnim();
        yield return MoveActorToPoint(currentInteractor.transform, start, moveToPointSpeed);

        SurvivorMove move = GetCurrentMove();
        if (move != null) move.SetVaulting(true);

        if (sidePoint == leftPoint) PlayAnim("LeftVault");
        else PlayAnim("RightVault");

        yield return MoveActorToPoint(currentInteractor.transform, arrive, vaultSpeed);

        if (controller != null) controller.enabled = true;
        LockMovement(false);
        if (move != null) move.SetVaulting(false);
        isVaulting = false;
    }

    // --- [핵심 로직: 실제 상태 변경 및 스턴] ---
    private void Drop()
    {
        isDropped = true;
        standingCollider.enabled = false;
        droppedCollider.enabled = true;

        // 밀어내기(PushOut)는 제거하고, 스턴 시 포인트로 정렬시킵니다.
        CheckKillerStun();
    }

    private void CheckKillerStun()
    {
        Collider[] hits = Physics.OverlapBox(droppedCollider.bounds.center, droppedCollider.bounds.extents, transform.rotation);
        foreach (var hit in hits)
        {
            if (hit.CompareTag("Killer"))
            {
                var kInteract = hit.GetComponentInParent<KillerInteractor>();
                if (kInteract != null)
                {
                    // 단순 힛이 아니라, 포인트로 정렬하는 루틴을 실행합니다.
                    StartCoroutine(KillerHitAlignRoutine(hit.gameObject, kInteract));
                }
            }
        }
    }

    // --- [제어 및 도움 함수들] ---
    private void LockMovement(bool value)
    {
        SurvivorMove move = GetCurrentMove();
        if (move != null) move.SetMoveLock(value);
    }

    private void PlayAnim(string triggerName)
    {
        SurvivorMove move = GetCurrentMove();
        if (move != null) move.PlayAnimation(triggerName);
    }

    private void StopAnim()
    {
        SurvivorMove move = GetCurrentMove();
        if (move != null) move.StopAnimation();
    }

    private SurvivorMove GetCurrentMove()
    {
        if (currentInteractor == null) return null;
        SurvivorMove move = currentInteractor.GetComponent<SurvivorMove>();
        if (move == null) move = currentInteractor.GetComponentInParent<SurvivorMove>();
        return move;
    }

    private Transform GetSidePointForActor(Transform actor)
    {
        Vector3 localPos = transform.InverseTransformPoint(actor.position);
        isLeftSide = localPos.x < 0f;
        return isLeftSide ? leftPoint : rightPoint;
    }

    private void FaceActorToPallet(Transform actor, bool isLeft)
    {
        Vector3 lookDir = isLeft ? transform.right : -transform.right;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.001f) actor.rotation = Quaternion.LookRotation(lookDir.normalized);
    }

    private IEnumerator MoveActorToPoint(Transform actor, Vector3 target, float speed)
    {
        while (Vector3.Distance(actor.position, target) > 0.001f)
        {
            actor.position = Vector3.MoveTowards(actor.position, target, speed * Time.deltaTime);
            yield return null;
        }
        actor.position = target;
    }

    private bool IsOpponentAtPoint(Transform targetPoint, string opponentTag)
    {
        Collider[] hits = Physics.OverlapSphere(targetPoint.position, occupationRadius);
        foreach (var hit in hits) if (hit.CompareTag(opponentTag)) return true;
        return false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Survivor"))
        {
            currentInteractor = other.GetComponent<SurvivorInteractor>();
            currentInteractor.SetInteractable(this);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Survivor") && currentInteractor?.gameObject == other.gameObject)
        {
            currentInteractor.ClearInteractable(this);
            currentInteractor = null;
        }
    }
}