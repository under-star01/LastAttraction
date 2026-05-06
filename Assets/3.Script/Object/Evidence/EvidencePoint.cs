using System.Collections.Generic;          // List 사용
using Mirror;                              // NetworkBehaviour, SyncVar, Command, ClientRpc 사용
using UnityEngine;                         // Unity 기본 기능 사용

public class EvidencePoint : NetworkBehaviour, IInteractable
{
    // 증거는 누르고 있는 동안 진행되는 Hold 타입 상호작용이다.
    public InteractType InteractType => InteractType.Hold;

    [Header("조사 설정")]
    [SerializeField] private float interactTime = 10f;      // 증거 조사 완료까지 걸리는 시간

    [Header("QTE 설정")]
    [SerializeField] private int minQteCount = 2;            // 조사 중 최소 QTE 횟수
    [SerializeField] private int maxQteCount = 4;            // 조사 중 최대 QTE 횟수
    [SerializeField] private float qteFailStunTime = 3f;     // QTE 실패 시 스턴 시간

    // 이 증거가 속한 EvidenceZone이다.
    private EvidenceZone zone;

    // 이 포인트가 진짜 증거인지 서버에서 동기화한다.
    [SyncVar]
    private bool isRealEvidence;

    // 조사 완료 여부를 서버에서 동기화한다.
    // 완료 상태가 늦게 들어온 클라이언트에도 반영되도록 hook을 사용한다.
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

    // EvidenceZone에서 이 포인트의 소속 구역을 지정할 때 호출한다.
    public void SetZone(EvidenceZone evidenceZone)
    {
        // 어떤 EvidenceZone에 포함된 증거인지 저장한다.
        zone = evidenceZone;
    }

    // 서버에서만 진짜 증거 여부를 설정한다.
    [Server]
    public void SetIsRealEvidenceServer(bool value)
    {
        // 랜덤으로 선택된 진짜 증거 여부를 SyncVar에 저장한다.
        isRealEvidence = value;
    }

    public override void OnStartClient()
    {
        // Mirror 기본 클라이언트 시작 처리를 실행한다.
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
        // 이미 완료된 증거면 시작하지 않는다.
        if (isCompleted)
            return;

        // 로컬 플레이어 정보가 없으면 시작하지 않는다.
        if (localInteractor == null)
            return;

        // 다른 플레이어가 조사 중이면 시작하지 않는다.
        if (IsBusyByOtherLocal())
            return;

        // 조사 시작 시 증거 방향을 바라보게 한다.
        FaceToEvidenceLocal();

        // 조사 중 이동을 잠근다.
        LockMovementLocal(true);

        // 조사 애니메이션을 켠다.
        SetSearchingLocal(true);

        // 시작하자마자 0% ProgressUI를 보여줘서 UI 반응이 늦어 보이지 않게 한다.
        localInteractor.ShowProgress(this, 0f);

        // 서버에 조사 시작을 요청한다.
        CmdBeginInteract();
    }

    // 로컬 플레이어가 조사 입력을 놓았을 때 호출된다.
    public void EndInteract()
    {
        // 로컬 플레이어 정보가 없으면 처리하지 않는다.
        if (localInteractor == null)
            return;

        // 이동 잠금을 해제한다.
        LockMovementLocal(false);

        // 조사 애니메이션을 끈다.
        SetSearchingLocal(false);

        // QTE가 떠 있었다면 닫는다.
        if (localQTEUI != null)
            localQTEUI.ForceClose(false);

        // ProgressUI는 직접 건드리지 않고 SurvivorInteractor의 owner 구조로 숨긴다.
        localInteractor.HideProgress(this, true);

        // 서버에 조사 종료를 요청한다.
        CmdEndInteract();
    }

