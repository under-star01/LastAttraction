using System.Collections;
using Mirror;
using UnityEngine;

public class Pallet : NetworkBehaviour, IInteractable
{
    // 판자는 버튼 1번 눌러서 바로 실행되는 Press 타입
    public InteractType InteractType => InteractType.Press;

    [Header("참조")]
    [SerializeField] private Animator animator;          // 판자 애니메이터
    [SerializeField] private Collider standingCollider;  // 세워져 있을 때 콜라이더
    [SerializeField] private Collider droppedCollider;   // 내려간 뒤 콜라이더
    [SerializeField] private Transform leftPoint;        // 왼쪽 사용 포인트
    [SerializeField] private Transform rightPoint;       // 오른쪽 사용 포인트

    [Header("이동/연출 설정")]
    [SerializeField] private Vector3 vaultOffset = new Vector3(0f, 0.2f, 0f); // 볼트 시 살짝 위로
    [SerializeField] private float moveToPointSpeed = 5f;   // 시작 포인트로 이동 속도
    [SerializeField] private float dropActionTime = 0.5f;   // 생존자 판자 내리기 시간
    [SerializeField] private float survivorVaultSpeed = 4f; // 생존자 판자 넘기 속도
    [SerializeField] private float breakActionTime = 2f;    // 킬러 판자 부수기 시간

    [Header("판정")]
    [SerializeField] private float useDistance = 2f;        // 사용 가능 거리
    [SerializeField] private float occupationRadius = 1f;   // 포인트 점유 검사 반경
    [SerializeField] private float stunTime = 1.2f;         // 판자 맞은 킬러 스턴 시간

    // 현재 판자가 내려가 있는지
    [SyncVar(hook = nameof(OnDroppedChanged))]
    private bool isDropped;

    // 누가 이 판자를 사용 중인지
    // 한번 선점되면 다른 사람은 못 쓰게 막는다.
    [SyncVar]
    private bool isBusy;

    // 현재 사용 중인 플레이어 netId
    [SyncVar]
    private uint currentActorNetId;

