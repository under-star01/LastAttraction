using System.Collections.Generic;
using Mirror;
using UnityEngine;

// 증거 종류다.
// 결과창, 목표 UI, 로그 표시 등에 사용할 수 있다.
public enum EvidenceType
{
    None,
    MissingPoster,       // 실종자 전단
    StaffLogbook,        // 직원 근무일지
    BrokenCamera,        // 부서진 CCTV
    BloodStainedTicket,  // 피 묻은 입장권
    VoiceRecorder        // 낡은 녹음기
}

public class EvidencePoint : NetworkBehaviour, IInteractable
{
    // 증거는 누르고 있는 동안 진행되는 Hold 타입 상호작용이다.
    public InteractType InteractType => InteractType.Hold;

    [Header("증거 정보")]
    [SerializeField] private EvidenceType evidenceType = EvidenceType.None; // 이 프리팹의 증거 종류
    [SerializeField] private string displayName;                            // 결과창 / UI에 표시할 이름
    [SerializeField] private Sprite icon;                                   // 결과창 / UI에 표시할 아이콘

    [Header("조사 설정")]
    [SerializeField] private float interactTime = 10f;                      // 증거 조사 완료까지 걸리는 시간

    [Header("QTE 설정")]
    [SerializeField] private int minQteCount = 2;                           // 조사 중 최소 QTE 횟수
    [SerializeField] private int maxQteCount = 4;                           // 조사 중 최대 QTE 횟수
    [SerializeField] private float qteFailStunTime = 3f;                    // QTE 실패 시 스턴 시간

    // 이 증거가 속한 EvidenceZone이다.
    // 서버에서 완료 보고할 때 사용한다.
    private EvidenceZone zone;

    // 조사 완료 여부를 서버에서 동기화한다.
    [SyncVar(hook = nameof(OnCompletedChanged))]
    private bool isCompleted;

    // 현재 조사 중인지 서버에서 동기화한다.
    [SyncVar]
    private bool isInteracting;

    // 현재 조사 진행도다.
    [SyncVar]
    private float progress;

    // 현재 조사 중인 플레이어의 netId다.
    [SyncVar]
    private uint currentInteractorNetId;

    // QTE 결과를 기다리는 중인지 저장한다.
    [SyncVar]
    private bool isWaitingQTE;

    // 서버에서 생성한 QTE 발생 타이밍 목록이다.
    private readonly List<float> qteTriggerProgressList = new List<float>();

    // 현재 몇 번째 QTE인지 저장한다.
    private int currentQteIndex;

    // 로컬 플레이어의 상호작용 컴포넌트다.
    private SurvivorInteractor localInteractor;

    // 로컬 플레이어의 이동 컴포넌트다.
    private SurvivorMove localMove;

    // 로컬 플레이어의 QTE UI다.
    private QTEUI localQTEUI;

    // 내 로컬 플레이어가 이 증거 범위 안에 있는지 여부다.
    private bool isLocalInside;

    public EvidenceType EvidenceType => evidenceType;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;