    // 클라이언트가 서버에 조사 시작을 요청한다.
    [Command(requiresAuthority = false)]
    private void CmdBeginInteract(NetworkConnectionToClient sender = null)
    {
        // 이미 완료된 증거면 시작하지 않는다.
        if (isCompleted)
            return;

        // 요청을 보낸 클라이언트 정보가 없으면 무시한다.
        if (sender == null || sender.identity == null)
            return;

        // 요청한 플레이어의 SurvivorInteractor를 가져온다.
        SurvivorInteractor interactor = sender.identity.GetComponent<SurvivorInteractor>();

        // 상호작용 컴포넌트가 없으면 무시한다.
        if (interactor == null)
            return;

        // 요청한 플레이어의 이동 컴포넌트를 가져온다.
        SurvivorMove move = sender.identity.GetComponent<SurvivorMove>();

        // 이동 컴포넌트가 없으면 무시한다.
        if (move == null)
            return;

        // 이미 다른 사람이 조사 중이면 시작하지 않는다.
        if (isInteracting && currentInteractorNetId != sender.identity.netId)
            return;

        // 서버 기준으로 거리가 멀면 시작하지 않는다.
        if (!CanInteractorUseThis(sender.identity.transform))
            return;

        // 조사 중 상태로 변경한다.
        isInteracting = true;

        // 현재 조사 중인 플레이어를 저장한다.
        currentInteractorNetId = sender.identity.netId;

        // 조사를 새로 시작할 때 진행도를 0으로 초기화한다.
        progress = 0f;

        // QTE 대기 상태를 초기화한다.
        isWaitingQTE = false;

        // 이번 조사에서 발생할 QTE 타이밍을 새로 생성한다.
        SetupQTEPointsServer();

        // 증거 조사 루프 사운드 시작
        StartLoopSound();
    }

    // 클라이언트가 서버에 조사 종료를 요청한다.
    [Command(requiresAuthority = false)]
    private void CmdEndInteract(NetworkConnectionToClient sender = null)
    {
        // 요청한 클라이언트 정보가 없으면 무시한다.
        if (sender == null || sender.identity == null)
            return;

        // 조사 중이 아니면 무시한다.
        if (!isInteracting)
            return;

        // 현재 조사 중인 플레이어가 아니면 종료할 수 없다.
        if (currentInteractorNetId != sender.identity.netId)
            return;

        // 서버 조사 상태를 정리한다.
        StopServerInteract();
    }

    // 클라이언트가 서버에 QTE 결과를 전달한다.
    [Command(requiresAuthority = false)]
    private void CmdSubmitQTEResult(bool success, NetworkConnectionToClient sender = null)
    {
        // 요청한 클라이언트 정보가 없으면 무시한다.
        if (sender == null || sender.identity == null)
            return;

        // 조사 중이 아니면 무시한다.
        if (!isInteracting)
            return;

        // QTE 대기 중이 아니면 무시한다.
        if (!isWaitingQTE)
            return;

        // 현재 조사 중인 플레이어가 아니면 QTE 결과를 받을 수 없다.
        if (currentInteractorNetId != sender.identity.netId)
            return;

        // QTE 실패면 조사 중단 후 스턴을 적용한다.
        if (!success)
        {
            FailQTEServer(sender.identity);
            return;
        }

        // 성공했으면 QTE 대기 상태를 해제한다.
        isWaitingQTE = false;

        // 다음 QTE 인덱스로 넘어간다.
        currentQteIndex++;
    }

    // 서버에서 QTE 실패를 처리한다.
    [Server]
    private void FailQTEServer(NetworkIdentity identity)
    {
        // 대상이 없으면 조사만 중단한다.
        if (identity == null)
        {
            StopServerInteract();
            return;
        }

        // 실패한 생존자의 상태 컴포넌트를 가져온다.
        SurvivorState survivorState = identity.GetComponent<SurvivorState>();

        // 자식/부모 구조일 수도 있으니 부모에서도 찾는다.
        if (survivorState == null)
            survivorState = identity.GetComponentInParent<SurvivorState>();

        // 먼저 조사 상태를 중단한다.
        StopServerInteract();

        // 생존자 상태가 있으면 스턴을 적용한다.
        if (survivorState != null)
            survivorState.ApplyStun(qteFailStunTime);
    }

