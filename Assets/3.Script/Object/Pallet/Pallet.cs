using System.Collections;
using Mirror;
using UnityEngine;

public class Pallet : NetworkBehaviour, IInteractable
{
    // 판자는 누르면 즉시 동작하는 Press 타입 상호작용이다.
    public InteractType InteractType => InteractType.Press;

    [Header("참조")]
    [SerializeField] private Animator animator;                  // 판자 애니메이터
    [SerializeField] private Collider standingCollider;          // 세워진 상태 콜라이더
    [SerializeField] private Collider droppedCollider;           // 내려진 상태 콜라이더
    [SerializeField] private Transform leftPoint;                // 판자 왼쪽 사용 위치
    [SerializeField] private Transform rightPoint;               // 판자 오른쪽 사용 위치

    [Header("이동/연출 설정")]
    [SerializeField] private float moveToPointSpeed = 5f;        // 시작 위치로 이동하는 속도
    [SerializeField] private float dropActionTime = 0.5f;        // 판자 내리기 연출 시간
    [SerializeField] private float survivorVaultSpeed = 4f;      // 생존자 판자 넘기 속도
    [SerializeField] private float breakActionTime = 2f;         // 살인마 판자 부수기 시간

    [Header("판정")]
    [SerializeField] private float useDistance = 2f;             // 서버 기준 사용 가능 거리
    [SerializeField] private float occupationRadius = 1f;        // 사용 위치 점유 검사 반경
    [SerializeField] private float stunTime = 1.2f;              // 판자 스턴 시간

    [Header("오디오")]
    [SerializeField] private AudioKey vaultSoundKey = AudioKey.ObjectVault;   // 판자 넘는 소리
    [SerializeField] private AudioKey dropSoundKey = AudioKey.PalletDrop;     // 판자 내리는 소리
    [SerializeField] private AudioKey breakSoundKey = AudioKey.PalletBreak;   // 판자 부수는 소리

    // 판자가 내려졌는지 여부다.
    // Prison의 isDoorOpen처럼 상태를 SyncVar로 동기화하고, hook에서 클라이언트 애니메이션을 실행한다.
    [SyncVar(hook = nameof(OnDroppedChanged))]
    private bool isDropped;

    // 현재 판자가 사용 중인지 여부다.
    [SyncVar] private bool isBusy;

    // 현재 판자를 사용 중인 액터 netId다.
    [SyncVar] private uint currentActorNetId;

    // 판자 내리기 중인지 여부다.
    [SyncVar] private bool isDropping;

    // 판자 넘기 중인지 여부다.
    [SyncVar] private bool isVaulting;

    // 판자 부수기 중인지 여부다.
    [SyncVar] private bool isBreaking;

    // 로컬 플레이어의 SurvivorInteractor 참조다.
    private SurvivorInteractor localInteractor;

    // 로컬 플레이어가 판자 트리거 안에 있는지 여부다.
    private bool isLocalInside;

    private void Awake()
    {
        // 애니메이터가 비어 있으면 자식에서 자동으로 찾는다.
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // 시작 시 현재 SyncVar 상태에 맞게 콜라이더를 적용한다.
        ApplyDroppedState(isDropped);
    }

    public override void OnStartClient()
    {
        // Mirror 기본 클라이언트 시작 처리를 실행한다.
        base.OnStartClient();

        // 늦게 접속한 클라이언트도 현재 판자 상태에 맞게 콜라이더를 적용한다.
        ApplyDroppedState(isDropped);

        // 이미 내려진 상태로 접속했다면 판자 Animator도 내려진 상태로 맞춘다.
        if (isDropped)
            PlayPalletTrigger("Drop");
    }

    private void Update()
    {
        // 로컬 플레이어가 판자 트리거 안에 있으면 후보 등록 상태를 계속 보정한다.
        RefreshLocalAvailability();
    }

    public void BeginInteract(GameObject actor)
    {
        // 액터가 없으면 시작하지 않는다.
        if (actor == null)
            return;

        // 액터의 NetworkIdentity를 찾는다.
        NetworkIdentity actorIdentity = actor.GetComponent<NetworkIdentity>();

        // 자식 콜라이더 구조일 수 있으므로 부모에서도 찾는다.
        if (actorIdentity == null)
            actorIdentity = actor.GetComponentInParent<NetworkIdentity>();

        // NetworkIdentity가 없으면 네트워크 상호작용을 할 수 없다.
        if (actorIdentity == null)
            return;

        // 서버라면 바로 시작 판정을 한다.
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
        // 클라이언트가 보낸 netId로 서버의 NetworkIdentity를 찾는다.
        if (!NetworkServer.spawned.TryGetValue(actorNetId, out NetworkIdentity actorIdentity))
            return;

        // 서버에서 실제 시작 판정을 한다.
        TryBegin(actorIdentity);
    }

