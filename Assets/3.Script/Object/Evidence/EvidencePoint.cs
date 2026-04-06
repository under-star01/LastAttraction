using Mirror;
using UnityEngine;

public class EvidencePoint : NetworkBehaviour, IInteractable
{
    // 증거는 Hold 타입
    public InteractType InteractType => InteractType.Hold;

    [Header("조사 설정")]
    [SerializeField] private float interactTime = 10f;
    [SerializeField] private ProgressUI progressUI;

    private EvidenceZone zone;

    [SyncVar]
    private bool isRealEvidence;

    [SyncVar(hook = nameof(OnCompletedChanged))]
    private bool isCompleted;

    [SyncVar]
    private bool isInteracting;

    [SyncVar]
    private float progress;

    [SyncVar]
    private uint currentInteractorNetId;

    private SurvivorInteractor localInteractor;
    private SurvivorMove localMove;

    // 내 로컬 플레이어가 범위 안에 있는지
    private bool isLocalInside;

    public void SetZone(EvidenceZone evidenceZone)
    {
        zone = evidenceZone;
    }

    [Server]
    public void SetIsRealEvidenceServer(bool value)
    {
        isRealEvidence = value;
    }

    private void Update()
    {
        if (isServer)
            ServerUpdateInteract();

        UpdateLocalUI();
        RefreshLocalAvailability();
    }

    // 머지 후 인터페이스에 맞춰 actor 파라미터 받음
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

        if (sender.identity.GetComponent<SurvivorMove>() == null)
            return;

        if (isInteracting && currentInteractorNetId != sender.identity.netId)
            return;

        if (!CanInteractorUseThis(sender.identity.transform))
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

        if (!CanInteractorUseThis(identity.transform))
        {
            StopServerInteract();
            return;
        }

        progress += Time.deltaTime;

        if (progress >= interactTime)
            CompleteServer();
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

        if (localInteractor != null)
            localInteractor.ClearInteractable(this);
    }

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

    // 다른 사람이 먼저 하고 있었더라도
    // 끊으면 범위 안 대기 중인 다른 플레이어가 바로 가능하게 함
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

        progressUI = interactor.ProgressUI;
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
    }
}