using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class EvidencePoint : NetworkBehaviour, IInteractable
{
    // 증거는 Hold 타입이다.
    public InteractType InteractType => InteractType.Hold;

    [Header("조사 설정")]
    [SerializeField] private float interactTime = 10f;
    [SerializeField] private ProgressUI progressUI;

    [Header("QTE 설정")]
    [SerializeField] private int minQteCount = 2;
    [SerializeField] private int maxQteCount = 4;
    [SerializeField] private float qteFailStunTime = 3f;

    // 이 포인트가 속한 EvidenceZone이다.
    private EvidenceZone zone;

    // 진짜 증거인지 여부를 서버에서 동기화한다.
    [SyncVar]
    private bool isRealEvidence;

    // 조사 완료 여부를 서버에서 동기화한다.
    [SyncVar]
    private bool isCompleted;

    // 현재 조사 중인지 여부를 서버에서 동기화한다.
    [SyncVar]
    private bool isInteracting;

    // 현재 조사 진행도를 서버에서 동기화한다.
    [SyncVar]
    private float progress;

    // 현재 조사 중인 플레이어 netId를 저장한다.
    [SyncVar]
    private uint currentInteractorNetId;

    // 현재 QTE 결과를 기다리는 중인지 저장한다.
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

    // 이 EvidencePoint가 어느 EvidenceZone 소속인지 저장한다.
    public void SetZone(EvidenceZone evidenceZone)
    {
        zone = evidenceZone;
    }

    // 서버에서만 진짜/가짜 증거 여부를 설정한다.
    [Server]
    public void SetIsRealEvidenceServer(bool value)
    {
        isRealEvidence = value;
    }

    private void Update()
    {
        // 서버에서만 실제 조사 진행을 처리한다.
        if (isServer)
            ServerUpdateInteract();

        // 로컬 클라이언트에서만 UI 표시를 갱신한다.
        UpdateLocalUI();

        // 로컬 플레이어 기준 상호작용 후보 상태를 갱신한다.
        RefreshLocalAvailability();
    }

    // 상호작용 시작 시 로컬 연출 후 서버에 시작 요청을 보낸다.
    public void BeginInteract(GameObject actor)
    {
        if (isCompleted)
            return;

        if (localInteractor == null)
            return;

        if (IsBusyByOtherLocal())
            return;

        FaceToEvidenceLocal();
        LockMovementLocal(true);
        SetSearchingLocal(true);

        CmdBeginInteract();
    }

    // 상호작용 종료 시 로컬 연출을 끄고 서버에 종료 요청을 보낸다.
    public void EndInteract()
    {
        if (localInteractor == null)
            return;

        LockMovementLocal(false);
        SetSearchingLocal(false);

        if (progressUI != null)
        {
            progressUI.SetProgress(0f);
            progressUI.Hide();
        }

        if (localQTEUI != null)
            localQTEUI.ForceClose(false);

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

        if (isInteracting && currentInteractorNetId != sender.identity.netId)
            return;

        if (!CanInteractorUseThis(sender.identity.transform))
            return;

        isInteracting = true;
        currentInteractorNetId = sender.identity.netId;
        progress = 0f;
        isWaitingQTE = false;

        SetupQTEPointsServer();
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

    // 서버에서 QTE 실패 시 조사 중단 후 생존자에게 스턴을 적용한다.
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

    // 서버에서 조사 진행도와 QTE 발생 타이밍을 처리한다.
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

    // 서버에서 조사 상태를 중단하고 모든 클라이언트의 로컬 연출을 정리한다.
    [Server]
    private void StopServerInteract()
    {
        isInteracting = false;
        currentInteractorNetId = 0;
        progress = 0f;
        isWaitingQTE = false;

        qteTriggerProgressList.Clear();
        currentQteIndex = 0;

        RpcForceStopLocalEffects();
    }

    // 서버에서 조사 완료를 처리하고 진짜 증거면 EvidenceZone에 알린다.
    [Server]
    private void CompleteServer()
    {
        isCompleted = true;
        isInteracting = false;
        currentInteractorNetId = 0;
        progress = interactTime;
        isWaitingQTE = false;

        qteTriggerProgressList.Clear();
        currentQteIndex = 0;

        if (isRealEvidence)
        {
            Debug.Log($"{name} : 진짜 증거 발견!");
            zone?.OnRealEvidenceFound(this);
        }
        else
        {
            Debug.Log($"{name} : 가짜 포인트");
        }

        RpcForceStopLocalEffects();
        RpcHideEvidence();
    }

    // 서버에서 QTE 발생 위치를 랜덤으로 생성한다.
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

    // 조사 중인 플레이어 클라이언트에만 QTE 시작을 보낸다.
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

    // 로컬 QTE가 끝나면 서버에 성공 여부를 보낸다.
    private void OnLocalQTEFinished(bool success)
    {
        CmdSubmitQTEResult(success);
    }

    // 모든 클라이언트에서 로컬 이동 잠금, UI, QTE를 정리한다.
    [ClientRpc]
    private void RpcForceStopLocalEffects()
    {
        if (localMove != null)
        {
            localMove.SetMoveLock(false);
            localMove.SetSearching(false);
            localMove.SetCamAnim(false);
        }

        if (progressUI != null)
        {
            progressUI.SetProgress(0f);
            progressUI.Hide();
        }

        if (localQTEUI != null)
            localQTEUI.ForceClose(false);

        if (localInteractor != null)
            localInteractor.ClearInteractable(this);
    }

    // 조사 완료된 증거를 모든 클라이언트에서 보이지 않고 충돌하지 않게 만든다.
    [ClientRpc]
    private void RpcHideEvidence()
    {
        HideEvidenceLocal();
    }

    // NetworkIdentity 오브젝트는 끄지 않고 Renderer와 Collider만 비활성화한다.
    private void HideEvidenceLocal()
    {
        if (localInteractor != null)
            localInteractor.ClearInteractable(this);

        Collider[] cols = GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < cols.Length; i++)
            cols[i].enabled = false;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
            renderers[i].enabled = false;

        if (progressUI != null)
        {
            progressUI.SetProgress(0f);
            progressUI.Hide();
        }

        if (localQTEUI != null)
            localQTEUI.ForceClose(false);

        // gameObject.SetActive(false)는 사용하지 않는다.
    }

    // 내 로컬 플레이어가 조사 중일 때만 ProgressUI를 표시한다.
    private void UpdateLocalUI()
    {
        if (progressUI == null)
            return;

        if (localInteractor == null)
            return;

        bool isMyInteract = false;

        if (isInteracting && !isCompleted && localInteractor.netId == currentInteractorNetId)
            isMyInteract = true;

        if (isMyInteract)
        {
            progressUI.Show();
            progressUI.SetProgress(progress / interactTime);
        }
        else
        {
            progressUI.Hide();
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

    // 로컬 플레이어 Searching 애니메이션을 설정한다.
    private void SetSearchingLocal(bool value)
    {
        if (localMove != null)
            localMove.SetSearching(value);
    }

    // 로컬 생존자가 범위 안에 들어오면 상호작용 후보로 등록한다.
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

        localInteractor = interactor;

        localMove = interactor.GetComponent<SurvivorMove>();

        if (localMove == null)
            localMove = interactor.GetComponentInParent<SurvivorMove>();

        progressUI = interactor.ProgressUI;
        localQTEUI = interactor.QTEUI;

        isLocalInside = true;

        RefreshLocalAvailability();
    }

    // 로컬 생존자가 범위 밖으로 나가면 상호작용 후보와 UI를 정리한다.
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

        if (localInteractor == interactor)
        {
            LockMovementLocal(false);
            SetSearchingLocal(false);

            if (progressUI != null)
            {
                progressUI.SetProgress(0f);
                progressUI.Hide();
            }

            if (localQTEUI != null)
                localQTEUI.ForceClose(false);

            CmdEndInteract();

            localInteractor = null;
            localMove = null;
            localQTEUI = null;
        }
    }
}