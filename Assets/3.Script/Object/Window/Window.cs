using System.Collections;
using Mirror;
using UnityEngine;

public class Window : NetworkBehaviour, IInteractable
{
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

    [SyncVar] private bool isBusy;
    [SyncVar] private bool isVaulting;
    [SyncVar] private uint currentActorNetId;

    public void BeginInteract(GameObject actor)
    {
        if (actor == null)
            return;

        NetworkIdentity actorIdentity = actor.GetComponent<NetworkIdentity>();
        if (actorIdentity == null)
            actorIdentity = actor.GetComponentInParent<NetworkIdentity>();

        if (actorIdentity == null)
            return;

        if (isBusy || isVaulting)
            return;

        if (isServer)
            TryBegin(actorIdentity);
        else
            CmdBegin(actorIdentity.netId);
    }

    public void EndInteract()
    {
        // Press 타입이라 종료 처리 없음
    }

    [Command(requiresAuthority = false)]
    private void CmdBegin(uint actorNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(actorNetId, out NetworkIdentity actorIdentity))
            return;

        TryBegin(actorIdentity);
    }

    // 서버에서 실제 사용 가능 여부 확인 후 시작
    [Server]
    private void TryBegin(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
            return;

        if (isBusy || isVaulting)
            return;

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
            return;

        bool isSurvivor = actor.CompareTag("Survivor");
        bool isKiller = actor.CompareTag("Killer");

        if (!isSurvivor && !isKiller)
            return;

        Transform sidePoint = GetSide(actor.transform);
        if (sidePoint == null)
            return;

        string opponentTag = isSurvivor ? "Killer" : "Survivor";
        if (IsOpponentAtPoint(sidePoint, opponentTag))
            return;

        if (!CanUse(actor.transform))
            return;

        isBusy = true;
        currentActorNetId = actorIdentity.netId;

        if (isSurvivor)
            StartCoroutine(SurvivorVault(actorIdentity));
        else
            StartCoroutine(KillerVault(actorIdentity));
    }

    // 생존자 창틀 넘기
    [Server]
    private IEnumerator SurvivorVault(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
        {
            StopVault();
            yield break;
        }

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
        {
            StopVault();
            yield break;
        }

        SurvivorMove move = actor.GetComponent<SurvivorMove>();
        SurvivorActionState act = actor.GetComponent<SurvivorActionState>();
        CharacterController controller = actor.GetComponent<CharacterController>();

        Transform sidePoint = GetSide(actor.transform);
        Transform oppositePoint = GetOpposite(sidePoint);

        if (sidePoint == null || oppositePoint == null)
        {
            StopVault();
            yield break;
        }

        // 넘기 시작 전에 움직임 잠금, 방향 정렬, 스킬 해제
        if (move != null)
        {
            move.SetMoveLock(true);

            Vector3 lookDir = GetLook(sidePoint);
            if (lookDir.sqrMagnitude > 0.001f)
                move.FaceDirection(lookDir.normalized);

            move.StopAnimation();
            move.SetCamAnim(false);
        }

        if (act != null)
            act.SetCam(false);

        if (controller != null)
            controller.enabled = false;

        yield return null;

        Vector3 startPos = sidePoint.position + upPoint;
        Vector3 endPos = oppositePoint.position + upPoint;

        // 먼저 자기 쪽 시작점으로 이동
        yield return MoveTo(actor.transform, startPos, moveToPointSpeed);

        // 실제 넘기 상태 시작
        isVaulting = true;

        if (act != null)
        {
            act.SetCam(false);
            act.SetAct(SurvivorAction.Vault);
        }

        if (move != null)
        {
            move.SetVaulting(true);

            if (sidePoint == leftPoint)
                move.PlayAnimation("LeftVault");
            else
                move.PlayAnimation("RightVault");
        }

        // 반대편으로 이동
        yield return MoveTo(actor.transform, endPos, survivorVaultSpeed);

        if (controller != null)
            controller.enabled = true;

        if (move != null)
        {
            move.SetVaulting(false);
            move.SetMoveLock(false);
        }

        if (act != null)
            act.ClearAct(SurvivorAction.Vault);

        StopVault();
    }

    // 킬러 창틀 넘기
    [Server]
    private IEnumerator KillerVault(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
        {
            StopVault();
            yield break;
        }

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
        {
            StopVault();
            yield break;
        }

        KillerState killerState = actor.GetComponent<KillerState>();
        CharacterController controller = actor.GetComponent<CharacterController>();
        Animator animator = actor.GetComponentInChildren<Animator>();

        Transform sidePoint = GetSide(actor.transform);
        Transform oppositePoint = GetOpposite(sidePoint);

        if (sidePoint == null || oppositePoint == null)
        {
            StopVault();
            yield break;
        }

        if (killerState != null)
            killerState.ChangeState(KillerCondition.Vaulting);

        if (controller != null)
            controller.enabled = false;

        yield return null;

        yield return MoveTo(actor.transform, sidePoint.position, moveToPointSpeed);

        isVaulting = true;

        Vector3 lookDir = GetLook(sidePoint);
        if (lookDir.sqrMagnitude > 0.001f)
            actor.transform.rotation = Quaternion.LookRotation(lookDir.normalized);

        if (animator != null)
            animator.SetTrigger("Vault");

        yield return MoveTo(actor.transform, oppositePoint.position, killerVaultSpeed);

        if (controller != null)
            controller.enabled = true;

        if (killerState != null)
            killerState.ChangeState(KillerCondition.Idle);

        StopVault();
    }

    [Server]
    private void StopVault()
    {
        isBusy = false;
        isVaulting = false;
        currentActorNetId = 0;
    }

    private Transform GetSide(Transform actor)
    {
        if (actor == null)
            return null;

        Vector3 localPos = transform.InverseTransformPoint(actor.position);

        if (localPos.x < 0f)
            return leftPoint;
        else
            return rightPoint;
    }

    private Transform GetOpposite(Transform sidePoint)
    {
        if (sidePoint == leftPoint)
            return rightPoint;

        if (sidePoint == rightPoint)
            return leftPoint;

        return null;
    }

    private Vector3 GetLook(Transform sidePoint)
    {
        if (sidePoint == leftPoint)
            return transform.right;

        if (sidePoint == rightPoint)
            return -transform.right;

        return Vector3.zero;
    }

    [Server]
    private IEnumerator MoveTo(Transform actor, Vector3 targetPos, float speed)
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