    // 서버에서 조사 진행도와 QTE 타이밍을 처리한다.
    [Server]
    private void ServerUpdateInteract()
    {
        // 조사 중이 아니거나 이미 완료된 증거면 처리하지 않는다.
        if (!isInteracting || isCompleted)
            return;

        // 현재 조사 중인 플레이어가 서버에 존재하는지 확인한다.
        if (!NetworkServer.spawned.TryGetValue(currentInteractorNetId, out NetworkIdentity identity))
        {
            StopServerInteract();
            return;
        }

        // 조사 중인 플레이어의 SurvivorInteractor를 가져온다.
        SurvivorInteractor interactor = identity.GetComponent<SurvivorInteractor>();

        // 상호작용 컴포넌트가 없으면 조사를 중단한다.
        if (interactor == null)
        {
            StopServerInteract();
            return;
        }

        // 조사 중인 플레이어가 범위 밖으로 나가면 조사를 중단한다.
        if (!CanInteractorUseThis(identity.transform))
        {
            StopServerInteract();
            return;
        }

        // QTE 결과를 기다리는 동안에는 진행도를 멈춘다.
        if (isWaitingQTE)
            return;

        // 서버에서만 진행도를 증가시킨다.
        progress += Time.deltaTime;

        // 아직 발생할 QTE가 남아 있다면 확인한다.
        if (currentQteIndex < qteTriggerProgressList.Count)
        {
            // 정규화된 QTE 타이밍을 실제 시간으로 바꾼다.
            float triggerTime = qteTriggerProgressList[currentQteIndex] * interactTime;

            // 현재 진행도가 QTE 타이밍에 도달하면 QTE를 시작한다.
            if (progress >= triggerTime)
            {
                // QTE 결과를 받을 때까지 진행도를 멈춘다.
                isWaitingQTE = true;

                // 조사 중인 클라이언트에게만 QTE UI를 띄운다.
                TargetStartQTE(identity.connectionToClient);

                return;
            }
        }

        // 진행도가 완료 시간에 도달하면 조사 완료 처리한다.
        if (progress >= interactTime)
            CompleteServer();
    }

    // 서버에서 조사 중단 상태를 정리한다.
    [Server]
    private void StopServerInteract()
    {
        // 조사 루프 사운드 종료
        StopLoopSound();

        // 조사 중 상태를 해제한다.
        isInteracting = false;

        // 조사 중인 플레이어 정보를 초기화한다.
        currentInteractorNetId = 0;

        // 중단 시 진행도를 0으로 되돌린다.
        progress = 0f;

        // QTE 대기 상태를 해제한다.
        isWaitingQTE = false;

        // QTE 타이밍 목록을 비운다.
        qteTriggerProgressList.Clear();

        // QTE 인덱스를 초기화한다.
        currentQteIndex = 0;

        // 모든 클라이언트에서 로컬 연출과 UI를 정리한다.
        RpcForceStopLocalEffects(true);
    }

    // 서버에서 조사 완료를 처리한다.
    [Server]
    private void CompleteServer()
    {
        // 완료로 끝나도 루프 사운드는 반드시 종료
        StopLoopSound();

        // 완료 상태를 true로 변경한다.
        isCompleted = true;

        // 조사 중 상태를 해제한다.
        isInteracting = false;

        // 조사 중인 플레이어 정보를 초기화한다.
        currentInteractorNetId = 0;

        // 완료 상태이므로 진행도를 최대값으로 맞춘다.
        progress = interactTime;

        // QTE 대기 상태를 해제한다.
        isWaitingQTE = false;

        // QTE 타이밍 목록을 비운다.
        qteTriggerProgressList.Clear();

        // QTE 인덱스를 초기화한다.
        currentQteIndex = 0;

        // 진짜 증거라면 EvidenceZone에 발견 완료를 보고한다.
        if (isRealEvidence)
        {
            Debug.Log($"{name} : 진짜 증거 발견!");
            zone?.OnRealEvidenceFound(this);
        }
        else
        {
            Debug.Log($"{name} : 가짜 포인트");
        }

        // 중요:
        // 서버에서도 콜라이더를 꺼야 서버 판정에서 증거가 완전히 사라진다.
        HideEvidenceLocal();

        // 모든 클라이언트에서도 렌더러와 콜라이더를 끈다.
        RpcCompleteEvidence();
    }