            return evidenceType.ToString();
        }
    }

    public Sprite Icon => icon;

    public bool IsInteractingForUI => isInteracting;
    public uint CurrentInteractorNetId => currentInteractorNetId;

    public float Progress01
    {
        get
        {
            if (interactTime <= 0f)
                return 1f;

            return Mathf.Clamp01(progress / interactTime);
        }
    }

    // EvidenceZone이 서버에서 증거를 생성한 직후 호출한다.
    // 증거 타입, 이름, 아이콘은 이 EvidencePoint 프리팹에 이미 들어있다.
    [Server]
    public void ServerInit(EvidenceZone ownerZone)
    {
        zone = ownerZone;
    }

    // 예전 코드 호환용이다.
    // 기존 EvidenceZone 구조에서 호출하던 SetZone을 유지한다.
    public void SetZone(EvidenceZone evidenceZone)
    {
        zone = evidenceZone;
    }

    // 예전 구조에서는 진짜/가짜 증거 구분에 사용했다.
    // 새 구조에서는 생성된 EvidencePoint가 무조건 진짜라서 사용하지 않는다.
    [Server]
    public void SetIsRealEvidenceServer(bool value)
    {
        // 사용하지 않음.
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 이미 완료된 증거라면 늦게 들어온 클라이언트에서도 숨김 처리한다.
        if (isCompleted)
            HideEvidenceLocal();
    }

    private void Update()
    {
        // 서버에서만 실제 조사 진행도를 증가시킨다.
        if (isServer)
            ServerUpdateInteract();

        // 로컬 플레이어에게 ProgressUI를 갱신한다.
        UpdateLocalUI();

        // 로컬 플레이어 기준으로 상호작용 후보 등록/해제를 갱신한다.
        RefreshLocalAvailability();
    }

    // 로컬 플레이어가 조사 시작 입력을 했을 때 호출된다.
    public void BeginInteract(GameObject actor)
    {
        if (isCompleted)
            return;

        if (localInteractor == null)
            return;

        if (IsBusyByOtherLocal())
            return;

        // 조사 시작 시 증거 방향을 바라보게 한다.
        FaceToEvidenceLocal();

        // 조사 중 이동을 잠근다.
        LockMovementLocal(true);

        // 조사 애니메이션을 켠다.
        SetSearchingLocal(true);

        // 시작하자마자 0% ProgressUI를 보여준다.
        localInteractor.ShowProgress(this, 0f);

        // 서버에 조사 시작을 요청한다.
        CmdBeginInteract();
    }

    // 로컬 플레이어가 조사 입력을 놓았을 때 호출된다.
    public void EndInteract()
    {
        if (localInteractor == null)
            return;

        // 이동 잠금 해제
        LockMovementLocal(false);

        // 조사 애니메이션 해제
        SetSearchingLocal(false);

        // QTE가 떠 있었다면 닫는다.
        if (localQTEUI != null)
            localQTEUI.ForceClose(false);

        // ProgressUI 숨김
        localInteractor.HideProgress(this, true);

        // 서버에 조사 종료를 요청한다.
        CmdEndInteract();
    }

    // 클라이언트가 서버에 조사 시작을 요청한다.
    [Command(requiresAuthority = false)]
    private void CmdBeginInteract(NetworkConnectionToClient sender = null)
    {
        if (isCompleted)
            return;

        if (sender == null || sender.identity == null)
            return;

        SurvivorInteractor interactor = sender.identity.GetComponent<SurvivorInteractor>();
        if (interactor == null)
            return;

        SurvivorMove move = sender.identity.GetComponent<SurvivorMove>();
        if (move == null)
            return;

        // 이미 다른 사람이 조사 중이면 시작하지 않는다.
        if (isInteracting && currentInteractorNetId != sender.identity.netId)
            return;

        // 서버 기준으로 거리가 멀면 시작하지 않는다.
        if (!CanInteractorUseThis(sender.identity.transform))
            return;

        isInteracting = true;
        currentInteractorNetId = sender.identity.netId;
        progress = 0f;
        isWaitingQTE = false;

        SetupQTEPointsServer();

        StartLoopSound();
    }

    // 클라이언트가 서버에 조사 종료를 요청한다.
    [Command(requiresAuthority = false)]
    private void CmdEndInteract(NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null)
            return;

        if (!isInteracting)
            return;

        if (currentInteractorNetId != sender.identity.netId)
            return;

        StopServerInteract();
    }

    // 클라이언트가 서버에 QTE 결과를 전달한다.
    [Command(requiresAuthority = false)]
    private void CmdSubmitQTEResult(bool success, NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null)
            return;

        if (!isInteracting)
            return;

        if (!isWaitingQTE)
            return;

        if (currentInteractorNetId != sender.identity.netId)
            return;

        if (!success)
        {
            FailQTEServer(sender.identity);
            return;
        }

        isWaitingQTE = false;
        currentQteIndex++;
    }

    // 서버에서 QTE 실패를 처리한다.
    [Server]
    private void FailQTEServer(NetworkIdentity identity)
    {
        if (identity == null)
        {
            StopServerInteract();
            return;
        }

        SurvivorState survivorState = identity.GetComponent<SurvivorState>();

        if (survivorState == null)
            survivorState = identity.GetComponentInParent<SurvivorState>();

        // 먼저 조사 상태를 중단한다.
        StopServerInteract();

        // 생존자에게 스턴을 적용한다.
        if (survivorState != null)
            survivorState.ApplyStun(qteFailStunTime);
    }

    // 서버에서 조사 진행도와 QTE 타이밍을 처리한다.
    [Server]
    private void ServerUpdateInteract()
    {
        if (!isInteracting || isCompleted)
            return;

        if (!NetworkServer.spawned.TryGetValue(currentInteractorNetId, out NetworkIdentity identity))
        {
            StopServerInteract();
            return;
        }

        SurvivorInteractor interactor = identity.GetComponent<SurvivorInteractor>();

        if (interactor == null)
        {
            StopServerInteract();
            return;
        }

        if (!CanInteractorUseThis(identity.transform))
        {
            StopServerInteract();
            return;
        }

        // QTE 결과를 기다리는 동안에는 진행도를 멈춘다.
        if (isWaitingQTE)
            return;

        progress += Time.deltaTime;

        if (currentQteIndex < qteTriggerProgressList.Count)
        {
            float triggerTime = qteTriggerProgressList[currentQteIndex] * interactTime;

            if (progress >= triggerTime)
            {
                isWaitingQTE = true;
                TargetStartQTE(identity.connectionToClient);
                return;
            }
        }

        if (progress >= interactTime)
            CompleteServer();
    }

    // 서버에서 조사 중단 상태를 정리한다.
    [Server]
    private void StopServerInteract()
    {
        StopLoopSound();

        isInteracting = false;
        currentInteractorNetId = 0;
        progress = 0f;
        isWaitingQTE = false;

        qteTriggerProgressList.Clear();
        currentQteIndex = 0;

        RpcForceStopLocalEffects(true);
    }

    // 서버에서 조사 완료를 처리한다.
    [Server]
    private void CompleteServer()
    {
        StopLoopSound();

        // 현재 조사한 플레이어를 결과 기록용으로 저장한다.
        uint finderNetId = currentInteractorNetId;

        isCompleted = true;
        isInteracting = false;
        currentInteractorNetId = 0;
        progress = interactTime;
        isWaitingQTE = false;

        qteTriggerProgressList.Clear();
        currentQteIndex = 0;

        // 새 구조에서는 생성된 EvidencePoint가 무조건 진짜 증거다.
        if (zone != null)
        {
            Debug.Log(
                $"[EvidencePoint] {name} : {DisplayName} 발견 완료 / " +
                $"타입: {evidenceType} / 발견자 NetId: {finderNetId}"
            );

            zone.OnRealEvidenceFound(this, finderNetId);
        }
        else
        {
            Debug.LogWarning(
                $"[EvidencePoint] {name} : EvidenceZone 참조가 없어 목표 카운트가 증가하지 않습니다.",
                this
            );
        }

        // 서버에서도 콜라이더와 렌더러를 꺼서 완전히 사라지게 한다.
        HideEvidenceLocal();

        // 모든 클라이언트에서도 숨김 처리한다.
        RpcCompleteEvidence();
    }

    // 서버에서 QTE 발생 타이밍을 랜덤으로 생성한다.
    [Server]
    private void SetupQTEPointsServer()
    {
        qteTriggerProgressList.Clear();
        currentQteIndex = 0;

        int count = Random.Range(minQteCount, maxQteCount + 1);

        float startNormalized = 0.15f;
        float endNormalized = 0.85f;
        float totalRange = endNormalized - startNormalized;
        float sectionSize = totalRange / count;

        for (int i = 0; i < count; i++)
        {
            float sectionStart = startNormalized + sectionSize * i;
            float sectionEnd = sectionStart + sectionSize * 0.8f;
            float point = Random.Range(sectionStart, sectionEnd);

            qteTriggerProgressList.Add(point);
        }
    }

    // 조사 중인 클라이언트에게만 QTE를 시작시킨다.
    [TargetRpc]
    private void TargetStartQTE(NetworkConnection target)
    {
        if (localQTEUI == null && localInteractor != null)
            localQTEUI = localInteractor.QTEUI;

        if (localQTEUI == null)
        {
            CmdSubmitQTEResult(false);
            return;
        }

        localQTEUI.StartQTE(OnLocalQTEFinished);
    }

    // 로컬 QTE가 끝났을 때 호출된다.
    private void OnLocalQTEFinished(bool success)
    {
        CmdSubmitQTEResult(success);
    }

    // 조사 중단 시 모든 클라이언트에서 로컬 연출을 정리한다.
    [ClientRpc]
    private void RpcForceStopLocalEffects(bool resetProgress)
    {
        StopLocalEffects(resetProgress);
    }

    // 조사 완료 시 모든 클라이언트에서 증거를 숨긴다.
    [ClientRpc]
    private void RpcCompleteEvidence()
    {
        StopLocalEffects(true);
        HideEvidenceLocal();
    }

    // 완료 상태가 늦게 들어온 클라이언트에도 반영되게 한다.
    private void OnCompletedChanged(bool oldValue, bool newValue)
    {
        if (newValue)
            HideEvidenceLocal();
    }

    // 로컬 플레이어의 이동 잠금, 애니메이션, UI를 정리한다.
    private void StopLocalEffects(bool resetProgress)
    {
        if (localMove != null)
        {
            localMove.SetMoveLock(false);
            localMove.SetSearching(false);
            localMove.SetCamAnim(false);
        }

        if (localQTEUI != null)
            localQTEUI.ForceClose(false);

        if (localInteractor != null)
        {
            localInteractor.HideProgress(this, resetProgress);
            localInteractor.ClearInteractable(this);
        }
    }

    // 증거 오브젝트를 보이지 않고 충돌하지 않게 만든다.
    private void HideEvidenceLocal()
    {
        if (localInteractor != null)
        {
            localInteractor.HideProgress(this, true);
            localInteractor.ClearInteractable(this);
        }

        if (localQTEUI != null)
            localQTEUI.ForceClose(false);

        Collider[] cols = GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < cols.Length; i++)
            cols[i].enabled = false;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = false;

        isLocalInside = false;
    }

    // 로컬 플레이어가 조사 중일 때만 ProgressUI를 표시한다.
    private void UpdateLocalUI()
    {
        if (localInteractor == null)
            return;

        bool isMyInteract = isInteracting &&
                            !isCompleted &&
                            localInteractor.netId == currentInteractorNetId;

        if (isMyInteract)
        {
            float value = interactTime <= 0f ? 1f : progress / interactTime;
            localInteractor.ShowProgress(this, value);
        }
        else
        {
            localInteractor.HideProgress(this, false);
        }
    }

    // 현재 로컬 플레이어가 이 증거를 상호작용 후보로 유지할지 판단한다.
    private void RefreshLocalAvailability()
    {
        if (!isLocalInside)
            return;

        if (localInteractor == null)
            return;

        if (isCompleted)
        {
            localInteractor.ClearInteractable(this);
            return;
        }

        if (IsBusyByOtherLocal())
        {
            localInteractor.ClearInteractable(this);
            return;
        }

        localInteractor.SetInteractable(this);
    }

    // 다른 플레이어가 조사 중인지 로컬 기준으로 판단한다.
    private bool IsBusyByOtherLocal()
    {
        if (!isInteracting)
            return false;

        if (localInteractor == null)
            return true;

        return currentInteractorNetId != localInteractor.netId;
    }

    // 서버 기준으로 조사 가능한 거리인지 검사한다.
    private bool CanInteractorUseThis(Transform interactorTransform)
    {
        if (interactorTransform == null)
            return false;

        Collider myCol = GetComponent<Collider>();

        if (myCol == null)
            myCol = GetComponentInChildren<Collider>();

        if (myCol == null)
            return false;

        Vector3 closest = myCol.ClosestPoint(interactorTransform.position);
        float sqrDist = (closest - interactorTransform.position).sqrMagnitude;

        // 4m 이내면 조사 가능
        return sqrDist <= 4f;
    }

    // 로컬 플레이어가 증거 쪽을 바라보게 한다.
    private void FaceToEvidenceLocal()
    {
        if (localMove == null)
            return;

        Vector3 lookDir = transform.position - localMove.transform.position;
        lookDir.y = 0f;

        if (lookDir.sqrMagnitude <= 0.001f)
            return;

        localMove.FaceDirection(lookDir.normalized);
    }

    // 로컬 플레이어 이동 잠금을 설정한다.
    private void LockMovementLocal(bool value)
    {
        if (localMove != null)
            localMove.SetMoveLock(value);
    }

    // 로컬 플레이어 조사 애니메이션을 설정한다.
    private void SetSearchingLocal(bool value)
    {
        if (localMove != null)
            localMove.SetSearching(value);
    }

    // 증거 조사 루프 사운드 시작
    [Server]
    private void StartLoopSound()
    {
        NetworkAudioManager.StartLoopAudioForEveryone(
            netId,
            AudioKey.SurvivorEvidenceLoop,
            AudioDimension.Sound3D,
            transform.position
        );
    }

    // 증거 조사 루프 사운드 종료
    [Server]
    private void StopLoopSound()
    {
        NetworkAudioManager.StopLoopAudioForEveryone(
            netId,
            AudioKey.SurvivorEvidenceLoop
        );
    }

    // 로컬 생존자가 증거 범위 안에 들어오면 호출된다.
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

        if (isCompleted)
            return;

        localInteractor = interactor;

        localMove = interactor.GetComponent<SurvivorMove>();

        if (localMove == null)
            localMove = interactor.GetComponentInParent<SurvivorMove>();

        localQTEUI = interactor.QTEUI;

        isLocalInside = true;

        RefreshLocalAvailability();
    }

    // 로컬 생존자가 증거 범위 밖으로 나가면 호출된다.
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

        isLocalInside = false;

        if (localInteractor != interactor)
            return;

        LockMovementLocal(false);
        SetSearchingLocal(false);

        localInteractor.HideProgress(this, true);

        if (localQTEUI != null)
            localQTEUI.ForceClose(false);

        CmdEndInteract();

        localInteractor = null;
        localMove = null;
        localQTEUI = null;
    }
}