    // 서버에서 실제 상호작용 가능 여부를 검사하고 시작한다.
    [Server]
    private void TryBegin(NetworkIdentity actorIdentity)
    {
        // 액터 정보가 없으면 중단한다.
        if (actorIdentity == null)
            return;

        // 이미 다른 동작 중이면 중단한다.
        if (isBusy || isDropping || isVaulting || isBreaking)
            return;

        // 실제 액터 GameObject를 가져온다.
        GameObject actor = actorIdentity.gameObject;

        // 액터가 없으면 중단한다.
        if (actor == null)
            return;

        // 생존자인지 확인한다.
        bool isSurvivor = actor.CompareTag("Survivor");

        // 살인마인지 확인한다.
        bool isKiller = actor.CompareTag("Killer");

        // 생존자도 살인마도 아니면 사용할 수 없다.
        if (!isSurvivor && !isKiller)
            return;

        // 서버 기준 거리 검사에서 멀면 사용할 수 없다.
        if (!CanUse(actor.transform))
            return;

        // 액터가 현재 어느 쪽에 있는지 확인한다.
        Transform sidePoint = GetSide(actor.transform);

        // 사용 위치를 못 찾으면 중단한다.
        if (sidePoint == null)
            return;

        // 생존자는 살인마 점유를 검사하고, 살인마는 생존자 점유를 검사한다.
        string opponentTag = isSurvivor ? "Killer" : "Survivor";

        // 사용 위치에 상대가 있으면 겹침 방지를 위해 사용하지 않는다.
        if (IsOpponentAtPoint(sidePoint, opponentTag))
            return;

        // 판자를 사용 중 상태로 바꾼다.
        isBusy = true;

        // 현재 사용자를 저장한다.
        currentActorNetId = actorIdentity.netId;

        // 세워진 판자는 생존자만 내릴 수 있다.
        if (!isDropped)
        {
            // 살인마는 세워진 판자를 사용할 수 없다.
            if (!isSurvivor)
            {
                StopUse();
                return;
            }

            // 생존자 판자 내리기를 시작한다.
            StartCoroutine(Drop(actorIdentity));
            return;
        }

        // 내려진 판자는 생존자는 넘고, 살인마는 부술 수 있다.
        if (isDropped)
        {
            // 생존자는 판자를 넘는다.
            if (isSurvivor)
            {
                StartCoroutine(Vault(actorIdentity));
                return;
            }

            // 살인마는 판자를 부순다.
            if (isKiller)
            {
                StartCoroutine(Break(actorIdentity));
                return;
            }
        }

        // 어떤 동작도 시작하지 못했으면 사용 상태를 정리한다.
        StopUse();
    }