    // 서버에서 QTE 발생 타이밍을 랜덤으로 생성한다.
    [Server]
    private void SetupQTEPointsServer()
    {
        // 기존 QTE 타이밍 목록을 비운다.
        qteTriggerProgressList.Clear();

        // QTE 인덱스를 처음으로 되돌린다.
        currentQteIndex = 0;

        // 이번 조사에서 사용할 QTE 개수를 랜덤으로 정한다.
        int count = Random.Range(minQteCount, maxQteCount + 1);

        // QTE는 너무 처음이나 끝에 나오지 않게 15%~85% 사이에서 발생시킨다.
        float startNormalized = 0.15f;

        // QTE 발생 종료 지점이다.
        float endNormalized = 0.85f;

        // QTE 발생 가능 범위다.
        float totalRange = endNormalized - startNormalized;

        // QTE 개수만큼 구간을 나눈다.
        float sectionSize = totalRange / count;

        // 각 구간마다 QTE 타이밍을 하나씩 만든다.
        for (int i = 0; i < count; i++)
        {
            // 현재 구간 시작 지점이다.
            float sectionStart = startNormalized + sectionSize * i;

            // 현재 구간 끝 지점이다.
            float sectionEnd = sectionStart + sectionSize * 0.8f;

            // 현재 구간 안에서 랜덤 타이밍을 뽑는다.
            float point = Random.Range(sectionStart, sectionEnd);

            // QTE 타이밍 목록에 추가한다.
            qteTriggerProgressList.Add(point);
        }
    }

    // 조사 중인 클라이언트에게만 QTE를 시작시킨다.
    [TargetRpc]
    private void TargetStartQTE(NetworkConnection target)
    {
        // QTE UI가 없으면 SurvivorInteractor에서 가져온다.
        if (localQTEUI == null && localInteractor != null)
            localQTEUI = localInteractor.QTEUI;

        // QTE UI를 못 찾으면 실패 처리한다.
        if (localQTEUI == null)
        {
            CmdSubmitQTEResult(false);
            return;
        }

        // QTE를 시작하고 결과 콜백을 등록한다.
        localQTEUI.StartQTE(OnLocalQTEFinished);
    }

    // 로컬 QTE가 끝났을 때 호출된다.
    private void OnLocalQTEFinished(bool success)
    {
        // QTE 성공 여부를 서버로 보낸다.
        CmdSubmitQTEResult(success);
    }

    // 조사 중단 시 모든 클라이언트에서 로컬 연출을 정리한다.
    [ClientRpc]
    private void RpcForceStopLocalEffects(bool resetProgress)
    {
        // 이동 잠금, 애니메이션, UI를 정리한다.
        StopLocalEffects(resetProgress);
    }

    // 조사 완료 시 모든 클라이언트에서 증거를 숨긴다.
    [ClientRpc]
    private void RpcCompleteEvidence()
    {
        // 진행 중인 로컬 연출을 정리한다.
        StopLocalEffects(true);

        // 클라이언트에서 렌더러와 콜라이더를 끈다.
        HideEvidenceLocal();
    }

    // isCompleted SyncVar가 클라이언트에 반영될 때 호출된다.
    private void OnCompletedChanged(bool oldValue, bool newValue)
    {
        // 완료 상태가 되면 해당 클라이언트에서도 증거를 숨긴다.
        if (newValue)
            HideEvidenceLocal();
    }

    // 로컬 플레이어의 이동 잠금, 애니메이션, UI를 정리한다.
    private void StopLocalEffects(bool resetProgress)
    {
        // 로컬 이동 컴포넌트가 있으면 이동/애니메이션 상태를 정리한다.
        if (localMove != null)
        {
            // 조사 중 이동 잠금을 해제한다.
            localMove.SetMoveLock(false);

            // 조사 애니메이션을 끈다.
            localMove.SetSearching(false);

            // 카메라 스킬 애니메이션도 꺼둔다.
            localMove.SetCamAnim(false);
        }

        // QTE가 열려 있으면 닫는다.
        if (localQTEUI != null)
            localQTEUI.ForceClose(false);

        // ProgressUI는 SurvivorInteractor의 owner 구조로만 정리한다.
        if (localInteractor != null)
        {
            // 이 EvidencePoint가 소유한 ProgressUI만 숨긴다.
            localInteractor.HideProgress(this, resetProgress);

            // 더 이상 상호작용 후보가 아니므로 목록에서 제거한다.
            localInteractor.ClearInteractable(this);
        }
    }

