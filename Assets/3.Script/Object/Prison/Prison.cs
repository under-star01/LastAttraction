using Mirror;
using UnityEngine;

public class Prison : NetworkBehaviour, IInteractable
{
    // 감옥 상호작용은 Hold 타입
    public InteractType InteractType => InteractType.Hold;

    [Header("참조")]
    [SerializeField] private Transform prisonerPoint;      // 죄수를 감옥 안에 둘 위치
    [SerializeField] private Transform lookPoint;          // 상호작용할 때 바라볼 위치(문 쪽)
    [SerializeField] private Animator animator;            // 감옥 문 애니메이터
    [SerializeField] private Collider doorBlocker;         // 문이 닫혀 있을 때 막는 콜라이더

    [Header("상호작용 시간")]
    [SerializeField] private float interactTime = 3f;      // 탈출/구출에 걸리는 시간

    [Header("탈출 설정")]
    [SerializeField] private float escapeChance = 5f;      // 본인 탈출 성공 확률 5%
    [SerializeField] private float failPenalty = 20f;      // 탈출 실패 시 남은 시간 20초 감소

    // 현재 갇혀 있는 생존자 netId
    [SyncVar]
    private uint prisonerId;

    // 현재 문이 열려 있는지
    [SyncVar(hook = nameof(OnDoorChanged))]
    private bool isDoorOpen;

    // 현재 남은 시간
    [SyncVar]
    private float remainTime;

    // 현재 누가 감옥 상호작용을 진행 중인지
    [SyncVar]
    private uint currentUserId;

    // 현재 감옥 상호작용 진행 중인지
    [SyncVar]
    private bool isInteracting;

    // 현재 감옥 상호작용 진행도
    [SyncVar]
    private float progress;

    // 로컬 UI / 애니메이션용 참조
    private SurvivorInteractor localInteractor;
    private SurvivorMove localMove;
    private SurvivorState localState;

    // 내 로컬 플레이어가 현재 트리거 안에 있는지
    private bool isLocalInside;

    public bool IsOccupied => prisonerId != 0;

    private void Awake()
    {
        ApplyDoor(isDoorOpen);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyDoor(isDoorOpen);
    }

    private void Update()
    {
        // 서버에서만 감옥 타이머 감소 / Hold 진행
        if (isServer)
        {
            TickTime();
            TickInteract();
        }

        // 로컬 UI / 후보 갱신
        UpdateLocalUI();
        RefreshLocalAvailability();
    }

    // 생존자를 감옥에 넣기
    [Server]
    public bool SetPrisoner(SurvivorState target)
    {
        if (target == null)
            return false;

        if (IsOccupied)
            return false;

        float startTime = target.GetPrisonStartTime();

        // 다음 감옥이 즉사 단계면 감옥에 넣지 않고 바로 사망
        if (startTime <= 0f)
        {
            target.Die();
            return false;
        }

        NetworkIdentity id = target.GetComponent<NetworkIdentity>();
        if (id == null)
            return false;

        // 상태를 감옥 상태로 변경
        if (!target.EnterPrison(netId))
            return false;

        prisonerId = id.netId;
        remainTime = startTime;

        // 새로 갇힐 때 문 닫기
        isDoorOpen = false;
        ApplyDoor(false);

        // 이전 상호작용 상태 초기화
        isInteracting = false;
        currentUserId = 0;
        progress = 0f;

        MoveToPrison(target.transform);
        return true;
    }

    // 실제 순간이동
    [Server]
    private void MoveToPrison(Transform target)
    {
        if (target == null || prisonerPoint == null)
            return;

        CharacterController controller = target.GetComponent<CharacterController>();

        if (controller != null)
            controller.enabled = false;

        target.position = prisonerPoint.position;

        if (controller != null)
            controller.enabled = true;
    }

    // 감옥 시간 감소
    [Server]
    private void TickTime()
    {
        if (!IsOccupied)
            return;

        if (isDoorOpen)
            return;

        if (!NetworkServer.spawned.TryGetValue(prisonerId, out NetworkIdentity id))
            return;

        SurvivorState state = id.GetComponent<SurvivorState>();
        if (state == null)
            return;

        if (state.IsDead)
            return;

        remainTime -= Time.deltaTime;

        // 시간이 다 되면 사망
        if (remainTime <= 0f)
        {
            remainTime = 0f;
            state.Die();
            OpenAndClearDead();
        }
    }