    // 생존자 판자 내리기 루틴이다.
    [Server]
    private IEnumerator Drop(NetworkIdentity actorIdentity)
    {
        // 액터 정보가 없으면 정리 후 종료한다.
        if (actorIdentity == null)
        {
            StopUse();
            yield break;
        }

        // 실제 액터 GameObject를 가져온다.
        GameObject actor = actorIdentity.gameObject;

        // 액터가 없으면 정리 후 종료한다.
        if (actor == null)
        {
            StopUse();
            yield break;
        }

        // 판자 내리기 중 상태를 켠다.
        isDropping = true;

        // 생존자 이동 컴포넌트를 가져온다.
        SurvivorMove move = actor.GetComponent<SurvivorMove>();

        // 생존자 행동 상태 컴포넌트를 가져온다.
        SurvivorActionState act = actor.GetComponent<SurvivorActionState>();

        // CharacterController를 가져온다.
        CharacterController controller = actor.GetComponent<CharacterController>();

        // 현재 액터가 있는 쪽 포인트를 구한다.
        Transform sidePoint = GetSide(actor.transform);

        // 사용 위치를 못 찾으면 정리 후 종료한다.
        if (sidePoint == null)
        {
            isDropping = false;
            StopUse();
            yield break;
        }

        // 내리기 시작 전에 이동 잠금, 방향 정렬, 스킬 해제를 처리한다.
        if (move != null)
        {
            // 상호작용 중 이동을 막는다.
            move.SetMoveLock(true);

            // 판자를 바라볼 방향을 구한다.
            Vector3 lookDir = GetLook(sidePoint);

            // 방향이 유효하면 모델을 판자 방향으로 돌린다.
            if (lookDir.sqrMagnitude > 0.001f)
                move.FaceDirection(lookDir.normalized);

            // 이동 애니메이션을 idle 쪽으로 정리한다.
            move.StopAnimation();

            // 카메라 스킬 애니메이션이 켜져 있으면 끈다.
            move.SetCamAnim(false);
        }

        // 행동 상태의 카메라 스킬 상태도 끈다.
        if (act != null)
            act.SetCam(false);

        // 직접 위치 이동을 위해 CharacterController를 잠시 끈다.
        if (controller != null)
            controller.enabled = false;

        // CharacterController 비활성화가 반영될 시간을 한 프레임 준다.
        yield return null;

        // 자기 쪽 포인트로 이동한다.
        yield return MoveTo(actor.transform, sidePoint.position, moveToPointSpeed);

        // 판자 내리기 소리를 모든 클라이언트에게 3D 사운드로 재생한다.
        PlayOneShotSound(dropSoundKey, transform.position);

        // 생존자 판자 내리기 애니메이션을 재생한다.
        if (move != null)
            move.PlayAnimation("Drop");

        isDropped = true;

        // 서버에서도 즉시 콜라이더 상태를 적용한다.
        ApplyDroppedState(true);

        // 내려오는 판자 안에 겹친 생존자를 정리한다.
        PushOutServer();

        // 내려오는 판자에 맞은 킬러가 있으면 스턴을 적용한다.
        CheckKillerStunServer();

        // 내리기 연출 시간만큼 기다린다.
        yield return new WaitForSeconds(dropActionTime);

        // CharacterController를 다시 켠다.
        if (controller != null)
            controller.enabled = true;

        // 이동 잠금을 해제한다.
        if (move != null)
            move.SetMoveLock(false);

        // 판자 내리기 상태를 해제한다.
        isDropping = false;

        // 판자 사용 상태를 초기화한다.
        StopUse();

        // 내린 뒤에도 로컬 플레이어가 판자 트리거 안에 있으면 다시 후보로 잡히게 한다.
        RpcRefreshLocalUse();
    }

    // 생존자 판자 넘기 루틴이다.
    [Server]
    private IEnumerator Vault(NetworkIdentity actorIdentity)
    {
        // 액터 정보가 없으면 정리 후 종료한다.
        if (actorIdentity == null)
        {
            StopUse();
            yield break;
        }

        // 실제 액터 GameObject를 가져온다.
        GameObject actor = actorIdentity.gameObject;

        // 액터가 없으면 정리 후 종료한다.
        if (actor == null)
        {
            StopUse();
            yield break;
        }

        // 판자 넘기 중 상태를 켠다.
        isVaulting = true;

        // 생존자 이동 컴포넌트를 가져온다.
        SurvivorMove move = actor.GetComponent<SurvivorMove>();

        // 생존자 행동 상태 컴포넌트를 가져온다.
        SurvivorActionState act = actor.GetComponent<SurvivorActionState>();

        // CharacterController를 가져온다.
        CharacterController controller = actor.GetComponent<CharacterController>();

        // 현재 액터가 있는 쪽 포인트를 구한다.
        Transform sidePoint = GetSide(actor.transform);

        // 반대편 포인트를 구한다.
        Transform oppositePoint = GetOpposite(sidePoint);

        // 양쪽 포인트 중 하나라도 없으면 정리 후 종료한다.
        if (sidePoint == null || oppositePoint == null)
        {
            isVaulting = false;
            StopUse();
            yield break;
        }

        // 넘기 시작 전에 이동 잠금, 방향 정렬, 스킬 해제를 처리한다.
        if (move != null)
        {
            // 상호작용 중 이동을 막는다.
            move.SetMoveLock(true);

            // 판자를 바라볼 방향을 구한다.
            Vector3 lookDir = GetLook(sidePoint);

            // 방향이 유효하면 모델을 판자 방향으로 돌린다.
            if (lookDir.sqrMagnitude > 0.001f)
                move.FaceDirection(lookDir.normalized);

            // 이동 애니메이션을 idle 쪽으로 정리한다.
            move.StopAnimation();

            // 카메라 스킬 애니메이션이 켜져 있으면 끈다.
            move.SetCamAnim(false);
        }

        // 행동 상태를 정리한다.
        if (act != null)
        {
            // 카메라 스킬 상태를 끈다.
            act.SetCam(false);

            // 현재 행동을 Vault로 설정한다.
            act.SetAct(SurvivorAction.Vault);
        }

        // 직접 위치 이동을 위해 CharacterController를 잠시 끈다.
        if (controller != null)
            controller.enabled = false;

        // CharacterController 비활성화가 반영될 시간을 한 프레임 준다.
        yield return null;

        // 먼저 자기 쪽 시작 위치로 이동한다.
        yield return MoveTo(actor.transform, sidePoint.position, moveToPointSpeed);

        // 판자 넘기 소리를 모든 클라이언트에게 3D 사운드로 재생한다.
        PlayOneShotSound(vaultSoundKey, transform.position);

        // 넘기 애니메이션을 재생한다.
        if (move != null)
        {
            // Animator Bool을 켠다.
            move.SetVaulting(true);

            // 왼쪽/오른쪽 방향에 맞는 애니메이션 Trigger를 사용한다.
            if (sidePoint == leftPoint)
                move.PlayAnimation("LeftVault");
            else
                move.PlayAnimation("RightVault");
        }

        // 반대편 위치로 이동한다.
        yield return MoveTo(actor.transform, oppositePoint.position, survivorVaultSpeed);

        // 이동이 끝났으므로 CharacterController를 다시 켠다.
        if (controller != null)
            controller.enabled = true;

        // 이동 잠금과 볼트 애니메이션 Bool을 해제한다.
        if (move != null)
        {
            move.SetVaulting(false);
            move.SetMoveLock(false);
        }

        // Vault 행동 상태를 해제한다.
        if (act != null)
            act.ClearAct(SurvivorAction.Vault);

        // 판자 넘기 상태를 해제한다.
        isVaulting = false;

        // 판자 사용 상태를 초기화한다.
        StopUse();

        // 넘은 뒤에도 로컬 플레이어가 판자 트리거 안에 있으면 다시 후보로 잡히게 한다.
        RpcRefreshLocalUse();
    }