    // 증거 오브젝트를 보이지 않고 충돌하지 않게 만든다.
    private void HideEvidenceLocal()
    {
        // 로컬 상호작용 후보와 ProgressUI를 정리한다.
        if (localInteractor != null)
        {
            // 이 증거가 띄운 ProgressUI를 숨긴다.
            localInteractor.HideProgress(this, true);

            // 이 증거를 상호작용 후보 목록에서 제거한다.
            localInteractor.ClearInteractable(this);
        }

        // QTE가 남아 있으면 닫는다.
        if (localQTEUI != null)
            localQTEUI.ForceClose(false);

        // 이 오브젝트와 자식의 모든 Collider를 가져온다.
        Collider[] cols = GetComponentsInChildren<Collider>(true);

        // 모든 Collider를 비활성화한다.
        for (int i = 0; i < cols.Length; i++)
            cols[i].enabled = false;

        // 이 오브젝트와 자식의 모든 Renderer를 가져온다.
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        // 모든 Renderer를 비활성화한다.
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = false;

        // 로컬 플레이어가 더 이상 이 증거 안에 있는 것으로 처리되지 않게 한다.
        isLocalInside = false;
    }

    // 로컬 플레이어가 조사 중일 때만 ProgressUI를 표시한다.
    private void UpdateLocalUI()
    {
        // 로컬 플레이어 정보가 없으면 UI를 갱신할 수 없다.
        if (localInteractor == null)
            return;

        // 현재 로컬 플레이어가 이 증거를 조사 중인지 확인한다.
        bool isMyInteract = isInteracting &&
                            !isCompleted &&
                            localInteractor.netId == currentInteractorNetId;

        // 내가 조사 중이면 ProgressUI를 보여준다.
        if (isMyInteract)
        {
            // interactTime이 0 이하일 경우를 대비해 안전하게 처리한다.
            float value = interactTime <= 0f ? 1f : progress / interactTime;

            // SurvivorInteractor의 ProgressUI owner 구조를 사용한다.
            localInteractor.ShowProgress(this, value);
        }
        else
        {
            // 내가 조사 중이 아니면 이 증거가 소유한 UI만 숨긴다.
            localInteractor.HideProgress(this, false);
        }
    }

    // 현재 로컬 플레이어가 이 증거를 상호작용 후보로 유지할지 판단한다.
    private void RefreshLocalAvailability()
    {
        // 로컬 플레이어가 트리거 안에 없으면 갱신하지 않는다.
        if (!isLocalInside)
            return;

        // 로컬 상호작용 컴포넌트가 없으면 갱신하지 않는다.
        if (localInteractor == null)
            return;

        // 완료된 증거는 상호작용 후보에서 제거한다.
        if (isCompleted)
        {
            localInteractor.ClearInteractable(this);
            return;
        }

        // 다른 플레이어가 조사 중이면 상호작용 후보에서 제거한다.
        if (IsBusyByOtherLocal())
        {
            localInteractor.ClearInteractable(this);
            return;
        }

        // 사용할 수 있는 상태면 상호작용 후보로 등록한다.
        localInteractor.SetInteractable(this);
    }

    // 다른 플레이어가 조사 중인지 로컬 기준으로 판단한다.
    private bool IsBusyByOtherLocal()
    {
        // 아무도 조사 중이 아니면 바쁘지 않다.
        if (!isInteracting)
            return false;

        // 로컬 플레이어 정보가 없으면 안전하게 바쁘다고 본다.
        if (localInteractor == null)
            return true;

        // 현재 조사 중인 사람이 내가 아니면 바쁜 상태다.
        return currentInteractorNetId != localInteractor.netId;
    }

    // 서버 기준으로 조사 가능한 거리인지 검사한다.
    private bool CanInteractorUseThis(Transform interactorTransform)
    {
        // 검사할 대상이 없으면 사용할 수 없다.
        if (interactorTransform == null)
            return false;

        // 이 증거 오브젝트의 Collider를 찾는다.
        Collider myCol = GetComponent<Collider>();

        // 루트에 없으면 자식에서 찾는다.
        if (myCol == null)
            myCol = GetComponentInChildren<Collider>();

        // Collider가 없으면 거리 판정을 할 수 없다.
        if (myCol == null)
            return false;

        // 증거 Collider에서 플레이어와 가장 가까운 지점을 구한다.
        Vector3 closest = myCol.ClosestPoint(interactorTransform.position);

        // 플레이어와 가장 가까운 지점 사이의 제곱 거리를 구한다.
        float sqrDist = (closest - interactorTransform.position).sqrMagnitude;

        // 4m 이내면 사용 가능하다.
        return sqrDist <= 4f;
    }