    // 현재 어떤 동작 중인지 따로 분리
    // 이 값을 따로 두는 이유:
    // 내리는 중 / 넘는 중 / 부수는 중을 명확하게 구분해서
    // 중간에 킬러가 들어오거나 다른 생존자가 들어오는 버그를 막기 위함
    [SyncVar] private bool isDropping;
    [SyncVar] private bool isVaulting;
    [SyncVar] private bool isBreaking;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // 시작 상태 반영
        ApplyDroppedState(isDropped);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 클라이언트 접속 시 현재 상태 반영
        ApplyDroppedState(isDropped);
    }

    // 상호작용 시작
    public void BeginInteract(GameObject actor)
    {
        if (actor == null)
            return;

        // 상호작용한 플레이어의 NetworkIdentity 찾기
        NetworkIdentity actorIdentity = actor.GetComponent<NetworkIdentity>();
        if (actorIdentity == null)
            actorIdentity = actor.GetComponentInParent<NetworkIdentity>();

        if (actorIdentity == null)
            return;

        // 서버면 바로 처리
        if (isServer)
            TryBeginInteractServer(actorIdentity);
        else
            CmdBeginInteract(actorIdentity.netId);
    }

    public void EndInteract()
    {
        // 판자는 Press 타입이라 따로 종료 처리 없음
    }

    [Command(requiresAuthority = false)]
    private void CmdBeginInteract(uint actorNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(actorNetId, out NetworkIdentity actorIdentity))
            return;

        TryBeginInteractServer(actorIdentity);
    }

    // 서버에서 실제 사용 시작 판정
    [Server]
    private void TryBeginInteractServer(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
            return;

        // 이미 사용 중이거나,
        // 내리는 중 / 넘는 중 / 부수는 중이면 새로 시작 불가
        if (isBusy || isDropping || isVaulting || isBreaking)
            return;

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
            return;

        bool isSurvivor = actor.CompareTag("Survivor");
        bool isKiller = actor.CompareTag("Killer");

        // 생존자 또는 킬러만 사용 가능
        if (!isSurvivor && !isKiller)
            return;

        // 너무 멀면 사용 불가
        if (!CanUse(actor.transform))
            return;

        // 현재 어느 쪽에서 접근했는지 계산
        Transform sidePoint = GetSidePointForActor(actor.transform);
        if (sidePoint == null)
            return;

        // 같은 쪽 포인트에 상대가 너무 가까이 있으면 막기
        string opponentTag = isSurvivor ? "Killer" : "Survivor";
        if (IsOpponentAtPoint(sidePoint, opponentTag))
            return;

        // 선점
        isBusy = true;
        currentActorNetId = actorIdentity.netId;

        // 아직 세워진 판자
        if (!isDropped)
        {
            // 세워진 판자를 내릴 수 있는 건 생존자뿐
            if (!isSurvivor)
            {
                StopUseServer();
                return;
            }

            StartCoroutine(DropRoutineServer(actorIdentity));
            return;
        }

        // 이미 내려진 판자
        if (isDropped)
        {
            // 생존자는 넘기
            if (isSurvivor)
            {
                StartCoroutine(VaultRoutineServer(actorIdentity));
                return;
            }

            // 킬러는 부수기
            if (isKiller)
            {
                StartCoroutine(BreakRoutineServer(actorIdentity));
                return;
            }
        }

        StopUseServer();
    }

    // --------------------------
    // 생존자 판자 내리기
    // --------------------------
    [Server]
    private IEnumerator DropRoutineServer(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
        {
            StopUseServer();
            yield break;
        }

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
        {
            StopUseServer();
            yield break;
        }

        // 내리는 중 상태 시작
        // 이 순간부터 킬러가 부수려고 해도 들어오지 못함
        isDropping = true;

        SurvivorMove move = actor.GetComponent<SurvivorMove>();
        CharacterController controller = actor.GetComponent<CharacterController>();

        Transform sidePoint = GetSidePointForActor(actor.transform);
        if (sidePoint == null)
        {
            StopUseServer();
            yield break;
        }

        // 이동 잠금 + 판자 방향 바라보기
        if (move != null)
        {
            move.SetMoveLock(true);

            Vector3 lookDir = GetLookDirection(sidePoint);
            if (lookDir.sqrMagnitude > 0.001f)
                move.FaceDirection(lookDir.normalized);

            move.StopAnimation();
        }

        // 직접 이동시킬 것이므로 컨트롤러 끔
        if (controller != null)
            controller.enabled = false;

        yield return null;

        // 자기 쪽 포인트로 이동
        yield return MoveActorToPoint(actor.transform, sidePoint.position, moveToPointSpeed);

        // 생존자 드롭 애니메이션
        if (move != null)
            move.PlayAnimation("Drop");

        // 판자 자체 애니메이션
        RpcPlayPalletTrigger("Drop");

        // 판자가 내려오면서 닿은 생존자 밀어내기
        PushOutServer();

        // 내려오는 판자에 킬러가 맞았는지 검사
        CheckKillerStunServer();

        // 액션 시간 대기
        yield return new WaitForSeconds(dropActionTime);

        // 실제로 내려진 상태 적용
        isDropped = true;
        ApplyDroppedState(true);

        // 컨트롤러 복구
        if (controller != null)
            controller.enabled = true;

        // 이동 잠금 해제
        if (move != null)
            move.SetMoveLock(false);

        isDropping = false;
        StopUseServer();
    }

    // --------------------------
    // 생존자 판자 넘기
    // --------------------------
    [Server]
    private IEnumerator VaultRoutineServer(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
        {
            StopUseServer();
            yield break;
        }

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
        {
            StopUseServer();
            yield break;
        }

        // 넘는 중 상태 시작
        // 이 순간부터 킬러가 부수려고 해도 들어오지 못함
        isVaulting = true;

        SurvivorMove move = actor.GetComponent<SurvivorMove>();
        CharacterController controller = actor.GetComponent<CharacterController>();

        Transform sidePoint = GetSidePointForActor(actor.transform);
        Transform oppositePoint = GetOppositePoint(sidePoint);

        if (sidePoint == null || oppositePoint == null)
        {
            StopUseServer();
            yield break;
        }

        // 이동 잠금 + 판자 방향 보기
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

        Vector3 startPos = sidePoint.position + vaultOffset;
        Vector3 endPos = oppositePoint.position + vaultOffset;

        // 먼저 자기 쪽 시작 포인트로 이동
        yield return MoveActorToPoint(actor.transform, startPos, moveToPointSpeed);

        // 볼트 애니메이션 실행
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

        if (controller != null)
            controller.enabled = true;

        if (move != null)
        {
            move.SetVaulting(false);
            move.SetMoveLock(false);
        }

        isVaulting = false;
        StopUseServer();
    }

    // --------------------------
    // 킬러 판자 부수기
    // --------------------------
    [Server]
    private IEnumerator BreakRoutineServer(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
        {
            StopUseServer();
            yield break;
        }

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
        {
            StopUseServer();
            yield break;
        }

        // 부수기 시작
        isBreaking = true;

        KillerState killerState = actor.GetComponent<KillerState>();
        CharacterController controller = actor.GetComponent<CharacterController>();
        Animator killerAnimator = actor.GetComponentInChildren<Animator>();

        Transform sidePoint = GetSidePointForActor(actor.transform);
        if (sidePoint == null)
        {
            StopUseServer();
            yield break;
        }

        // 중요:
        // 여기서만 Breaking 상태를 넣는다.
        // 즉, 진짜 부수기 시작이 확정된 뒤에만 상태 변경.
        if (killerState != null)
            killerState.ChangeState(KillerCondition.Breaking);

        if (controller != null)
            controller.enabled = false;

        yield return null;

        // 자기 쪽 포인트로 이동
        yield return MoveActorToPoint(actor.transform, sidePoint.position, moveToPointSpeed);

        // 판자 바라보게 정렬
        Vector3 lookDir = GetLookDirection(sidePoint);
        if (lookDir.sqrMagnitude > 0.001f)
            actor.transform.rotation = Quaternion.LookRotation(lookDir.normalized);

        // 킬러 부수기 애니메이션
        if (killerAnimator != null)
            killerAnimator.SetTrigger("Break");

        // 판자 자체 부서지는 애니메이션
        RpcPlayPalletTrigger("Break");

        yield return new WaitForSeconds(breakActionTime);

        if (controller != null)
            controller.enabled = true;

        if (killerState != null)
            killerState.ChangeState(KillerCondition.Idle);

        isBreaking = false;

        // 판자 제거
        NetworkServer.Destroy(gameObject);
    }

    // 내려진 판자 안에 있는 생존자 밀어내기
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

            // 판자 내린 본인은 제외
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

            // 판자 기준 좌우 판정
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

            StartCoroutine(KillerHitAlignRoutineServer(killerIdentity, killerInteractor));
        }
    }

    // 맞은 킬러를 판자 쪽으로 정렬한 뒤 스턴 적용
    [Server]
    private IEnumerator KillerHitAlignRoutineServer(NetworkIdentity killerIdentity, KillerInteractor killerInteractor)
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

        Transform sidePoint = GetSidePointForActor(killer.transform);
        if (sidePoint != null)
        {
            yield return MoveActorToPoint(killer.transform, sidePoint.position, moveToPointSpeed);

            Vector3 lookDir = GetLookDirection(sidePoint);
            if (lookDir.sqrMagnitude > 0.001f)
                killer.transform.rotation = Quaternion.LookRotation(lookDir.normalized);
        }

        killerInteractor.ApplyHitStun(stunTime);

        yield return new WaitForSeconds(stunTime);

        if (controller != null)
            controller.enabled = true;
    }

    // 사용 종료
    [Server]
    private void StopUseServer()
    {
        isBusy = false;
        currentActorNetId = 0;
    }

    // isDropped 값이 바뀌면 외형/콜라이더 반영
    private void OnDroppedChanged(bool oldValue, bool newValue)
    {
        ApplyDroppedState(newValue);
    }

    // 세워짐/내려짐 상태에 따라 콜라이더 전환
    private void ApplyDroppedState(bool dropped)
    {
        if (standingCollider != null)
            standingCollider.enabled = !dropped;

        if (droppedCollider != null)
            droppedCollider.enabled = dropped;
    }

    // 판자 애니메이션을 모든 클라이언트에서 재생
    [ClientRpc]
    private void RpcPlayPalletTrigger(string triggerName)
    {
        if (animator != null)
            animator.SetTrigger(triggerName);
    }

    // 플레이어가 판자의 왼쪽/오른쪽 어디에 있는지 판정
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

    // 반대편 포인트 구하기
    private Transform GetOppositePoint(Transform sidePoint)
    {
        if (sidePoint == leftPoint)
            return rightPoint;

        if (sidePoint == rightPoint)
            return leftPoint;

        return null;
    }

    // 해당 쪽에서 판자를 바라보는 방향 계산
    private Vector3 GetLookDirection(Transform sidePoint)
    {
        if (sidePoint == leftPoint)
            return transform.right;

        if (sidePoint == rightPoint)
            return -transform.right;

        return Vector3.zero;
    }

    // 서버에서 실제 위치 이동
    [Server]
    private IEnumerator MoveActorToPoint(Transform actor, Vector3 targetPos, float speed)
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

    // 특정 포인트 주변에 상대가 있는지 검사
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

    // 사용 가능 거리 검사
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

    // 생존자 로컬 상호작용 등록
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

    // 씬에서 점유 반경 확인용
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