    // Hold 진행도 증가
    [Server]
    private void TickInteract()
    {
        if (!isInteracting)
            return;

        if (!IsOccupied)
        {
            StopInteract();
            return;
        }

        if (isDoorOpen)
        {
            StopInteract();
            return;
        }

        if (!NetworkServer.spawned.TryGetValue(currentUserId, out NetworkIdentity userId))
        {
            StopInteract();
            return;
        }

        SurvivorState userState = userId.GetComponent<SurvivorState>();
        if (userState == null || userState.IsDead)
        {
            StopInteract();
            return;
        }

        // 현재 상호작용 중인 플레이어가 아직 범위 안에서 사용할 수 있는지 검사
        if (!CanUse(userState.transform))
        {
            StopInteract();
            return;
        }

        progress += Time.deltaTime;

        if (progress >= interactTime)
        {
            progress = interactTime;
            CompleteInteract(userState);
        }
    }

    // Hold 시작
    public void BeginInteract(GameObject actor)
    {
        if (actor == null)
            return;

        SurvivorState actorState = actor.GetComponent<SurvivorState>();
        if (actorState == null)
            actorState = actor.GetComponentInParent<SurvivorState>();

        if (actorState == null)
            return;

        // 로컬에서 감옥 쪽 바라보기 + Searching 시작
        StartLocalInteractFx();

        if (isServer)
        {
            TryBegin(actorState);
        }
        else
        {
            NetworkIdentity actorId = actor.GetComponent<NetworkIdentity>();
            if (actorId == null)
                actorId = actor.GetComponentInParent<NetworkIdentity>();

            if (actorId == null)
                return;

            CmdBegin(actorId.netId);
        }
    }

    // Hold 중 손 떼면 취소
    public void EndInteract()
    {
        StopLocalInteractFx();

        if (isServer)
        {
            TryEnd();
        }
        else
        {
            CmdEnd();
        }
    }

    [Command(requiresAuthority = false)]
    private void CmdBegin(uint actorNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(actorNetId, out NetworkIdentity actorId))
            return;

        SurvivorState actorState = actorId.GetComponent<SurvivorState>();
        if (actorState == null)
            return;