    // 킬러 판자 부수기 루틴이다.
    [Server]
    private IEnumerator Break(NetworkIdentity actorIdentity)
    {
        // 액터 정보가 없으면 정리 후 종료한다.
        if (actorIdentity == null)
        {
            StopUse();
            yield break;
        }

        // 실제 액터 GameObject를 가져온다.
        GameObject actor = actorIdentity.gameObject;

        // 액터가 없으면 정리 후 종료한다.
        if (actor == null)
        {
            StopUse();
            yield break;
        }

        // 판자 부수기 중 상태를 켠다.
        isBreaking = true;

        // 살인마 상태 컴포넌트를 가져온다.
        KillerState killerState = actor.GetComponent<KillerState>();

        // CharacterController를 가져온다.
        CharacterController controller = actor.GetComponent<CharacterController>();

        // 살인마 애니메이터를 가져온다.
        Animator killerAnimator = actor.GetComponentInChildren<Animator>();

        // 현재 액터가 있는 쪽 포인트를 구한다.
        Transform sidePoint = GetSide(actor.transform);

        // 사용 위치를 못 찾으면 정리 후 종료한다.
        if (sidePoint == null)
        {
            isBreaking = false;
            StopUse();
            yield break;
        }

        // 살인마 상태를 Breaking으로 바꾼다.
        if (killerState != null)
            killerState.ChangeState(KillerCondition.Breaking);

        // 직접 위치 이동을 위해 CharacterController를 잠시 끈다.
        if (controller != null)
            controller.enabled = false;

        // CharacterController 비활성화가 반영될 시간을 한 프레임 준다.
        yield return null;

        // 자기 쪽 포인트로 이동한다.
        yield return MoveTo(actor.transform, sidePoint.position, moveToPointSpeed);

        // 판자를 바라볼 방향을 구한다.
        Vector3 lookDir = GetLook(sidePoint);

        // 방향이 유효하면 살인마를 판자 방향으로 돌린다.
        if (lookDir.sqrMagnitude > 0.001f)
            actor.transform.rotation = Quaternion.LookRotation(lookDir.normalized);

        // 판자 부수기 소리를 모든 클라이언트에게 3D 사운드로 재생한다.
        PlayOneShotSound(breakSoundKey, transform.position);

        // 살인마 부수기 애니메이션을 재생한다.
        if (killerAnimator != null)
            killerAnimator.SetTrigger("Break");

        // 부수기는 isDropped 같은 상태 변경이 아니라 일회성 연출이므로 RPC로 유지한다.
        RpcPlayPalletTrigger("Break");

        // 부수기 시간만큼 기다린다.
        yield return new WaitForSeconds(breakActionTime);

        // CharacterController를 다시 켠다.
        if (controller != null)
            controller.enabled = true;

        // 살인마 상태를 Idle로 되돌린다.
        if (killerState != null)
            killerState.ChangeState(KillerCondition.Idle);

        // 판자 부수기 상태를 해제한다.
        isBreaking = false;

        // 부수기 완료 후 판자 네트워크 오브젝트를 제거한다.
        NetworkServer.Destroy(gameObject);
    }