    // 로컬 플레이어가 증거 쪽을 바라보게 한다.
    private void FaceToEvidenceLocal()
    {
        // 로컬 이동 컴포넌트가 없으면 처리하지 않는다.
        if (localMove == null)
            return;

        // 플레이어에서 증거 방향을 계산한다.
        Vector3 lookDir = transform.position - localMove.transform.position;

        // 위아래 회전은 빼고 수평 방향만 사용한다.
        lookDir.y = 0f;

        // 방향이 너무 작으면 회전하지 않는다.
        if (lookDir.sqrMagnitude <= 0.001f)
            return;

        // 생존자 모델을 증거 방향으로 돌린다.
        localMove.FaceDirection(lookDir.normalized);
    }

    // 로컬 플레이어 이동 잠금을 설정한다.
    private void LockMovementLocal(bool value)
    {
        // 이동 컴포넌트가 있으면 이동 잠금을 적용한다.
        if (localMove != null)
            localMove.SetMoveLock(value);
    }

    // 로컬 플레이어 조사 애니메이션을 설정한다.
    private void SetSearchingLocal(bool value)
    {
        // 이동 컴포넌트가 있으면 Searching Bool을 적용한다.
        if (localMove != null)
            localMove.SetSearching(value);
    }

    // 사운드
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
        // 생존자만 처리한다.
        if (!other.CompareTag("Survivor"))
            return;

        // 들어온 오브젝트에서 SurvivorInteractor를 찾는다.
        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();

        // 자식 콜라이더일 수 있으므로 부모에서도 찾는다.
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        // 상호작용 컴포넌트가 없으면 무시한다.
        if (interactor == null)
            return;

        // 로컬 플레이어가 아니면 UI와 후보 등록을 하지 않는다.
        if (!interactor.isLocalPlayer)
            return;

        // 이미 완료된 증거면 들어와도 후보로 등록하지 않는다.
        if (isCompleted)
            return;

        // 로컬 상호작용 컴포넌트를 저장한다.
        localInteractor = interactor;

        // 로컬 이동 컴포넌트를 가져온다.
        localMove = interactor.GetComponent<SurvivorMove>();

        // 구조상 부모에 있을 수 있으므로 부모에서도 찾는다.
        if (localMove == null)
            localMove = interactor.GetComponentInParent<SurvivorMove>();

        // QTE UI는 SurvivorInteractor에서 가져온다.
        localQTEUI = interactor.QTEUI;

        // 로컬 플레이어가 범위 안에 있다고 저장한다.
        isLocalInside = true;

        // 현재 상태 기준으로 상호작용 후보 등록을 갱신한다.
        RefreshLocalAvailability();
    }

    // 로컬 생존자가 증거 범위 밖으로 나가면 호출된다.
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

        // 상호작용 컴포넌트가 없으면 무시한다.
        if (interactor == null)
            return;

        // 로컬 플레이어가 아니면 처리하지 않는다.
        if (!interactor.isLocalPlayer)
            return;

        // 이 증거를 상호작용 후보에서 제거한다.
        interactor.ClearInteractable(this);

        // 로컬 플레이어가 범위 밖에 있다고 저장한다.
        isLocalInside = false;

        // 저장된 로컬 플레이어와 나간 플레이어가 다르면 더 처리하지 않는다.
        if (localInteractor != interactor)
            return;

        // 이동 잠금을 해제한다.
        LockMovementLocal(false);

        // 조사 애니메이션을 끈다.
        SetSearchingLocal(false);

        // ProgressUI를 owner 방식으로 숨긴다.
        localInteractor.HideProgress(this, true);

        // QTE가 떠 있으면 닫는다.
        if (localQTEUI != null)
            localQTEUI.ForceClose(false);

        // 서버에 조사 종료를 요청한다.
        CmdEndInteract();

        // 로컬 참조를 정리한다.
        localInteractor = null;

        // 이동 참조를 정리한다.
        localMove = null;

        // QTE UI 참조를 정리한다.
        localQTEUI = null;
    }
}