        TryBegin(actorState);
    }

    [Command(requiresAuthority = false)]
    private void CmdEnd(NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null)
            return;

        if (!isInteracting)
            return;

        // 현재 진행 중인 사용자만 종료 가능
        if (currentUserId != sender.identity.netId)
            return;

        StopInteract();
    }

    // 실제 Hold 시작 판정
    [Server]
    private void TryBegin(SurvivorState actorState)
    {
        if (actorState == null)
            return;

        if (!IsOccupied)
            return;

        if (isDoorOpen)
            return;

        // 이미 다른 사람이 하고 있으면 시작 불가
        if (isInteracting && currentUserId != actorState.netId)
            return;

        // 범위 밖이면 불가
        if (!CanUse(actorState.transform))
            return;

        if (!NetworkServer.spawned.TryGetValue(prisonerId, out NetworkIdentity prisonerIdentity))
            return;

        SurvivorState prisonerState = prisonerIdentity.GetComponent<SurvivorState>();
        if (prisonerState == null || prisonerState.IsDead)
            return;

        // 죄수 본인도 가능, 다른 생존자도 가능
        isInteracting = true;
        currentUserId = actorState.netId;
        progress = 0f;
    }

    // 실제 Hold 종료
    [Server]
    private void TryEnd()
    {
        StopInteract();
    }

    // 서버에서 진행 취소
    [Server]
    private void StopInteract()
    {
        isInteracting = false;
        currentUserId = 0;
        progress = 0f;

        RpcStopLocalUI();
    }

    // Hold 완료 시 처리
    [Server]
    private void CompleteInteract(SurvivorState userState)
    {
        if (userState == null)
        {
            StopInteract();
            return;
        }

        if (!NetworkServer.spawned.TryGetValue(prisonerId, out NetworkIdentity prisonerIdentity))
        {
            StopInteract();
            return;
        }

        SurvivorState prisonerState = prisonerIdentity.GetComponent<SurvivorState>();
        if (prisonerState == null || prisonerState.IsDead)
        {
            StopInteract();
            return;
        }

        // 죄수 본인이면 탈출 시도
        if (userState.netId == prisonerId)
        {
            DoSelfEscape(prisonerState);
            return;
        }

        // 다른 생존자면 즉시 구출
        DoRescue(prisonerState);
    }

    // 죄수 본인 탈출 처리
    [Server]
    private void DoSelfEscape(SurvivorState prisonerState)
    {
        float roll = Random.Range(0f, 100f);

        if (roll <= escapeChance)
        {
            OpenDoor();
            ReleasePrisoner(prisonerState);
            StopInteract();
            return;
        }

        // 실패하면 시간 20초 감소
        remainTime -= failPenalty;

        if (remainTime < 0f)
            remainTime = 0f;

        // 실패 후 시간 다 되면 즉시 사망
        if (remainTime <= 0f)
        {
            prisonerState.Die();
            OpenAndClearDead();
        }

        StopInteract();
    }

    // 다른 생존자 구출 처리
    [Server]
    private void DoRescue(SurvivorState prisonerState)
    {
        OpenDoor();
        ReleasePrisoner(prisonerState);
        StopInteract();
    }

    // 죄수 해제
    [Server]
    private void ReleasePrisoner(SurvivorState prisonerState)
    {
        if (prisonerState != null && !prisonerState.IsDead)
            prisonerState.LeavePrison(remainTime);

        prisonerId = 0;
    }

    // 문 열기
    [Server]
    private void OpenDoor()
    {
        isDoorOpen = true;
        ApplyDoor(true);
    }

    // 사망했을 때 문 열고 비우기
    [Server]
    private void OpenAndClearDead()
    {
        isDoorOpen = true;
        ApplyDoor(true);

        prisonerId = 0;
        StopInteract();
    }

    // 문 상태 동기화
    private void OnDoorChanged(bool oldValue, bool newValue)
    {
        ApplyDoor(newValue);
    }

    // 실제 문 상태 적용
    private void ApplyDoor(bool open)
    {
        if (animator == null)
            return;

        animator.ResetTrigger("Open");
        animator.ResetTrigger("Close");

        if (open)
        {
            if (doorBlocker != null)
                doorBlocker.enabled = false;

            animator.SetTrigger("Open");
        }
        else
        {
            animator.SetTrigger("Close");
        }
    }

    public void EnableDoorBlocker()
    {
        if (doorBlocker != null)
            doorBlocker.enabled = true;
    }

    // 로컬 UI 갱신
    private void UpdateLocalUI()
    {
        if (localInteractor == null)
            return;

        bool isMyInteract = false;

        if (isInteracting && currentUserId == localInteractor.netId)
            isMyInteract = true;

        if (isMyInteract)
            localInteractor.ShowProgress(this, progress / interactTime);
        else
            localInteractor.HideProgress(this, false);
    }

    // 현재 로컬 플레이어가 이 감옥을 상호작용 후보로 유지할지 판단
    private void RefreshLocalAvailability()
    {
        if (!isLocalInside)
            return;

        if (localInteractor == null || localState == null)
            return;

        if (localState.IsDead)
        {
            localInteractor.ClearInteractable(this);
            return;
        }

        if (!IsOccupied || isDoorOpen)
        {
            localInteractor.ClearInteractable(this);
            return;
        }

        // 다른 사람이 상호작용 중이면 현재 로컬 플레이어는 사용 불가
        if (isInteracting && currentUserId != localInteractor.netId)
        {
            localInteractor.ClearInteractable(this);
            return;
        }

        // 죄수 본인 또는 다른 생존자 모두 가능
        localInteractor.SetInteractable(this);
    }

    // 감옥 상호작용 범위 검사
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

    // 로컬에서 감옥 쪽 바라보고 Searching 시작
    private void StartLocalInteractFx()
    {
        if (localMove == null)
            return;

        // 상호작용 중에는 이동 불가
        localMove.SetMoveLock(true);

        // 문 쪽 바라보기
        Transform target = lookPoint != null ? lookPoint : transform;

        Vector3 lookDir = target.position - localMove.transform.position;
        lookDir.y = 0f;

        if (lookDir.sqrMagnitude > 0.001f)
            localMove.FaceDirection(lookDir.normalized);

        // Searching 애니메이션 시작
        localMove.SetSearching(true);
    }

    // 로컬 Searching 종료
    private void StopLocalInteractFx()
    {
        if (localMove == null)
            return;

        // 상호작용 종료 시 다시 이동 가능
        localMove.SetMoveLock(false);
        localMove.SetSearching(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Survivor"))
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        SurvivorMove move = other.GetComponent<SurvivorMove>();
        if (move == null)
            move = other.GetComponentInParent<SurvivorMove>();

        SurvivorState state = other.GetComponent<SurvivorState>();
        if (state == null)
            state = other.GetComponentInParent<SurvivorState>();

        if (interactor == null || state == null)
            return;

        if (!interactor.isLocalPlayer)
            return;

        localInteractor = interactor;
        localMove = move;
        localState = state;
        isLocalInside = true;

        RefreshLocalAvailability();
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
        interactor.HideProgress(this, true);

        isLocalInside = false;

        if (localInteractor == interactor)
        {
            StopLocalInteractFx();
            CmdEnd();

            localInteractor = null;
            localMove = null;
            localState = null;
        }
    }

    // 진행도 UI / Searching 강제 종료
    [ClientRpc]
    private void RpcStopLocalUI()
    {
        StopLocalInteractFx();

        if (localInteractor != null)
            localInteractor.HideProgress(this, true);
    }
}