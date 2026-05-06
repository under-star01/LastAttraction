using Mirror;
using UnityEngine;

public class SurvivorHeal : NetworkBehaviour, IInteractable
{
    // 힐은 Hold 타입
    public InteractType InteractType => InteractType.Hold;

    [Header("힐 설정")]
    [SerializeField] private float healTime = 16f;

    [Header("참조")]
    [SerializeField] private SurvivorState targetState;                 // 힐 대상 몸 상태
    [SerializeField] private SurvivorActionState targetActionState;     // 힐 대상 행동 상태
    [SerializeField] private SurvivorMove targetMove;                   // 힐 대상 이동 제어
    [SerializeField] private SurvivorInteractor targetInteractor;       // 힐 대상 UI 표시용

    [SyncVar] private uint healer;      // 현재 힐 중인 플레이어 netId
    [SyncVar] private bool isHealing;   // 현재 힐 중인지
    [SyncVar] private float progress;   // 현재 진행도

    // 힐하는 쪽 로컬 참조
    private SurvivorInteractor localHealerInteractor;
    private SurvivorState localHealerState;
    private SurvivorMove localHealerMove;
    private SurvivorActionState localHealerActionState;

    // 힐받는 대상 쪽 로컬 참조
    private SurvivorInteractor localTargetInteractor;

    // 이 로컬 플레이어가 범위 안에 있는지
    private bool isLocalInside;

    public bool IsHealing => isHealing;
    public uint HealerNetId => healer;

    public uint TargetNetId
    {
        get
        {
            if (targetState == null)
                return 0;

            return targetState.netId;
        }
    }

    public float Progress01
    {
        get
        {
            if (healTime <= 0f)
                return 1f;

            return Mathf.Clamp01(progress / healTime);
        }
    }

    private void Awake()
    {
        if (targetState == null)
            targetState = GetComponentInParent<SurvivorState>();

        if (targetActionState == null)
            targetActionState = GetComponentInParent<SurvivorActionState>();

        if (targetMove == null)
            targetMove = GetComponentInParent<SurvivorMove>();

        if (targetInteractor == null)
            targetInteractor = GetComponentInParent<SurvivorInteractor>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        CacheTarget();
    }

    private void Update()
    {
        if (isServer)
            HealUpdate();

        CacheTarget();
        UpdateUI();
        RefreshLocalAvailability();
    }

    public void BeginInteract(GameObject actor)
    {
        if (!CanHeal())
            return;

        if (IsBusy())
            return;

        if (localHealerMove != null)
            localHealerMove.SetCamAnim(false);

        FaceTarget();
        SetHealerLock(true);
        SetHealAnim(true);

        CmdBeginHeal();
    }

    public void EndInteract()
    {
        if (localHealerInteractor == null)
            return;

        bool isMyHeal = isHealing && healer == localHealerInteractor.netId;

        if (!isMyHeal)
            return;

        SetHealerLock(false);
        SetHealAnim(false);
        localHealerInteractor.HideProgress(this, false);

        CmdEndHeal();
    }

    [Command(requiresAuthority = false)]
    private void CmdBeginHeal(NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null)
            return;

        SurvivorInteractor healerInteractor = sender.identity.GetComponent<SurvivorInteractor>();
        SurvivorState healerState = sender.identity.GetComponent<SurvivorState>();
        SurvivorMove healerMove = sender.identity.GetComponent<SurvivorMove>();
        SurvivorActionState healerActionState = sender.identity.GetComponent<SurvivorActionState>();

        if (healerInteractor == null || healerState == null || healerMove == null)
            return;

        if (targetState == null || targetMove == null)
            return;

        if (targetState.IsHealthy)
            return;

        if (targetState.IsImprisoned)
            return;

        if (healerState.IsDowned)
            return;

        if (healerState == targetState)
            return;

        if (isHealing && healer != sender.identity.netId)
            return;

