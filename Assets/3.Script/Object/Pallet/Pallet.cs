using System.Collections;
using Mirror;
using UnityEngine;

public class Pallet : NetworkBehaviour, IInteractable
{
    // 판자는 버튼 한 번 누르면 실행되는 Press 타입
    public InteractType InteractType => InteractType.Press;

    [Header("참조")]
    [SerializeField] private Animator animator;          // 판자 자체 애니메이터
    [SerializeField] private Collider standingCollider;  // 세워져 있을 때 콜라이더
    [SerializeField] private Collider droppedCollider;   // 내려간 뒤 콜라이더
    [SerializeField] private Transform leftPoint;        // 왼쪽 상호작용 기준점
    [SerializeField] private Transform rightPoint;       // 오른쪽 상호작용 기준점
    [SerializeField] private Vector3 vaultOffset = new Vector3(0f, 0.2f, 0f); // 볼트할 때 살짝 위로 띄울 값

    [Header("이동/연출 설정")]
    [SerializeField] private float moveToPointSpeed = 5f;   // 시작 위치 포인트로 이동하는 속도
    [SerializeField] private float dropActionTime = 0.5f;   // 판자 내리기 액션 시간
    [SerializeField] private float survivorVaultSpeed = 4f; // 생존자 판자 넘는 속도
    [SerializeField] private float breakActionTime = 2f;    // 살인마 판자 부수는 시간

    [Header("판정")]
    [SerializeField] private float useDistance = 2f;        // 판자 사용 가능 거리
    [SerializeField] private float occupationRadius = 1f;   // 포인트 주변에 상대가 있는지 검사할 반경
    [SerializeField] private float stunTime = 1.2f;         // 판자 맞은 살인마 스턴 시간

    // 현재 판자가 내려가 있는지 여부
    // SyncVar라서 서버 값이 모든 클라이언트에 자동 동기화됨
    [SyncVar(hook = nameof(OnDroppedChanged))]
    private bool isDropped;

    // 지금 누가 사용 중인지
    // 동시에 여러 명이 같은 판자를 쓰지 못하게 막기 위해 사용
    [SyncVar]
    private bool isBusy;

