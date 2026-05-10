using Mirror;
using UnityEngine;

public class Prison : NetworkBehaviour, IInteractable
{
    // 감옥 상호작용은 Hold 타입이다.
    public InteractType InteractType => InteractType.Hold;

    [Header("참조")]
    [SerializeField] private Transform prisonerPoint;      // 죄수를 감옥 안에 둘 위치
    [SerializeField] private Transform lookPoint;          // 상호작용할 때 바라볼 위치
    [SerializeField] private Animator animator;            // 감옥 문 애니메이터
    [SerializeField] private Collider doorBlocker;         // 문이 닫혀 있을 때 막는 콜라이더

    [Header("상호작용 시간")]
    [SerializeField] private float interactTime = 3f;      // 탈출 / 구출에 걸리는 시간

    [Header("탈출 설정")]
    [SerializeField] private float escapeChance = 5f;      // 본인 탈출 성공 확률
    [SerializeField] private float failPenalty = 20f;      // 탈출 실패 시 감소할 남은 시간

    // 현재 갇혀 있는 생존자 netId
    [SyncVar]
    private uint prisonerId;

    // 현재 문이 열려 있는지
    [SyncVar(hook = nameof(OnDoorChanged))]
    private bool isDoorOpen;

    // 감옥 시간이 다 되어 사망자가 나온 감옥인지
    // true가 되면 이 감옥은 다시 사용할 수 없다.
    [SyncVar]
    private bool isDisabled;

    // 현재 남은 감옥 시간
    [SyncVar]
    private float remainTime;

    // UI Slider 기준이 되는 최대 시간
    [SyncVar]
    private float prisonMaxTime;

    // 현재 감옥 상호작용을 진행 중인 생존자 netId
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

    // 내 로컬 플레이어가 감옥 트리거 안에 있는지
    private bool isLocalInside;

    public bool IsOccupied => prisonerId != 0;
    public bool IsDisabled => isDisabled;

    public uint PrisonerId => prisonerId;
    public uint CurrentUserId => currentUserId;
    public bool IsInteractingForUI => isInteracting;
    public float RemainTime => remainTime;

    public float RemainTime01
    {
        get
        {
            if (prisonMaxTime <= 0f)
                return 0f;

            return Mathf.Clamp01(remainTime / prisonMaxTime);
        }
    }

    public float Progress01
    {
        get
        {
            if (interactTime <= 0f)
                return 1f;

            return Mathf.Clamp01(progress / interactTime);
        }
    }

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
        // 서버에서만 감옥 시간과 상호작용 진행도를 계산한다.
        if (isServer)
        {
            TickTime();
            TickInteract();
        }