        if (!CanUse(healerInteractor.transform))
            return;

        if (targetActionState != null && targetActionState.IsDoingInteraction)
            return;

        if (healerActionState != null)
            healerActionState.SetCam(false);

        isHealing = true;
        healer = sender.identity.netId;

        // 힐 루프 사운드 시작
        StartHealLoopSound();

        if (targetActionState != null)
            targetActionState.SetHeal(true);

        if (targetState.connectionToClient != null)
            TargetLockTarget(targetState.connectionToClient, true);
    }

    [Command(requiresAuthority = false)]
    private void CmdEndHeal(NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null)
            return;

        if (!isHealing)
            return;

        if (healer != sender.identity.netId)
            return;

        StopHeal();
    }

    [Server]
    private void HealUpdate()
    {
        if (!isHealing)
            return;

        if (!NetworkServer.spawned.TryGetValue(healer, out NetworkIdentity identity))
        {
            StopHeal();
            return;
        }

        SurvivorInteractor healerInteractor = identity.GetComponent<SurvivorInteractor>();
        SurvivorState healerState = identity.GetComponent<SurvivorState>();

        if (healerInteractor == null || healerState == null)
        {
            StopHeal();
            return;
        }

        if (targetState.IsHealthy || healerState.IsDowned)
        {
            StopHeal();
            return;
        }

        if (targetState.IsImprisoned)
        {
            StopHeal();
            return;
        }

        if (!CanUse(healerInteractor.transform))
        {
            StopHeal();
            return;
        }

        progress += Time.deltaTime;

        if (progress >= healTime)
            CompleteHeal();
    }

    [Server]
    private void StopHeal()
    {
        StopHealLoopSound();

        uint stoppedHealer = healer;

        isHealing = false;
        healer = 0;

        if (targetActionState != null)
            targetActionState.SetHeal(false);

        if (targetState.connectionToClient != null)
            TargetLockTarget(targetState.connectionToClient, false);

        RpcStopHeal(stoppedHealer);
    }

    [Server]
    private void CompleteHeal()
    {
        StopHealLoopSound();

        uint stoppedHealer = healer;

        isHealing = false;
        healer = 0;
        progress = 0f;

        if (targetActionState != null)
            targetActionState.SetHeal(false);

        if (targetState.connectionToClient != null)
            TargetLockTarget(targetState.connectionToClient, false);

        if (targetState.IsDowned)
            targetState.RecoverToInjured();
        else if (targetState.IsInjured)
            targetState.HealToHealthy();

        RpcStopHeal(stoppedHealer);
    }

    [ClientRpc]
    private void RpcStopHeal(uint stoppedHealer)
    {
        if (localHealerMove != null && localHealerInteractor != null)
        {
            if (localHealerInteractor.netId == stoppedHealer)
            {
                localHealerMove.SetMoveLock(false);
                localHealerMove.SetSearching(false);
                localHealerMove.SetCamAnim(false);
            }
        }

        if (targetMove != null)
            targetMove.StopAnimation();

        if (localHealerInteractor != null)
            localHealerInteractor.HideProgress(this, false);

        if (localTargetInteractor != null)
            localTargetInteractor.HideProgress(this, false);
    }

    private void UpdateUI()
    {
        if (localHealerInteractor != null)
        {
            bool isMyHeal = false;

            if (isHealing && healer == localHealerInteractor.netId && !targetState.IsHealthy)
                isMyHeal = true;

            if (isMyHeal)
                localHealerInteractor.ShowProgress(this, progress / healTime);
            else
                localHealerInteractor.HideProgress(this, false);
        }

        if (localTargetInteractor != null && targetState.isLocalPlayer)
        {
            bool isMyTargetHeal = false;

            if (isHealing && !targetState.IsHealthy)
                isMyTargetHeal = true;

            if (isMyTargetHeal)
                localTargetInteractor.ShowProgress(this, progress / healTime);
            else
                localTargetInteractor.HideProgress(this, false);
        }
    }

    private void RefreshLocalAvailability()
    {
        if (!isLocalInside)
            return;

        if (localHealerInteractor == null)
            return;

        if (CanHeal() && !IsBusy())
            localHealerInteractor.SetInteractable(this);
        else
            localHealerInteractor.ClearInteractable(this);
    }

    private bool CanHeal()
    {
        if (targetState == null)
            return false;

        if (targetState.IsHealthy)
            return false;

        if (targetState.IsImprisoned)
            return false;

        if (localHealerInteractor == null || localHealerState == null)
            return false;

        if (localHealerState.IsDowned)
            return false;

        if (localHealerState == targetState)
            return false;

        if (targetActionState != null && targetActionState.IsDoingInteraction)
            return false;

        return true;
    }

    private bool IsBusy()
    {
        if (!isHealing)
            return false;

        if (localHealerInteractor == null)
            return true;

        return healer != localHealerInteractor.netId;
    }

    private bool CanUse(Transform healerTransform)
    {
        Collider col = GetComponent<Collider>();

        if (col == null)
            col = GetComponentInChildren<Collider>();

        if (col == null)
            return false;

        Vector3 closest = col.ClosestPoint(healerTransform.position);
        float sqrDist = (closest - healerTransform.position).sqrMagnitude;

        return sqrDist <= 4f;
    }

    private void SetHealAnim(bool value)
    {
        if (localHealerMove != null)
            localHealerMove.SetSearching(value);
    }

    private void FaceTarget()
    {
        if (localHealerMove == null)
            return;

        Vector3 lookDir = targetState.transform.position - localHealerMove.transform.position;
        lookDir.y = 0f;

        if (lookDir.sqrMagnitude <= 0.001f)
            return;

        localHealerMove.FaceDirection(lookDir.normalized);
    }

    private void SetHealerLock(bool value)
    {
        if (localHealerMove != null)
            localHealerMove.SetMoveLock(value);
    }

    private void CacheTarget()
    {
        if (targetState == null)
            return;

        if (!targetState.isLocalPlayer)
            return;

        if (localTargetInteractor == null)
            localTargetInteractor = targetInteractor;
    }

    [TargetRpc]
    private void TargetLockTarget(NetworkConnection target, bool value)
    {
        if (targetMove == null)
            return;

        targetMove.SetMoveLock(value);

        if (value)
            targetMove.StopAnimation();
    }

    [Server]
    private void StartHealLoopSound()
    {
        NetworkAudioManager.StartLoopAudioForEveryone(
            netId,
            AudioKey.SurvivorHealLoop,
            AudioDimension.Sound3D,
            transform.position
        );
    }

    [Server]
    private void StopHealLoopSound()
    {
        NetworkAudioManager.StopLoopAudioForEveryone(
            netId,
            AudioKey.SurvivorHealLoop
        );
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Survivor"))
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        if (interactor == null || !interactor.isLocalPlayer)
            return;

        localHealerInteractor = interactor;
        localHealerState = interactor.GetComponent<SurvivorState>();
        localHealerMove = interactor.GetComponent<SurvivorMove>();
        localHealerActionState = interactor.GetComponent<SurvivorActionState>();

        if (localHealerState == targetState)
            return;

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

        if (interactor == null || !interactor.isLocalPlayer)
            return;

        interactor.ClearInteractable(this);
        isLocalInside = false;

        if (localHealerInteractor != interactor)
            return;

        bool isMyHeal = isHealing && healer == interactor.netId;

        if (isMyHeal)
        {
            SetHealerLock(false);
            SetHealAnim(false);
            localHealerMove.SetCamAnim(false);
            localHealerInteractor.HideProgress(this, false);
            CmdEndHeal();
        }

        localHealerInteractor = null;
        localHealerState = null;
        localHealerMove = null;
        localHealerActionState = null;
    }

}