    // 현재 사용 중인 플레이어의 netId
    // 누가 이 판자를 쓰고 있는지 서버가 기억함
    [SyncVar]
    private uint currentActorNetId;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // 시작 상태에 맞게 콜라이더 적용
        ApplyDroppedState(isDropped);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 클라이언트가 접속했을 때도 현재 판자 상태를 외형에 반영
        ApplyDroppedState(isDropped);
    }

    // 로컬에서 상호작용 시작
    // 실제 판정은 서버에서 하게 해야 멀티에서 안전함
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
        {
            TryBeginInteractServer(actorIdentity);
        }
        // 클라이언트면 서버에 요청
        else
        {
            CmdBeginInteract(actorIdentity.netId);
        }
    }

    public void EndInteract()
    {
        // 판자는 Press 타입이라 별도 종료 없음
    }

    // 클라이언트 -> 서버
    // "이 플레이어가 판자를 쓰려고 했어요" 라고 요청
    [Command(requiresAuthority = false)]
    private void CmdBeginInteract(uint actorNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(actorNetId, out NetworkIdentity actorIdentity))
            return;

        TryBeginInteractServer(actorIdentity);
    }

    // 서버에서 실제 사용 가능 여부를 체크하고
    // 생존자면 내리기/넘기, 살인마면 부수기를 시작
    [Server]
    private void TryBeginInteractServer(NetworkIdentity actorIdentity)
    {
        if (actorIdentity == null)
            return;

        // 이미 다른 누군가가 사용 중이면 막음
        if (isBusy)
            return;

        GameObject actor = actorIdentity.gameObject;
        if (actor == null)
            return;

        bool isSurvivor = actor.CompareTag("Survivor");
        bool isKiller = actor.CompareTag("Killer");

        // 생존자나 살인마만 사용 가능
        if (!isSurvivor && !isKiller)
            return;

        // 너무 멀리 있으면 사용 불가
        if (!CanUse(actor.transform))
            return;

        // 내가 어느 쪽에서 접근했는지 계산
        Transform sidePoint = GetSidePointForActor(actor.transform);
        if (sidePoint == null)
            return;

        // 같은 쪽 포인트 주변에 상대가 있으면 시작 막기
        string opponentTag = isSurvivor ? "Killer" : "Survivor";
        if (IsOpponentAtPoint(sidePoint, opponentTag))
            return;

        // 이제 이 판자는 사용 중 상태
        isBusy = true;
        currentActorNetId = actorIdentity.netId;

        // 아직 안 내려진 판자
        if (!isDropped)
        {
            // 판자 내리기는 생존자만 가능
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

            // 살인마는 부수기
            if (isKiller)
            {
                StartCoroutine(BreakRoutineServer(actorIdentity));
                return;
            }
        }

        StopUseServer();
    }

    // 생존자 판자 내리기
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

        SurvivorMove move = actor.GetComponent<SurvivorMove>();
        CharacterController controller = actor.GetComponent<CharacterController>();

        // 현재 서 있는 쪽 포인트
        Transform sidePoint = GetSidePointForActor(actor.transform);
        if (sidePoint == null)
        {
            StopUseServer();
            yield break;
        }

        // 이동 막고, 판자 방향 바라보게
        if (move != null)
        {
            move.SetMoveLock(true);

            Vector3 lookDir = GetLookDirection(sidePoint);
            if (lookDir.sqrMagnitude > 0.001f)
                move.FaceDirection(lookDir.normalized);

            // 이동 애니메이션 정지
            move.StopAnimation();
        }

        // 직접 위치를 옮길 거라 CharacterController 잠깐 끔
        if (controller != null)
            controller.enabled = false;

        yield return null;

        // 내 쪽 포인트까지 이동
        yield return MoveActorToPoint(actor.transform, sidePoint.position, moveToPointSpeed);

        // 생존자 드롭 애니메이션
        if (move != null)
            move.PlayAnimation("Drop");

        // 판자 자체 애니메이션도 모든 클라에서 재생
        RpcPlayPalletTrigger("Drop");

        // 내리는 액션 시간만큼 대기
        yield return new WaitForSeconds(dropActionTime);

        // 실제로 판자가 내려간 상태로 변경
        isDropped = true;

        // 내려가면서 살인마가 맞았는지 검사
        CheckKillerStunServer();

        // 컨트롤러 복구
        if (controller != null)
            controller.enabled = true;

        // 이동 다시 허용
        if (move != null)
            move.SetMoveLock(false);

        StopUseServer();
    }

    // 생존자 판자 넘기
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

        SurvivorMove move = actor.GetComponent<SurvivorMove>();
        CharacterController controller = actor.GetComponent<CharacterController>();

        Transform sidePoint = GetSidePointForActor(actor.transform);
        Transform oppositePoint = GetOppositePoint(sidePoint);

        if (sidePoint == null || oppositePoint == null)
        {
            StopUseServer();
            yield break;
        }

        // 이동 막고 판자 방향 보기
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

        // 살짝 위로 띄운 시작점 / 도착점
        Vector3 startPos = sidePoint.position + vaultOffset;
        Vector3 endPos = oppositePoint.position + vaultOffset;

        // 먼저 자기 쪽 시작점으로 이동
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

        StopUseServer();
    }

    // 살인마 판자 부수기
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

        KillerState killerState = actor.GetComponent<KillerState>();
        CharacterController controller = actor.GetComponent<CharacterController>();
        Animator killerAnimator = actor.GetComponentInChildren<Animator>();

        Transform sidePoint = GetSidePointForActor(actor.transform);
        if (sidePoint == null)
        {
            StopUseServer();
            yield break;
        }

        // 살인마 상태를 부수기 상태로 변경
        if (killerState != null)
            killerState.ChangeState(KillerCondition.Breaking);

        // 직접 위치 이동하므로 컨트롤러 끔
        if (controller != null)
            controller.enabled = false;

        yield return null;

        // 자기 쪽 포인트로 이동
        yield return MoveActorToPoint(actor.transform, sidePoint.position, moveToPointSpeed);

        // 판자 바라보게 정렬
        Vector3 lookDir = GetLookDirection(sidePoint);
        if (lookDir.sqrMagnitude > 0.001f)
            actor.transform.rotation = Quaternion.LookRotation(lookDir.normalized);

        // 살인마 부수기 애니메이션
        if (killerAnimator != null)
            killerAnimator.SetTrigger("Break");

        // 판자 부서지는 애니메이션
        RpcPlayPalletTrigger("Break");

        yield return new WaitForSeconds(breakActionTime);

        if (controller != null)
            controller.enabled = true;

        if (killerState != null)
            killerState.ChangeState(KillerCondition.Idle);

        // 서버에서 네트워크 오브젝트 제거
        NetworkServer.Destroy(gameObject);
    }

    // 판자 맞은 살인마 정렬 + 스턴
    [Server]
    private void CheckKillerStunServer()
    {
        if (droppedCollider == null)
            return;

        // 내려진 판자 콜라이더 범위 안의 오브젝트 검사
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

            // KillerInteractor 찾기
            KillerInteractor killerInteractor = hit.GetComponent<KillerInteractor>();
            if (killerInteractor == null)
                killerInteractor = hit.GetComponentInParent<KillerInteractor>();

            // 네트워크 아이덴티티 찾기
            NetworkIdentity killerIdentity = hit.GetComponent<NetworkIdentity>();
            if (killerIdentity == null)
                killerIdentity = hit.GetComponentInParent<NetworkIdentity>();

            if (killerInteractor == null || killerIdentity == null)
                continue;

            // 맞은 살인마를 포인트 쪽으로 정렬 후 스턴
            StartCoroutine(KillerHitAlignRoutineServer(killerIdentity, killerInteractor));
        }
    }

    [Server]
    private IEnumerator KillerHitAlignRoutineServer(NetworkIdentity killerIdentity, KillerInteractor killerInteractor)
    {
        if (killerIdentity == null || killerInteractor == null)
            yield break;

        GameObject killer = killerIdentity.gameObject;
        if (killer == null)
            yield break;

        CharacterController controller = killer.GetComponent<CharacterController>();

        // 직접 위치 정렬 위해 컨트롤러 끔
        if (controller != null)
            controller.enabled = false;

        yield return null;

        Transform sidePoint = GetSidePointForActor(killer.transform);
        if (sidePoint != null)
        {
            // 현재 서 있는 쪽 포인트로 이동
            yield return MoveActorToPoint(killer.transform, sidePoint.position, moveToPointSpeed);

            // 판자 방향 바라보게
            Vector3 lookDir = GetLookDirection(sidePoint);
            if (lookDir.sqrMagnitude > 0.001f)
                killer.transform.rotation = Quaternion.LookRotation(lookDir.normalized);
        }

        // 살인마 스턴 적용
        killerInteractor.ApplyHitStun(stunTime);

        yield return new WaitForSeconds(stunTime);

        if (controller != null)
            controller.enabled = true;
    }

    // 공통 함수

    // 사용 종료
    // 바쁜 상태와 현재 사용자 정보 초기화
    [Server]
    private void StopUseServer()
    {
        isBusy = false;
        currentActorNetId = 0;
    }

    // isDropped 값이 바뀌면 자동 호출됨
    private void OnDroppedChanged(bool oldValue, bool newValue)
    {
        ApplyDroppedState(newValue);
    }

    // 판자 상태에 맞게 콜라이더 on/off
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

    // 플레이어가 판자의 왼쪽/오른쪽 어디에 있는지 판단
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

    // 해당 포인트 기준으로 판자 바라보는 방향 계산
    private Vector3 GetLookDirection(Transform sidePoint)
    {
        if (sidePoint == leftPoint)
            return transform.right;

        if (sidePoint == rightPoint)
            return -transform.right;

        return Vector3.zero;
    }

    // 서버에서 실제 위치를 부드럽게 이동
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

    // 특정 포인트 주변에 상대가 있는지 체크
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

    // 너무 멀면 판자 사용 못 하게
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

    // 생존자 쪽 상호작용 등록
    // SurvivorInteractor가 근처 상호작용 목록에 이 판자를 넣음
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Survivor"))
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        if (interactor == null)
            return;

        // 내 로컬 플레이어만 등록
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

    // 씬에서 반경 확인용
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