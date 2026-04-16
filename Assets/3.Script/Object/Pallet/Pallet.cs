using System.Collections;
using Mirror;
using UnityEngine;

public class Pallet : NetworkBehaviour, IInteractable
{
    public InteractType InteractType => InteractType.Press;

    [Header("참조")]
    [SerializeField] private Animator animator;
    [SerializeField] private Collider standingCollider;
    [SerializeField] private Collider droppedCollider;
    [SerializeField] private Transform leftPoint;
    [SerializeField] private Transform rightPoint;

    [Header("이동/연출 설정")]
    [SerializeField] private Vector3 vaultOffset = new Vector3(0f, 0.2f, 0f);
    [SerializeField] private float moveToPointSpeed = 5f;
    [SerializeField] private float dropActionTime = 0.5f;
    [SerializeField] private float survivorVaultSpeed = 4f;
    [SerializeField] private float breakActionTime = 2f;

    [Header("판정")]
    [SerializeField] private float useDistance = 2f;
    [SerializeField] private float occupationRadius = 1f;
    [SerializeField] private float stunTime = 1.2f;

    [SyncVar(hook = nameof(OnDroppedChanged))]
    private bool isDropped;

    [SyncVar] private bool isBusy;
    [SyncVar] private uint currentActorNetId;
    [SyncVar] private bool isDropping;
    [SyncVar] private bool isVaulting;
    [SyncVar] private bool isBreaking;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        ApplyDroppedState(isDropped);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyDroppedState(isDropped);
    }

    public void BeginInteract(GameObject actor)
    {
        if (actor == null)
            return;

        NetworkIdentity actorIdentity = actor.GetComponent<NetworkIdentity>();
        if (actorIdentity == null)
            actorIdentity = actor.GetComponentInParent<NetworkIdentity>();

        if (actorIdentity == null)
            return;

        if (isServer)
            TryBegin(actorIdentity);
        else
            CmdBegin(actorIdentity.netId);
    }

    public void EndInteract()
    {
        // Press 타입이라 종료 없음
    }

    [Command(requiresAuthority = false)]
    private void CmdBegin(uint actorNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(actorNetId, out NetworkIdentity actorIdentity))
            return;

        TryBegin(actorIdentity);
    }

    // 서버에서 실제 상호작용 가능 여부를 검사하고 시작
    [Server]
    private void TryBegin(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
            return;

        if (isBusy || isDropping || isVaulting || isBreaking)
            return;

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
            return;

        bool isSurvivor = actor.CompareTag("Survivor");
        bool isKiller = actor.CompareTag("Killer");

        if (!isSurvivor && !isKiller)
            return;

        if (!CanUse(actor.transform))
            return;

        Transform sidePoint = GetSide(actor.transform);
        if (sidePoint == null)
            return;

        string opponentTag = isSurvivor ? "Killer" : "Survivor";
        if (IsOpponentAtPoint(sidePoint, opponentTag))
            return;

        isBusy = true;
        currentActorNetId = actorIdentity.netId;

        // 세워진 판자는 생존자만 내릴 수 있다.
        if (!isDropped)
        {
            if (!isSurvivor)
            {
                StopUse();
                return;
            }

            StartCoroutine(Drop(actorIdentity));
            return;
        }

        // 이미 내려진 판자는 생존자는 넘기, 킬러는 부수기
        if (isDropped)
        {
            if (isSurvivor)
            {
                StartCoroutine(Vault(actorIdentity));
                return;
            }

            if (isKiller)
            {
                StartCoroutine(Break(actorIdentity));
                return;
            }
        }

        StopUse();
    }

    // 생존자 판자 내리기
    [Server]
    private IEnumerator Drop(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
        {
            StopUse();
            yield break;
        }

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
        {
            StopUse();
            yield break;
        }

        isDropping = true;

        SurvivorMove move = actor.GetComponent<SurvivorMove>();
        SurvivorActionState act = actor.GetComponent<SurvivorActionState>();
        CharacterController controller = actor.GetComponent<CharacterController>();

        Transform sidePoint = GetSide(actor.transform);
        if (sidePoint == null)
        {
            StopUse();
            yield break;
        }

        // 내리기 시작 전에 방향 정렬, 이동 잠금, 스킬 해제
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

        // 자기 쪽 포인트로 이동
        yield return MoveTo(actor.transform, sidePoint.position, moveToPointSpeed);

        if (move != null)
            move.PlayAnimation("Drop");

        RpcPlayPalletTrigger("Drop");

        // 내려오는 판자 안에 있는 생존자 정리
        PushOutServer();

        // 내려오는 판자에 맞은 킬러가 있으면 스턴
        CheckKillerStunServer();

        yield return new WaitForSeconds(dropActionTime);

        isDropped = true;
        ApplyDroppedState(true);

        if (controller != null)
            controller.enabled = true;

        if (move != null)
            move.SetMoveLock(false);

        isDropping = false;
        StopUse();
    }

    // 생존자 판자 넘기
    [Server]
    private IEnumerator Vault(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
        {
            StopUse();
            yield break;
        }

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
        {
            StopUse();
            yield break;
        }

        isVaulting = true;

        SurvivorMove move = actor.GetComponent<SurvivorMove>();
        SurvivorActionState act = actor.GetComponent<SurvivorActionState>();
        CharacterController controller = actor.GetComponent<CharacterController>();

        Transform sidePoint = GetSide(actor.transform);
        Transform oppositePoint = GetOpposite(sidePoint);

        if (sidePoint == null || oppositePoint == null)
        {
            StopUse();
            yield break;
        }

        // 넘기 시작 전에 방향 정렬, 이동 잠금, 스킬 해제
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
        {
            act.SetCam(false);
            act.SetAct(SurvivorAction.Vault);
        }

        if (controller != null)
            controller.enabled = false;

        yield return null;

        Vector3 startPos = sidePoint.position + vaultOffset;
        Vector3 endPos = oppositePoint.position + vaultOffset;

        // 먼저 자기 쪽 시작점으로 이동
        yield return MoveTo(actor.transform, startPos, moveToPointSpeed);

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

        isVaulting = false;
        StopUse();
    }

    // 킬러 판자 부수기
    [Server]
    private IEnumerator Break(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
        {
            StopUse();
            yield break;
        }

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
        {
            StopUse();
            yield break;
        }

        isBreaking = true;

        KillerState killerState = actor.GetComponent<KillerState>();
        CharacterController controller = actor.GetComponent<CharacterController>();
        Animator killerAnimator = actor.GetComponentInChildren<Animator>();

        Transform sidePoint = GetSide(actor.transform);
        if (sidePoint == null)
        {
            StopUse();
            yield break;
        }

        if (killerState != null)
            killerState.ChangeState(KillerCondition.Breaking);

        if (controller != null)
            controller.enabled = false;

        yield return null;

        yield return MoveTo(actor.transform, sidePoint.position, moveToPointSpeed);

        Vector3 lookDir = GetLook(sidePoint);
        if (lookDir.sqrMagnitude > 0.001f)
            actor.transform.rotation = Quaternion.LookRotation(lookDir.normalized);

        if (killerAnimator != null)
            killerAnimator.SetTrigger("Break");

        RpcPlayPalletTrigger("Break");

        yield return new WaitForSeconds(breakActionTime);

        if (controller != null)
            controller.enabled = true;

        if (killerState != null)
            killerState.ChangeState(KillerCondition.Idle);

        isBreaking = false;

        // 부수기 완료 후 판자 제거
        NetworkServer.Destroy(gameObject);
    }

    // 내려진 판자 안에 겹친 생존자를 좌우 포인트 쪽으로 빼낸다.
    [Server]
    private void PushOutServer()
    {
        if (droppedCollider == null)
            return;

        Collider[] hits = Physics.OverlapBox(
            droppedCollider.bounds.center,
            droppedCollider.bounds.extents,
            droppedCollider.transform.rotation
        );

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];

            if (!hit.CompareTag("Survivor"))
                continue;

            NetworkIdentity identity = hit.GetComponent<NetworkIdentity>();
            if (identity == null)
                identity = hit.GetComponentInParent<NetworkIdentity>();

            // 판자를 내린 본인은 제외
            if (identity != null && identity.netId == currentActorNetId)
                continue;

            Transform target = hit.transform;

            SurvivorMove move = hit.GetComponent<SurvivorMove>();
            if (move == null)
                move = hit.GetComponentInParent<SurvivorMove>();

            if (move != null)
                target = move.transform;

            CharacterController controller = target.GetComponent<CharacterController>();
            if (controller == null)
                controller = target.GetComponentInParent<CharacterController>();

            Vector3 localPos = transform.InverseTransformPoint(target.position);

            Vector3 teleportPos;

            if (localPos.x < 0f)
                teleportPos = leftPoint.position;
            else
                teleportPos = rightPoint.position;

            teleportPos.y = target.position.y;

            if (controller != null)
                controller.enabled = false;

            target.position = teleportPos;

            if (controller != null)
                controller.enabled = true;
        }
    }

    // 내려오는 판자에 킬러가 맞았는지 검사
    [Server]
    private void CheckKillerStunServer()
    {
        if (droppedCollider == null)
            return;

        Collider[] hits = Physics.OverlapBox(
            droppedCollider.bounds.center,
            droppedCollider.bounds.extents,
            droppedCollider.transform.rotation
        );

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];

            if (!hit.CompareTag("Killer"))
                continue;

            KillerInteractor killerInteractor = hit.GetComponent<KillerInteractor>();
            if (killerInteractor == null)
                killerInteractor = hit.GetComponentInParent<KillerInteractor>();

            NetworkIdentity killerIdentity = hit.GetComponent<NetworkIdentity>();
            if (killerIdentity == null)
                killerIdentity = hit.GetComponentInParent<NetworkIdentity>();

            if (killerInteractor == null || killerIdentity == null)
                continue;

            StartCoroutine(KillerHitAlign(killerIdentity, killerInteractor));
        }
    }

    // 맞은 킬러를 판자 쪽으로 정렬한 뒤 스턴 적용
    [Server]
    private IEnumerator KillerHitAlign(NetworkIdentity killerIdentity, KillerInteractor killerInteractor)
    {
        if (killerIdentity == null || killerInteractor == null)
            yield break;

        GameObject killer = killerIdentity.gameObject;
        if (killer == null)
            yield break;

        CharacterController controller = killer.GetComponent<CharacterController>();

        if (controller != null)
            controller.enabled = false;

        yield return null;

        Transform sidePoint = GetSide(killer.transform);
        if (sidePoint != null)
        {
            yield return MoveTo(killer.transform, sidePoint.position, moveToPointSpeed);

            Vector3 lookDir = GetLook(sidePoint);
            if (lookDir.sqrMagnitude > 0.001f)
                killer.transform.rotation = Quaternion.LookRotation(lookDir.normalized);
        }

        killerInteractor.ApplyHitStun(stunTime);

        yield return new WaitForSeconds(stunTime);

        if (controller != null)
            controller.enabled = true;
    }

    [Server]
    private void StopUse()
    {
        isBusy = false;
        currentActorNetId = 0;
    }

    private void OnDroppedChanged(bool oldValue, bool newValue)
    {
        ApplyDroppedState(newValue);
    }

    // 세워짐 / 내려짐 상태에 맞게 콜라이더 전환
    private void ApplyDroppedState(bool dropped)
    {
        if (standingCollider != null)
            standingCollider.enabled = !dropped;

        if (droppedCollider != null)
            droppedCollider.enabled = dropped;
    }

    [ClientRpc]
    private void RpcPlayPalletTrigger(string triggerName)
    {
        if (animator != null)
            animator.SetTrigger(triggerName);
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
            actor.position = Vector3.MoveTowards(
                actor.position,
                targetPos,
                speed * Time.deltaTime
            );

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

        return sqrDist <= useDistance * useDistance;
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

    private void OnDrawGizmosSelected()
    {
        if (leftPoint != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(leftPoint.position, occupationRadius);
        }

        if (rightPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(rightPoint.position, occupationRadius);
        }
    }
}