    // 내려진 판자 안에 겹친 생존자를 좌우 포인트 쪽으로 빼낸다.
    [Server]
    private void PushOutServer()
    {
        // 내려진 판자 콜라이더가 없으면 처리하지 않는다.
        if (droppedCollider == null)
            return;

        // 내려진 판자 콜라이더 영역 안의 콜라이더를 찾는다.
        Collider[] hits = Physics.OverlapBox(
            droppedCollider.bounds.center,
            droppedCollider.bounds.extents,
            droppedCollider.transform.rotation
        );

        // 감지된 콜라이더를 순회한다.
        for (int i = 0; i < hits.Length; i++)
        {
            // 현재 감지된 콜라이더다.
            Collider hit = hits[i];

            // 생존자가 아니면 무시한다.
            if (!hit.CompareTag("Survivor"))
                continue;

            // 감지된 대상의 NetworkIdentity를 찾는다.
            NetworkIdentity identity = hit.GetComponent<NetworkIdentity>();

            // 자식 콜라이더일 수 있으므로 부모에서도 찾는다.
            if (identity == null)
                identity = hit.GetComponentInParent<NetworkIdentity>();

            // 판자를 내린 본인은 제외한다.
            if (identity != null && identity.netId == currentActorNetId)
                continue;

            // 기본 이동 대상은 감지된 Transform이다.
            Transform target = hit.transform;

            // 생존자 이동 컴포넌트를 찾는다.
            SurvivorMove move = hit.GetComponent<SurvivorMove>();

            // 자식 콜라이더일 수 있으므로 부모에서도 찾는다.
            if (move == null)
                move = hit.GetComponentInParent<SurvivorMove>();

            // SurvivorMove가 있으면 실제 캐릭터 루트를 대상으로 사용한다.
            if (move != null)
                target = move.transform;

            // CharacterController를 찾는다.
            CharacterController controller = target.GetComponent<CharacterController>();

            // 부모에도 있을 수 있으므로 부모에서 다시 찾는다.
            if (controller == null)
                controller = target.GetComponentInParent<CharacterController>();

            // 대상 위치를 판자 기준 로컬 좌표로 변환한다.
            Vector3 localPos = transform.InverseTransformPoint(target.position);

            // 순간이동할 위치를 저장한다.
            Vector3 teleportPos;

            // 왼쪽에 있으면 왼쪽 포인트로 보낸다.
            if (localPos.x < 0f)
                teleportPos = leftPoint.position;
            else
                teleportPos = rightPoint.position;

            // 기존 높이는 유지한다.
            teleportPos.y = target.position.y;

            // 순간이동 전 CharacterController를 끈다.
            if (controller != null)
                controller.enabled = false;

            // 겹치지 않도록 위치를 보정한다.
            target.position = teleportPos;

            // 순간이동 후 CharacterController를 다시 켠다.
            if (controller != null)
                controller.enabled = true;
        }
    }

    // 내려오는 판자에 킬러가 맞았는지 검사한다.
    [Server]
    private void CheckKillerStunServer()
    {
        // 내려진 판자 콜라이더가 없으면 처리하지 않는다.
        if (droppedCollider == null)
            return;

        // 내려진 판자 콜라이더 영역 안의 콜라이더를 찾는다.
        Collider[] hits = Physics.OverlapBox(
            droppedCollider.bounds.center,
            droppedCollider.bounds.extents,
            droppedCollider.transform.rotation
        );

        // 감지된 콜라이더를 순회한다.
        for (int i = 0; i < hits.Length; i++)
        {
            // 현재 감지된 콜라이더다.
            Collider hit = hits[i];

            // 킬러가 아니면 무시한다.
            if (!hit.CompareTag("Killer"))
                continue;

            // 킬러 상호작용 컴포넌트를 찾는다.
            KillerInteractor killerInteractor = hit.GetComponent<KillerInteractor>();

            // 자식 콜라이더일 수 있으므로 부모에서도 찾는다.
            if (killerInteractor == null)
                killerInteractor = hit.GetComponentInParent<KillerInteractor>();

            // 킬러 NetworkIdentity를 찾는다.
            NetworkIdentity killerIdentity = hit.GetComponent<NetworkIdentity>();

            // 자식 콜라이더일 수 있으므로 부모에서도 찾는다.
            if (killerIdentity == null)
                killerIdentity = hit.GetComponentInParent<NetworkIdentity>();

            // 필요한 컴포넌트가 없으면 무시한다.
            if (killerInteractor == null || killerIdentity == null)
                continue;

            // 맞은 킬러를 정렬한 뒤 스턴을 적용한다.
            StartCoroutine(KillerHitAlign(killerIdentity, killerInteractor));
        }
    }