        // 로컬 플레이어 기준 UI / 상호작용 후보 갱신
        UpdateLocalUI();
        RefreshLocalAvailability();
    }

    // 생존자를 감옥에 넣는다.
    [Server]
    public bool SetPrisoner(SurvivorState target)
    {
        if (target == null)
            return false;

        // 시간 초과 사망자가 나온 감옥은 다시 사용할 수 없다.
        if (isDisabled)
            return false;

        if (IsOccupied)
            return false;

        float startTime = target.GetPrisonStartTime();

        // 다음 감옥 단계가 즉사 단계면 감옥에 넣지 않고 바로 사망 처리한다.
        if (startTime <= 0f)
        {
            target.Die();
            return false;
        }

        NetworkIdentity id = target.GetComponent<NetworkIdentity>();
        if (id == null)
            return false;

        // 생존자 상태를 감옥 상태로 변경한다.
        if (!target.EnterPrison(netId))
            return false;

        prisonerId = id.netId;

        // 실제 남은 시간은 현재 감옥 단계에 맞춰 시작한다.
        remainTime = startTime;

        // UI Slider 기준은 항상 전체 감옥 시간으로 둔다.
        // 두 번째 감옥이 60초로 시작하면 Slider가 0.5에서 시작하게 하기 위함이다.
        prisonMaxTime = target.PrisonFullTime;

        // 감옥에 들어갈 때 문은 닫힌 상태가 된다.
        isDoorOpen = false;
        ApplyDoor(false);

        // 이전 상호작용 상태 초기화
        isInteracting = false;
        currentUserId = 0;
        progress = 0f;

        MoveToPrison(target.transform);
        return true;
    }

    // 생존자를 감옥 위치로 이동시킨다.
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

    // 감옥 남은 시간을 감소시킨다.
    [Server]
    private void TickTime()
    {
        if (isDisabled)
            return;

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

        // 첫 번째 감옥에서 시간이 절반 이하가 되면 2단계 판정으로 바꾼다.
        if (state.PrisonStep == 1 && remainTime <= state.PrisonHalfTime)
            state.MarkPrisonHalfPassed();

        // 시간이 다 되면 생존자는 사망하고, 감옥은 닫힌 채 영구 폐쇄된다.
        if (remainTime <= 0f)
        {
            remainTime = 0f;
            state.DieByPrisonTime();
            CloseAndDisableDeadPrison();
        }
    }

    // Hold 상호작용 진행도를 증가시킨다.
    [Server]
    private void TickInteract()
    {
        if (!isInteracting)
            return;

        if (isDisabled)
        {
            StopInteract();
            return;
        }

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

        // 상호작용자가 아직 감옥 범위 안에 있는지 검사한다.
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

        // 폐쇄된 감옥은 로컬 연출도 시작하지 않는다.
        if (isDisabled)
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

    // Hold 중 손을 떼면 취소한다.
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

        // 현재 진행 중인 사용자만 종료 가능하다.
        if (currentUserId != sender.identity.netId)
            return;

        StopInteract();
    }

    // 서버에서 실제 Hold 시작 여부를 판정한다.
    [Server]
    private void TryBegin(SurvivorState actorState)
    {
        if (actorState == null)
            return;

        if (isDisabled)
            return;

        if (!IsOccupied)
            return;

        if (isDoorOpen)
            return;

        // 이미 다른 사람이 진행 중이면 시작 불가
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

        // 죄수 본인 탈출 / 다른 생존자 구출 모두 가능하다.
        isInteracting = true;
        currentUserId = actorState.netId;
        progress = 0f;

        StartPrisonLoopSound();
    }

    // 서버에서 Hold 종료
    [Server]
    private void TryEnd()
    {
        StopInteract();
    }

    // 서버에서 진행 중인 감옥 상호작용을 취소한다.
    [Server]
    private void StopInteract()
    {
        StopPrisonLoopSound();

        isInteracting = false;
        currentUserId = 0;
        progress = 0f;

        RpcStopLocalUI();
    }

    // Hold 완료 시 탈출 또는 구출을 처리한다.
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

        // 다른 생존자면 구출
        DoRescue(prisonerState);
    }

    // 죄수 본인 탈출 처리
    [Server]
    private void DoSelfEscape(SurvivorState prisonerState)
    {
        float roll = Random.Range(0f, 100f);

        if (roll <= escapeChance)
        {
            // 탈출 성공은 문을 열고 감옥을 재사용 가능 상태로 비운다.
            OpenDoor();
            ReleasePrisoner(prisonerState);
            StopInteract();
            return;
        }

        // 실패하면 남은 시간을 감소시킨다.
        remainTime -= failPenalty;

        if (remainTime < 0f)
            remainTime = 0f;

        // 실패 패널티로 시간이 0이 되면 사망 + 감옥 폐쇄
        if (remainTime <= 0f)
        {
            prisonerState.DieByPrisonTime();
            CloseAndDisableDeadPrison();
            return;
        }

        StopInteract();
    }

    // 다른 생존자 구출 처리
    [Server]
    private void DoRescue(SurvivorState prisonerState)
    {
        // 구출은 문을 열고 감옥을 재사용 가능 상태로 비운다.
        OpenDoor();
        ReleasePrisoner(prisonerState);
        StopInteract();
    }

    // 죄수를 감옥에서 해제한다.
    [Server]
    private void ReleasePrisoner(SurvivorState prisonerState)
    {
        if (prisonerState != null && !prisonerState.IsDead)
            prisonerState.LeavePrison(remainTime);

        prisonerId = 0;
        prisonMaxTime = 0f;
    }

    // 구출 / 탈출 성공 시 문을 연다.
    [Server]
    private void OpenDoor()
    {
        isDoorOpen = true;
        ApplyDoor(true);
    }

    // 감옥 시간 초과로 사망했을 때 처리한다.
    // 문은 열지 않고 닫힌 상태로 유지하며, 이 감옥은 영구 폐쇄된다.
    [Server]
    private void CloseAndDisableDeadPrison()
    {
        isDisabled = true;

        // 문은 닫힌 상태로 유지한다.
        isDoorOpen = false;
        ApplyDoor(false);

        // 사망한 생존자는 이미 Dead 상태이므로 감옥 점유 정보만 비운다.
        // PrisonManager는 isDisabled를 검사하기 때문에 이 감옥을 다시 선택하지 않는다.
        prisonerId = 0;
        prisonMaxTime = 0f;
        remainTime = 0f;

        StopInteract();
    }

    // 문 상태가 SyncVar로 바뀌면 클라이언트에서도 실제 문 상태를 반영한다.
    private void OnDoorChanged(bool oldValue, bool newValue)
    {
        ApplyDoor(newValue);
    }

    // 실제 문 상태 적용
    private void ApplyDoor(bool open)
    {
        // Collider는 Animator가 없어도 반드시 처리한다.
        if (open)
        {
            if (doorBlocker != null)
                doorBlocker.enabled = false;
        }
        else
        {
            if (doorBlocker != null)
                doorBlocker.enabled = true;
        }

        if (animator == null)
            return;

        animator.ResetTrigger("Open");
        animator.ResetTrigger("Close");

        if (open)
            animator.SetTrigger("Open");
        else
            animator.SetTrigger("Close");
    }

    // 문 닫힘 애니메이션 이벤트에서 호출해도 되는 함수다.
    // ApplyDoor(false)에서도 이미 켜지만, 이벤트를 유지해도 문제 없다.
    public void EnableDoorBlocker()
    {
        if (doorBlocker != null)
            doorBlocker.enabled = true;
    }

    // 로컬 진행도 UI를 갱신한다.
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

    // 현재 로컬 플레이어가 이 감옥을 상호작용 후보로 유지할지 판단한다.
    private void RefreshLocalAvailability()
    {
        if (!isLocalInside)
            return;

        if (localInteractor == null || localState == null)
            return;

        // 폐쇄된 감옥은 상호작용 후보에서 제거한다.
        if (isDisabled)
        {
            localInteractor.ClearInteractable(this);
            return;
        }

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

    // 감옥 상호작용 가능 거리 검사
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

    // 로컬에서 감옥 쪽을 바라보고 Searching 애니메이션을 시작한다.
    private void StartLocalInteractFx()
    {
        if (localMove == null)
            return;

        localMove.SetMoveLock(true);

        Transform target = lookPoint != null ? lookPoint : transform;

        Vector3 lookDir = target.position - localMove.transform.position;
        lookDir.y = 0f;

        if (lookDir.sqrMagnitude > 0.001f)
            localMove.FaceDirection(lookDir.normalized);

        localMove.SetSearching(true);
    }

    // 로컬 Searching / 이동 잠금을 종료한다.
    private void StopLocalInteractFx()
    {
        if (localMove == null)
            return;

        localMove.SetMoveLock(false);
        localMove.SetSearching(false);
    }

    // 진행도 UI / Searching을 강제로 종료한다.
    [ClientRpc]
    private void RpcStopLocalUI()
    {
        StopLocalInteractFx();

        if (localInteractor != null)
            localInteractor.HideProgress(this, true);
    }

    // 감옥 상호작용 루프 사운드 시작
    [Server]
    private void StartPrisonLoopSound()
    {
        NetworkAudioManager.StartLoopAudioForEveryone(
            netId,
            AudioKey.SurvivorPrisonLoop,
            AudioDimension.Sound3D,
            transform.position
        );
    }

    // 감옥 상호작용 루프 사운드 종료
    [Server]
    private void StopPrisonLoopSound()
    {
        NetworkAudioManager.StopLoopAudioForEveryone(
            netId,
            AudioKey.SurvivorPrisonLoop
        );
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
}