using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class EvidencePoint : NetworkBehaviour, IInteractable
{
    // 증거는 누르고 있는 동안 진행되는 Hold 타입 상호작용이다.
    public InteractType InteractType => InteractType.Hold;

    [Header("조사 설정")]
    [SerializeField] private float interactTime = 10f;

    [Header("QTE 설정")]
    [SerializeField] private int minQteCount = 2;
    [SerializeField] private int maxQteCount = 4;
    [SerializeField] private float qteFailStunTime = 3f;

    private EvidenceZone zone;

    [SyncVar]
    private EvidenceType evidenceType = EvidenceType.None;

    [SyncVar]
    private string displayName;

    // 실제로 조사 완료된 증거인지 여부다.
    // true가 되면 완료된 증거 오브젝트는 보이지 않게 처리한다.
    [SyncVar(hook = nameof(OnCompletedChanged))]
    private bool isCompleted;

    // 목표 완료 후 강제로 상호작용만 막힌 상태다.
    // true가 되어도 오브젝트는 계속 보이고, 조사와 원 아이콘만 막힌다.
    [SyncVar(hook = nameof(OnInteractionDisabledChanged))]
    private bool isInteractionDisabled;

    [SyncVar]
    private bool isInteracting;

    [SyncVar]
    private float progress;

    [SyncVar]
    private uint currentInteractorNetId;

    [SyncVar]
    private bool isWaitingQTE;

    private readonly List<float> qteTriggerProgressList = new List<float>();

    private int currentQteIndex;

    private SurvivorInteractor localInteractor;
    private SurvivorMove localMove;
    private QTEUI localQTEUI;

    private bool isLocalInside;

    public EvidenceType EvidenceType => evidenceType;
    public bool IsCompleted => isCompleted;
    public bool IsInteractionDisabled => isInteractionDisabled;
    public bool IsInteractionBlocked => isCompleted || isInteractionDisabled;
    public bool CanShowInteractIcon => !IsInteractionBlocked;

    public bool IsInteractingForUI => isInteracting;
    public uint CurrentInteractorNetId => currentInteractorNetId;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;

            return evidenceType.ToString();
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

    [Server]
    public void ServerInit(EvidenceZone ownerZone, EvidenceType type, string nameText)
    {
        zone = ownerZone;
        evidenceType = type;
        displayName = nameText;
    }

    public void SetZone(EvidenceZone evidenceZone)
    {
        zone = evidenceZone;
    }

    [Server]
    public void SetIsRealEvidenceServer(bool value)
    {
        // 현재 구조에서는 생성된 EvidencePoint가 무조건 진짜 증거라서 사용하지 않는다.
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (isCompleted)
        {
            HideEvidenceLocal();
            return;
        }

        if (isInteractionDisabled)
            StopLocalUse(true, true);
    }

    private void Update()
    {
        if (isServer)
            ServerUpdateInteract();

        UpdateLocalUI();
        RefreshLocalAvailability();
    }

    public void BeginInteract(GameObject actor)
    {
        if (IsInteractionBlocked)
            return;

        if (localInteractor == null)
            return;

        if (IsBusyByOtherLocal())
            return;

        FaceToEvidenceLocal();
        LockMovementLocal(true);
        SetSearchingLocal(true);

        localInteractor.ShowProgress(this, 0f);

        CmdBeginInteract();
    }

    public void EndInteract()
    {
        if (localInteractor == null)
            return;

        StopLocalUse(true, false);

        CmdEndInteract();
    }

    [Command(requiresAuthority = false)]
    private void CmdBeginInteract(NetworkConnectionToClient sender = null)
    {
        if (IsInteractionBlocked)
            return;

        if (sender == null || sender.identity == null)
            return;

        SurvivorInteractor interactor = sender.identity.GetComponent<SurvivorInteractor>();
        if (interactor == null)
            return;

        SurvivorMove move = sender.identity.GetComponent<SurvivorMove>();
        if (move == null)
            return;

        if (isInteracting && currentInteractorNetId != sender.identity.netId)
            return;

        if (!CanInteractorUseThis(sender.identity.transform))
            return;

        isInteracting = true;
        currentInteractorNetId = sender.identity.netId;
        progress = 0f;
        isWaitingQTE = false;

        SetupQTEPointsServer();

        StartLoopSound();
    }

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

        StopServerInteract();

        if (survivorState != null)
            survivorState.ApplyStun(qteFailStunTime);
    }

    [Server]
    private void ServerUpdateInteract()
    {
        if (!isInteracting || IsInteractionBlocked)
            return;

        if (!NetworkServer.spawned.TryGetValue(currentInteractorNetId, out NetworkIdentity identity))
        {
            StopServerInteract();
            return;
        }

        if (identity.GetComponent<SurvivorInteractor>() == null)
        {
            StopServerInteract();
            return;
        }

        if (!CanInteractorUseThis(identity.transform))
        {
            StopServerInteract();
            return;
        }

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

    [Server]
    private void StopServerInteract()
    {
        StopLoopSound();
        ResetServerInteract(0f);
        RpcStopLocalUse(true, false);
    }

    [Server]
    private void CompleteServer()
    {
        StopLoopSound();

        uint finderNetId = currentInteractorNetId;

        isCompleted = true;
        ResetServerInteract(interactTime);

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

        // 완료한 증거는 서버에서도 즉시 안 보이게 처리한다.
        HideEvidenceLocal();

        int evidenceIndex = (int)evidenceType - 1;

        // 모든 클라이언트에서도 완료한 증거를 안 보이게 처리하고, 획득한 증거 UI를 갱신한다.
        RpcCompleteEvidence(evidenceIndex);
    }

    // 목표 완료 후 이 증거를 숨기지 않고 상호작용만 막는다.
    [Server]
    public void ServerDisableInteractionOnly()
    {
        if (isCompleted || isInteractionDisabled)
            return;

        StopLoopSound();

        isInteractionDisabled = true;
        ResetServerInteract(0f);

        RpcStopLocalUse(true, true);
    }

    [Server]
    private void ResetServerInteract(float newProgress)
    {
        isInteracting = false;
        currentInteractorNetId = 0;
        progress = newProgress;
        isWaitingQTE = false;

        qteTriggerProgressList.Clear();
        currentQteIndex = 0;
    }

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

    private void OnLocalQTEFinished(bool success)
    {
        CmdSubmitQTEResult(success);
    }

    [ClientRpc]
    private void RpcStopLocalUse(bool resetProgress, bool clearInside)
    {
        StopLocalUse(resetProgress, clearInside);
    }

    [ClientRpc]
    private void RpcCompleteEvidence(int evidenceIndex)
    {
        StopLocalUse(true, true);
        HideEvidenceLocal();

        if (InGameUIManager.Instance != null)
            InGameUIManager.Instance.ShowAcquiredEvidence(evidenceIndex);
    }

    private void OnCompletedChanged(bool oldValue, bool newValue)
    {
        if (!newValue)
            return;

        StopLocalUse(true, true);
        HideEvidenceLocal();

        ShowEvidenceUIByType();
    }

    private void ShowEvidenceUIByType()
    {
        int evidenceIndex = (int)evidenceType - 1;

        if (InGameUIManager.Instance != null)
            InGameUIManager.Instance.ShowAcquiredEvidence(evidenceIndex);
    }

    private void OnInteractionDisabledChanged(bool oldValue, bool newValue)
    {
        if (!newValue)
            return;

        StopLocalUse(true, true);
    }

    private void StopLocalUse(bool resetProgress, bool clearInside)
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

        if (clearInside)
        {
            isLocalInside = false;
            localInteractor = null;
            localMove = null;
            localQTEUI = null;
        }
    }

    // 완료한 증거만 안 보이게 한다.
    // 목표 완료로 상호작용만 막힌 증거에는 이 함수를 호출하지 않는다.
    private void HideEvidenceLocal()
    {
        StopLocalUse(true, true);

        Collider[] cols = GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < cols.Length; i++)
            cols[i].enabled = false;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = false;
    }

    private void UpdateLocalUI()
    {
        if (localInteractor == null)
            return;

        bool isMyInteract = isInteracting &&
                            !IsInteractionBlocked &&
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

    private void RefreshLocalAvailability()
    {
        if (!isLocalInside)
            return;

        if (localInteractor == null)
            return;

        if (IsInteractionBlocked || IsBusyByOtherLocal())
        {
            localInteractor.ClearInteractable(this);
            return;
        }

        localInteractor.SetInteractable(this);
    }

    private bool IsBusyByOtherLocal()
    {
        if (!isInteracting)
            return false;

        if (localInteractor == null)
            return true;

        return currentInteractorNetId != localInteractor.netId;
    }

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

        return sqrDist <= 4f;
    }

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

    private void LockMovementLocal(bool value)
    {
        if (localMove != null)
            localMove.SetMoveLock(value);
    }

    private void SetSearchingLocal(bool value)
    {
        if (localMove != null)
            localMove.SetSearching(value);
    }

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

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Survivor"))
            return;

        if (IsInteractionBlocked)
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();

        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        if (interactor == null)
            return;

        if (!interactor.isLocalPlayer)
            return;

        localInteractor = interactor;

        localMove = interactor.GetComponent<SurvivorMove>();

        if (localMove == null)
            localMove = interactor.GetComponentInParent<SurvivorMove>();

        localQTEUI = interactor.QTEUI;

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

        isLocalInside = false;

        if (localInteractor != interactor)
            return;

        StopLocalUse(true, false);

        CmdEndInteract();

        localInteractor = null;
        localMove = null;
        localQTEUI = null;
    }
}