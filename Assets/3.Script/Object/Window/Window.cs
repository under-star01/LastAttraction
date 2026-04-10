using System.Collections;
using Mirror;
using UnityEngine;

public class Window : NetworkBehaviour, IInteractable
{
    // 창틀은 버튼 1번 눌러서 실행하는 Press 타입
    public InteractType InteractType => InteractType.Press;

    [Header("참조")]
    [SerializeField] private Transform leftPoint;
    [SerializeField] private Transform rightPoint;
    [SerializeField] private Vector3 upPoint = new Vector3(0f, 0.2f, 0f);

    [Header("이동/연출 설정")]
    [SerializeField] private float moveToPointSpeed = 5f;
    [SerializeField] private float survivorVaultSpeed = 4f;
    [SerializeField] private float killerVaultSpeed = 2.5f;
    [SerializeField] private float occupationRadius = 1.0f;

    // 버튼을 눌러 선점한 순간부터 true
    // 포인트로 이동 중이든 실제 넘는 중이든 다른 사람이 못 쓰게 막음
    [SyncVar] private bool isBusy;

    // 실제 넘는 연출 중인지
    [SyncVar] private bool isVaulting;

    // 현재 사용 중인 플레이어
    [SyncVar] private uint currentActorNetId;

    // 상호작용 시작
    public void BeginInteract(GameObject actor)
    {
        if (actor == null)
            return;

        NetworkIdentity actorIdentity = actor.GetComponent<NetworkIdentity>();
        if (actorIdentity == null)
            actorIdentity = actor.GetComponentInParent<NetworkIdentity>();

        if (actorIdentity == null)
            return;

        // 이미 누가 선점했거나 넘는 중이면 불가
        if (isBusy || isVaulting)
            return;

        if (isServer)
            TryBeginVaultServer(actorIdentity);
        else
            CmdBeginVault(actorIdentity.netId);
    }

    public void EndInteract()
    {
        // Press 타입이라 종료 처리 없음
    }

    [Command(requiresAuthority = false)]
    private void CmdBeginVault(uint actorNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(actorNetId, out NetworkIdentity actorIdentity))
            return;