    // 맞은 킬러를 판자 쪽으로 정렬한 뒤 스턴 적용한다.
    [Server]
    private IEnumerator KillerHitAlign(NetworkIdentity killerIdentity, KillerInteractor killerInteractor)
    {
        // 필요한 참조가 없으면 종료한다.
        if (killerIdentity == null || killerInteractor == null)
            yield break;

        // 킬러 GameObject를 가져온다.
        GameObject killer = killerIdentity.gameObject;

        // 킬러가 없으면 종료한다.
        if (killer == null)
            yield break;

        // CharacterController를 찾는다.
        CharacterController controller = killer.GetComponent<CharacterController>();

        // 직접 위치 이동을 위해 CharacterController를 잠시 끈다.
        if (controller != null)
            controller.enabled = false;

        // CharacterController 비활성화가 반영될 시간을 한 프레임 준다.
        yield return null;

        // 킬러가 있는 쪽 포인트를 구한다.
        Transform sidePoint = GetSide(killer.transform);

        // 포인트가 있으면 정렬한다.
        if (sidePoint != null)
        {
            // 킬러를 사용 위치로 이동시킨다.
            yield return MoveTo(killer.transform, sidePoint.position, moveToPointSpeed);

            // 판자를 바라볼 방향을 구한다.
            Vector3 lookDir = GetLook(sidePoint);

            // 방향이 유효하면 킬러를 판자 쪽으로 돌린다.
            if (lookDir.sqrMagnitude > 0.001f)
                killer.transform.rotation = Quaternion.LookRotation(lookDir.normalized);
        }

        // 킬러에게 스턴을 적용한다.
        killerInteractor.ApplyHitStun(stunTime);

        // 스턴 시간만큼 기다린다.
        yield return new WaitForSeconds(stunTime);

        // CharacterController를 다시 켠다.
        if (controller != null)
            controller.enabled = true;
    }

    // 서버에서 판자 사용 상태를 초기화한다.
    [Server]
    private void StopUse()
    {
        // 사용 중 상태를 해제한다.
        isBusy = false;

        // 현재 사용자 정보를 초기화한다.
        currentActorNetId = 0;
    }

    // 서버에서 판자 동작 완료 후 클라이언트에게 후보 갱신을 요청한다.
    [ClientRpc]
    private void RpcRefreshLocalUse()
    {
        // 로컬 플레이어가 판자 트리거 안에 있다면 다시 후보로 등록한다.
        RefreshLocalAvailability();
    }

    // 로컬 플레이어 기준으로 판자 후보 등록 상태를 갱신한다.
    private void RefreshLocalAvailability()
    {
        // 로컬 플레이어가 판자 트리거 안에 없으면 처리하지 않는다.
        if (!isLocalInside)
            return;

        // 로컬 상호작용 컴포넌트가 없으면 처리하지 않는다.
        if (localInteractor == null)
            return;

        // 판자가 어떤 동작 중이면 후보에서 제거한다.
        if (isBusy || isDropping || isVaulting || isBreaking)
        {
            localInteractor.ClearInteractable(this);
            return;
        }

        // 사용 가능한 상태면 후보로 다시 등록한다.
        localInteractor.SetInteractable(this);
    }

    // 판자 내려짐 상태가 동기화되면 클라이언트에서 콜라이더와 애니메이션을 적용한다.
    private void OnDroppedChanged(bool oldValue, bool newValue)
    {
        // 내려짐 상태에 맞는 콜라이더 상태를 적용한다.
        ApplyDroppedState(newValue);

        // false -> true로 바뀌는 순간에만 Drop 애니메이션을 실행한다.
        if (!oldValue && newValue)
            PlayPalletTrigger("Drop");
    }

