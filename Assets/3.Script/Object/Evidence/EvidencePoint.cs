using Mirror;
using UnityEngine;

public class EvidencePoint : NetworkBehaviour, IInteractable
{
    // 이 오브젝트는 Hold 타입 상호작용
    public InteractType InteractType => InteractType.Hold;

    [Header("조사 설정")]
    [SerializeField] private float interactTime = 10f;
    [SerializeField] private ProgressUI progressUI;

    private EvidenceZone zone;

    // 진짜 증거 여부
    [SyncVar]
    private bool isRealEvidence;

    // 완료 여부
    [SyncVar(hook = nameof(OnCompletedChanged))]
    private bool isCompleted;

    // 현재 조사 중인지
    [SyncVar]
    private bool isInteracting;

    // 현재 진행도 시간
    [SyncVar]
    private float progress;

    // 현재 조사 중인 플레이어의 netId
    [SyncVar]
    private uint currentInteractorNetId;

    // 로컬 플레이어 쪽 캐시 참조
    private SurvivorInteractor localInteractor;
    private SurvivorMove localMove;

    public void SetZone(EvidenceZone evidenceZone)
    {
        zone = evidenceZone;
    }

    [Server]
    public void SetIsRealEvidenceServer(bool value)
    {
        isRealEvidence = value;
    }

    private void Awake()
    {
        if (progressUI == null)
            progressUI = FindFirstObjectByType<ProgressUI>();
    }

    private void Update()
    {
        // 실제 진행도 증가는 서버에서만 처리
        if (isServer)
        {
            ServerUpdateInteract();
        }

        // UI 표시는 각 클라이언트 로컬에서 처리
        UpdateLocalUI();
    }

    // 로컬 플레이어가 상호작용 시작
    public void BeginInteract()
    {
        if (isCompleted)
            return;

        if (localInteractor == null)
            return;

        // 이미 다른 사람이 사용 중이면 시작 자체를 막음
        // 이 체크를 넣으면 "누군가 하고 있는 걸 내가 또 잡는" 느낌을 줄일 수 있다.
        if (IsBusyByOtherLocal())
            return;

        FaceToEvidenceLocal();
        LockMovementLocal(true);
        SetSearchingLocal(true);

        CmdBeginInteract();
    }

    // 로컬 플레이어가 상호작용 취소
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

        CmdEndInteract();
    }

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

        // 이미 다른 사람이 조사 중이면 막음
        if (isInteracting && currentInteractorNetId != sender.identity.netId)
            return;

        // 범위 안에 있는지 서버가 최종 확인
        if (!CanInteractorUseThis(interactor.transform))
            return;

        isInteracting = true;
        currentInteractorNetId = sender.identity.netId;
        progress = 0f;
    }

    [Command(requiresAuthority = false)]
    private void CmdEndInteract(NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null)
            return;

        if (!isInteracting)
            return;

        // 현재 조사 중인 본인만 취소 가능
        if (currentInteractorNetId != sender.identity.netId)
            return;

        isInteracting = false;
        currentInteractorNetId = 0;
        progress = 0f;

        RpcForceStopLocalEffects();
    }

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

        if (!CanInteractorUseThis(interactor.transform))
        {
            StopServerInteract();
            return;
        }

        progress += Time.deltaTime;

        if (progress >= interactTime)
        {
            CompleteServer();
        }
    }

    [Server]
    private void StopServerInteract()
    {
        isInteracting = false;
        currentInteractorNetId = 0;
        progress = 0f;

        RpcForceStopLocalEffects();
    }

    [Server]
    private void CompleteServer()
    {
        isCompleted = true;
        isInteracting = false;
        currentInteractorNetId = 0;
        progress = interactTime;

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
        gameObject.SetActive(false);
    }

    [ClientRpc]
    private void RpcForceStopLocalEffects()
    {
        if (localMove != null)
        {
            localMove.SetMoveLock(false);
            localMove.SetSearching(false);
        }

        if (progressUI != null)
        {
            progressUI.SetProgress(0f);
            progressUI.Hide();
        }
    }

    private void OnCompletedChanged(bool oldValue, bool newValue)
    {
        if (!newValue)
            return;

        if (progressUI != null)
            progressUI.Hide();
    }

    private void UpdateLocalUI()
    {
        if (progressUI == null)
            return;

        if (localInteractor == null)
            return;

        bool isMyInteract =
            isInteracting &&
            localInteractor.netId == currentInteractorNetId &&
            !isCompleted;

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

    // 로컬 기준으로 "다른 사람이 이미 쓰고 있는 중인지" 확인
    // true면 나는 이 증거를 잡으면 안 됨
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

        // 이미 다른 사람이 사용 중이면
        // 이 로컬 플레이어에게는 상호작용 가능 대상으로 등록하지 않음
        if (IsBusyByOtherLocal())
        {
            Debug.Log($"{name} : 다른 플레이어가 조사 중이라 상호작용 불가");
            return;
        }

        interactor.SetInteractable(this);
        Debug.Log($"{name} 범위 진입");
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

        if (localInteractor == interactor)
        {
            LockMovementLocal(false);
            SetSearchingLocal(false);

            if (progressUI != null)
            {
                progressUI.SetProgress(0f);
                progressUI.Hide();
            }

            CmdEndInteract();

            localInteractor = null;
            localMove = null;
        }

        Debug.Log($"{name} 범위 이탈");
    }
}