        TryBeginVaultServer(actorIdentity);
    }

    [Server]
    private void TryBeginVaultServer(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
            return;

        // 이미 누가 선점했거나 넘는 중이면 불가
        if (isBusy || isVaulting)
            return;

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
            return;

        bool isSurvivor = actor.CompareTag("Survivor");
        bool isKiller = actor.CompareTag("Killer");

        if (!isSurvivor && !isKiller)
            return;

        Transform sidePoint = GetSidePointForActor(actor.transform);
        if (sidePoint == null)
            return;

        // 같은 쪽 포인트 주변에 상대가 있으면 시작 막기
        string opponentTag = isSurvivor ? "Killer" : "Survivor";
        if (IsOpponentAtPoint(sidePoint, opponentTag))
            return;

        // 범위 밖이면 불가
        if (!CanUse(actor.transform))
            return;

        // 여기서 바로 선점
        isBusy = true;
        currentActorNetId = actorIdentity.netId;

        if (isSurvivor)
            StartCoroutine(SurvivorVaultRoutine(actorIdentity));
        else
            StartCoroutine(KillerVaultRoutine(actorIdentity));
    }

    [Server]
    private IEnumerator SurvivorVaultRoutine(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
        {
            StopVaultServer();
            yield break;
        }

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
        {
            StopVaultServer();
            yield break;
        }

        SurvivorMove move = actor.GetComponent<SurvivorMove>();
        CharacterController controller = actor.GetComponent<CharacterController>();

        Transform sidePoint = GetSidePointForActor(actor.transform);
        Transform oppositePoint = GetOppositePoint(sidePoint);

        if (sidePoint == null || oppositePoint == null)
        {
            StopVaultServer();
            yield break;
        }

        if (move != null)
        {
            move.SetMoveLock(true);

            Vector3 lookDir = GetLookDirection(sidePoint);
            if (lookDir.sqrMagnitude > 0.001f)
                move.FaceDirection(lookDir.normalized);

            move.StopAnimation();
        }

        if (controller != null)
            controller.enabled = false;

        yield return null;

        Vector3 startPos = sidePoint.position + upPoint;
        Vector3 endPos = oppositePoint.position + upPoint;

        // 포인트로 이동하는 구간도 이미 isBusy=true 상태
        yield return MoveActorToPoint(actor.transform, startPos, moveToPointSpeed);

        // 실제 넘기 시작
        isVaulting = true;

        if (move != null)
        {
            move.SetVaulting(true);

            if (sidePoint == leftPoint)
                move.PlayAnimation("LeftVault");
            else
                move.PlayAnimation("RightVault");
        }

        yield return MoveActorToPoint(actor.transform, endPos, survivorVaultSpeed);

        if (controller != null)
            controller.enabled = true;

        if (move != null)
        {
            move.SetVaulting(false);
            move.SetMoveLock(false);
        }

        StopVaultServer();
    }

    [Server]
    private IEnumerator KillerVaultRoutine(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
        {
            StopVaultServer();
            yield break;
        }

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
        {
            StopVaultServer();
            yield break;
        }

        KillerState killerState = actor.GetComponent<KillerState>();
        CharacterController controller = actor.GetComponent<CharacterController>();
        Animator animator = actor.GetComponentInChildren<Animator>();

        Transform sidePoint = GetSidePointForActor(actor.transform);
        Transform oppositePoint = GetOppositePoint(sidePoint);

        if (sidePoint == null || oppositePoint == null)
        {
            StopVaultServer();
            yield break;
        }

        if (killerState != null)
            killerState.ChangeState(KillerCondition.Vaulting);

        if (controller != null)
            controller.enabled = false;

        yield return null;

        // 포인트로 이동하는 구간도 이미 isBusy=true 상태
        yield return MoveActorToPoint(actor.transform, sidePoint.position, moveToPointSpeed);

        isVaulting = true;

        Vector3 lookDir = GetLookDirection(sidePoint);
        if (lookDir.sqrMagnitude > 0.001f)
            actor.transform.rotation = Quaternion.LookRotation(lookDir.normalized);

        if (animator != null)
            animator.SetTrigger("Vault");

        yield return MoveActorToPoint(actor.transform, oppositePoint.position, killerVaultSpeed);

        if (controller != null)
            controller.enabled = true;

        if (killerState != null)
            killerState.ChangeState(KillerCondition.Idle);

        StopVaultServer();
    }

    [Server]
    private void StopVaultServer()
    {
        isBusy = false;
        isVaulting = false;
        currentActorNetId = 0;
    }

    private Transform GetSidePointForActor(Transform actor)
    {
        if (actor == null)
            return null;

        Vector3 localPos = transform.InverseTransformPoint(actor.position);

        if (localPos.x < 0f)
            return leftPoint;
        else
            return rightPoint;
    }

    private Transform GetOppositePoint(Transform sidePoint)
    {
        if (sidePoint == leftPoint)
            return rightPoint;

        if (sidePoint == rightPoint)
            return leftPoint;

        return null;
    }

    private Vector3 GetLookDirection(Transform sidePoint)
    {
        if (sidePoint == leftPoint)
            return transform.right;

        if (sidePoint == rightPoint)
            return -transform.right;

        return Vector3.zero;
    }

    [Server]
    private IEnumerator MoveActorToPoint(Transform actor, Vector3 targetPos, float speed)
    {
        if (actor == null)
            yield break;

        while ((actor.position - targetPos).sqrMagnitude > 0.0001f)
        {
            actor.position = Vector3.MoveTowards(actor.position, targetPos, speed * Time.deltaTime);
            yield return null;
        }

        actor.position = targetPos;
    }

    private bool IsOpponentAtPoint(Transform targetPoint, string opponentTag)
    {
        if (targetPoint == null)
            return false;

        Collider[] hits = Physics.OverlapSphere(targetPoint.position, occupationRadius);

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].CompareTag(opponentTag))
                return true;
        }

        return false;
    }

    private bool CanUse(Transform actorTransform)
    {
        if (actorTransform == null)
            return false;

        Collider col = GetComponent<Collider>();
        if (col == null)
            col = GetComponentInChildren<Collider>();

        if (col == null)
            return false;

        Vector3 closest = col.ClosestPoint(actorTransform.position);
        float sqrDist = (closest - actorTransform.position).sqrMagnitude;

        return sqrDist <= 4f;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Survivor"))
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        if (interactor == null)
            return;

        if (!interactor.isLocalPlayer)
            return;

        interactor.SetInteractable(this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Survivor"))
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        if (interactor == null)
            return;

        if (!interactor.isLocalPlayer)
            return;

        interactor.ClearInteractable(this);
    }
}