    // 세워짐 / 내려짐 상태에 맞게 콜라이더를 전환한다.
    private void ApplyDroppedState(bool dropped)
    {
        // 세워진 콜라이더는 내려지지 않았을 때만 켠다.
        if (standingCollider != null)
            standingCollider.enabled = !dropped;

        // 내려진 콜라이더는 내려진 뒤에만 켠다.
        if (droppedCollider != null)
            droppedCollider.enabled = dropped;
    }

    // 판자 Animator Trigger를 실행한다.
    private void PlayPalletTrigger(string triggerName)
    {
        // 애니메이터가 없으면 실행하지 않는다.
        if (animator == null)
        {
            Debug.LogWarning($"[Pallet] Animator가 연결되지 않았습니다: {name}", this);
            return;
        }

        // 같은 Trigger가 남아 있을 수 있으므로 한번 초기화한다.
        animator.ResetTrigger(triggerName);

        // Trigger를 실행한다.
        animator.SetTrigger(triggerName);
    }

    // 서버에서 일회성 3D 사운드를 모든 클라이언트에게 재생한다.
    [Server]
    private void PlayOneShotSound(AudioKey key, Vector3 position)
    {
        if (key == AudioKey.None)
            return;

        NetworkAudioManager.PlayAudioForEveryone(
            key,
            AudioDimension.Sound3D,
            position
        );
    }

    // 부수기처럼 상태 SyncVar가 없는 일회성 연출은 RPC로 실행한다.
    [ClientRpc]
    private void RpcPlayPalletTrigger(string triggerName)
    {
        // 클라이언트에서 판자 Trigger를 실행한다.
        PlayPalletTrigger(triggerName);
    }

    // 액터가 현재 왼쪽/오른쪽 중 어느 쪽에 있는지 구한다.
    private Transform GetSide(Transform actor)
    {
        // 액터가 없으면 null을 반환한다.
        if (actor == null)
            return null;

        // 액터 위치를 판자 기준 로컬 좌표로 변환한다.
        Vector3 localPos = transform.InverseTransformPoint(actor.position);

        // 로컬 x가 0보다 작으면 왼쪽이다.
        if (localPos.x < 0f)
            return leftPoint;
        else
            return rightPoint;
    }

    // 현재 포인트의 반대편 포인트를 구한다.
    private Transform GetOpposite(Transform sidePoint)
    {
        // 현재 왼쪽이면 오른쪽을 반환한다.
        if (sidePoint == leftPoint)
            return rightPoint;

        // 현재 오른쪽이면 왼쪽을 반환한다.
        if (sidePoint == rightPoint)
            return leftPoint;

        // 둘 다 아니면 null을 반환한다.
        return null;
    }

    // 각 포인트에서 판자를 바라볼 방향을 구한다.
    private Vector3 GetLook(Transform sidePoint)
    {
        // 왼쪽에서는 transform.right 방향을 바라본다.
        if (sidePoint == leftPoint)
            return transform.right;

        // 오른쪽에서는 -transform.right 방향을 바라본다.
        if (sidePoint == rightPoint)
            return -transform.right;

        // 포인트가 잘못되면 zero를 반환한다.
        return Vector3.zero;
    }

    // 서버에서 액터를 목표 위치까지 이동시킨다.
    [Server]
    private IEnumerator MoveTo(Transform actor, Vector3 targetPos, float speed)
    {
        // 액터가 없으면 종료한다.
        if (actor == null)
            yield break;

        // 목표 위치에 충분히 가까워질 때까지 이동한다.
        while ((actor.position - targetPos).sqrMagnitude > 0.0001f)
        {
            // 지정된 속도로 목표 위치까지 이동한다.
            actor.position = Vector3.MoveTowards(
                actor.position,
                targetPos,
                speed * Time.deltaTime
            );

            // 다음 프레임까지 대기한다.
            yield return null;
        }

        // 마지막 위치 오차를 제거한다.
        actor.position = targetPos;
    }

    // 사용 위치에 상대 진영이 있는지 검사한다.
    private bool IsOpponentAtPoint(Transform targetPoint, string opponentTag)
    {
        // 검사 위치가 없으면 점유되지 않은 것으로 처리한다.
        if (targetPoint == null)
            return false;

        // 지정 반경 안의 콜라이더를 찾는다.
        Collider[] hits = Physics.OverlapSphere(targetPoint.position, occupationRadius);

        // 감지된 콜라이더를 순회한다.
        for (int i = 0; i < hits.Length; i++)
        {
            // 상대 태그가 있으면 점유된 것으로 처리한다.
            if (hits[i].CompareTag(opponentTag))
                return true;
        }

        // 상대가 없으면 사용 가능하다.
        return false;
    }

