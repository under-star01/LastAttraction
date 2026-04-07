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
    [SerializeField] private Vector3 upPoint = new Vector3(0f, 0.5f, 0f);

    [Header("이동/연출 설정")]
    [SerializeField] private float moveToPointSpeed = 5f;
    [SerializeField] private float survivorVaultSpeed = 4f;
    [SerializeField] private float killerVaultSpeed = 2.5f;
    [SerializeField] private float occupationRadius = 1.0f;

    // 지금 누가 창틀을 사용 중인지 서버가 관리
    [SyncVar] private bool isVaulting;
    [SyncVar] private uint currentActorNetId;

    // 상호작용 시작
    // 로컬에서 호출되지만 실제 처리는 서버에서 한다
    public void BeginInteract(GameObject actor)
    {
        if (actor == null)
            return;

        NetworkIdentity actorIdentity = actor.GetComponent<NetworkIdentity>();
        if (actorIdentity == null)
            actorIdentity = actor.GetComponentInParent<NetworkIdentity>();

        if (actorIdentity == null)
            return;

        // 이미 창틀 사용 중이면 시작 불가
        if (isVaulting)
            return;

        // 서버면 바로 처리
        if (isServer)
        {
            TryBeginVaultServer(actorIdentity);
        }
        // 클라이언트면 서버에 요청
        else
        {
            CmdBeginVault(actorIdentity.netId);
        }
    }

    public void EndInteract()
    {
        // Press 타입이라 따로 종료 처리 없음
    }

    // 클라 -> 서버 : 창틀 넘기 요청
    [Command(requiresAuthority = false)]
    private void CmdBeginVault(uint actorNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(actorNetId, out NetworkIdentity actorIdentity))
            return;

        TryBeginVaultServer(actorIdentity);
    }

    // 서버에서 실제 시작 판정
    [Server]
    private void TryBeginVaultServer(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
            return;

        if (isVaulting)
            return;

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
            return;

        // 생존자 / 살인마 둘 중 하나만 허용
        bool isSurvivor = actor.CompareTag("Survivor");
        bool isKiller = actor.CompareTag("Killer");

        if (!isSurvivor && !isKiller)
            return;

        Transform sidePoint = GetSidePointForActor(actor.transform);
        if (sidePoint == null)
            return;

        // 같은 쪽 포인트 근처에 상대가 있으면 못 넘게
        string opponentTag = isSurvivor ? "Killer" : "Survivor";
        if (IsOpponentAtPoint(sidePoint, opponentTag))
            return;

        // 범위 체크
        if (!CanUse(actor.transform))
            return;

        isVaulting = true;
        currentActorNetId = actorIdentity.netId;

        if (isSurvivor)
            StartCoroutine(SurvivorVaultRoutine(actorIdentity));
        else
            StartCoroutine(KillerVaultRoutine(actorIdentity));
    }

    // 생존자 창틀 넘기
    // 서버에서 실제 이동
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

        // 이동 잠금
        if (move != null)
        {
            move.SetMoveLock(true);

            Vector3 lookDir = GetLookDirection(sidePoint);
            if (lookDir.sqrMagnitude > 0.001f)
                move.FaceDirection(lookDir.normalized);
        }

        // 컨트롤러 끄고 위치 이동
        if (controller != null)
            controller.enabled = false;

        yield return null;

        Vector3 startPos = sidePoint.position + upPoint;
        Vector3 endPos = oppositePoint.position + upPoint;

        if (move != null)
            move.StopAnimation();

        // 내 쪽 시작 포인트까지 이동
        yield return MoveActorToPoint(actor.transform, startPos, moveToPointSpeed);

        // 볼트 애니메이션 시작
        if (move != null)
        {
            move.SetVaulting(true);

            if (sidePoint == leftPoint)
                move.PlayAnimation("LeftVault");
            else
                move.PlayAnimation("RightVault");
        }

        // 반대편으로 이동
        yield return MoveActorToPoint(actor.transform, endPos, survivorVaultSpeed);

        // 다시 원래 상태 복구
        if (controller != null)
            controller.enabled = true;

        if (move != null)
        {
            move.SetVaulting(false);
            move.SetMoveLock(false);
        }

        StopVaultServer();
    }

    // 살인마 창틀 넘기
    // 서버에서 실제 이동
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

        // 내 쪽 포인트로 먼저 이동
        yield return MoveActorToPoint(actor.transform, sidePoint.position, moveToPointSpeed);

        Vector3 lookDir = GetLookDirection(sidePoint);
        if (lookDir.sqrMagnitude > 0.001f)
            actor.transform.rotation = Quaternion.LookRotation(lookDir.normalized);

        if (animator != null)
            animator.SetTrigger("Vault");

        // 반대편으로 이동
        yield return MoveActorToPoint(actor.transform, oppositePoint.position, killerVaultSpeed);

        if (controller != null)
            controller.enabled = true;

        if (killerState != null)
            killerState.ChangeState(KillerCondition.Idle);

        StopVaultServer();
    }

    // 공통 종료
    [Server]
    private void StopVaultServer()
    {
        isVaulting = false;
        currentActorNetId = 0;
    }

    // 어느 쪽 포인트인지 계산
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

    // 반대편 포인트 반환
    private Transform GetOppositePoint(Transform sidePoint)
    {
        if (sidePoint == leftPoint)
            return rightPoint;

        if (sidePoint == rightPoint)
            return leftPoint;

        return null;
    }

    // 창틀을 바라보는 방향 계산
    private Vector3 GetLookDirection(Transform sidePoint)
    {
        if (sidePoint == leftPoint)
            return transform.right;

        if (sidePoint == rightPoint)
            return -transform.right;

        return Vector3.zero;
    }

    // 실제 위치 이동
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

    // 상대가 같은 쪽 포인트 근처에 있는지 확인
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

    // 너무 멀면 사용 못 하게
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

    // 생존자 상호작용 목록 등록
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