    // 서버 기준으로 액터가 판자를 사용할 수 있는 거리인지 검사한다.
    private bool CanUse(Transform actorTransform)
    {
        // 액터가 없으면 사용할 수 없다.
        if (actorTransform == null)
            return false;

        // 판자 루트의 Collider를 찾는다.
        Collider col = GetComponent<Collider>();

        // 루트에 없으면 자식에서 찾는다.
        if (col == null)
            col = GetComponentInChildren<Collider>();

        // Collider가 없으면 거리 판정을 할 수 없다.
        if (col == null)
            return false;

        // 판자 Collider에서 액터와 가장 가까운 지점을 구한다.
        Vector3 closest = col.ClosestPoint(actorTransform.position);

        // 액터와 가장 가까운 지점 사이의 제곱 거리를 구한다.
        float sqrDist = (closest - actorTransform.position).sqrMagnitude;

        // useDistance 이내면 사용할 수 있다.
        return sqrDist <= useDistance * useDistance;
    }

    // 로컬 생존자가 판자 트리거에 들어오면 호출된다.
    private void OnTriggerEnter(Collider other)
    {
        // 생존자만 처리한다.
        if (!other.CompareTag("Survivor"))
            return;

        // 들어온 오브젝트에서 SurvivorInteractor를 찾는다.
        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();

        // 자식 콜라이더일 수 있으므로 부모에서도 찾는다.
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        // 상호작용 컴포넌트가 없으면 처리하지 않는다.
        if (interactor == null)
            return;

        // 로컬 플레이어가 아니면 후보 등록하지 않는다.
        if (!interactor.isLocalPlayer)
            return;

        // 로컬 상호작용 컴포넌트를 저장한다.
        localInteractor = interactor;

        // 로컬 플레이어가 판자 트리거 안에 있다고 저장한다.
        isLocalInside = true;

        // 현재 상태 기준으로 후보 등록을 갱신한다.
        RefreshLocalAvailability();
    }

    // 트리거 안에 계속 머무르는 동안 후보 등록을 보정한다.
    private void OnTriggerStay(Collider other)
    {
        // 생존자만 처리한다.
        if (!other.CompareTag("Survivor"))
            return;

        // 들어와 있는 오브젝트에서 SurvivorInteractor를 찾는다.
        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();

        // 자식 콜라이더일 수 있으므로 부모에서도 찾는다.
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        // 상호작용 컴포넌트가 없으면 처리하지 않는다.
        if (interactor == null)
            return;

        // 로컬 플레이어가 아니면 처리하지 않는다.
        if (!interactor.isLocalPlayer)
            return;

        // 로컬 상호작용 컴포넌트를 다시 저장한다.
        localInteractor = interactor;

        // 트리거 안에 있다고 보정한다.
        isLocalInside = true;

        // 판자 후보 등록을 계속 보정한다.
        RefreshLocalAvailability();
    }

    // 로컬 생존자가 판자 트리거에서 나가면 호출된다.
    private void OnTriggerExit(Collider other)
    {
        // 생존자만 처리한다.
        if (!other.CompareTag("Survivor"))
            return;

        // 나간 오브젝트에서 SurvivorInteractor를 찾는다.
        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();

        // 자식 콜라이더일 수 있으므로 부모에서도 찾는다.
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        // 상호작용 컴포넌트가 없으면 처리하지 않는다.
        if (interactor == null)
            return;

        // 로컬 플레이어가 아니면 처리하지 않는다.
        if (!interactor.isLocalPlayer)
            return;

        // 이 판자를 상호작용 후보에서 제거한다.
        interactor.ClearInteractable(this);

        // 로컬 플레이어가 판자 트리거 밖에 있다고 저장한다.
        isLocalInside = false;

        // 저장된 로컬 플레이어와 나간 플레이어가 같으면 참조를 정리한다.
        if (localInteractor == interactor)
            localInteractor = null;
    }

    private void OnDrawGizmosSelected()
    {
        // 왼쪽 포인트 점유 검사 범위를 표시한다.
        if (leftPoint != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(leftPoint.position, occupationRadius);
        }

        // 오른쪽 포인트 점유 검사 범위를 표시한다.
        if (rightPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(rightPoint.position, occupationRadius);
        }
    }
}