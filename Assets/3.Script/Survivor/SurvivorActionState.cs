using System.Collections;
using Mirror;
using UnityEngine;

// 생존자의 현재 행동 상태
// 이동 상태와 몸 상태와는 별개로
// "지금 어떤 행동 때문에 다른 행동을 막아야 하는가"를 관리한다.
public enum SurvivorAction
{
    None,
    DownHit,
    Healing,
    Interacting,
    Stunned,
    Vault
}

public class SurvivorActionState : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private SurvivorMove move;
    [SerializeField] private SurvivorInteractor interactor;
    [SerializeField] private Animator animator;

    // 현재 대표 행동 상태
    [SyncVar(hook = nameof(OnActChanged))]
    private SurvivorAction currentAction = SurvivorAction.None;

    // 힐을 받고 있는 중인지
    [SyncVar(hook = nameof(OnHealChanged))]
    private bool isBeingHealed;

    // Hold 상호작용을 진행 중인지
    [SyncVar]
    private bool isDoingInteraction;

    // 우클릭 카메라 스킬을 사용 중인지
    [SyncVar]
    private bool isCamSkill;

    public SurvivorAction CurrentAction => currentAction;
    public bool IsBeingHealed => isBeingHealed;
    public bool IsDoingInteraction => isDoingInteraction;
    public bool IsCamSkill => isCamSkill;
    public bool IsVault => currentAction == SurvivorAction.Vault;

    // 실제로 강하게 행동을 막는 상태만 Busy로 취급
    public bool IsBusy =>
        currentAction == SurvivorAction.DownHit ||
        currentAction == SurvivorAction.Stunned ||
        currentAction == SurvivorAction.Vault;

    private void Awake()
    {
        if (move == null)
            move = GetComponent<SurvivorMove>();

        if (interactor == null)
            interactor = GetComponent<SurvivorInteractor>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    [Server]
    public void SetAct(SurvivorAction act)
    {
        currentAction = act;
        ApplyState();
    }

    [Server]
    public void ClearAct(SurvivorAction act)
    {
        if (currentAction != act)
            return;

        currentAction = SurvivorAction.None;
        ApplyState();
    }

    [Server]
    public void SetHeal(bool value)
    {
        isBeingHealed = value;
        ApplyUse();
    }

    [Server]
    public void SetInteract(bool value)
    {
        isDoingInteraction = value;
    }

    [Server]
    public void SetCam(bool value)
    {
        isCamSkill = value;
    }

    public bool CanCam()
    {
        SurvivorState state = GetComponent<SurvivorState>();
        if (state == null)
            return false;

        if (state.IsDead)
            return false;

        if (state.IsDowned)
            return false;

        // 감옥 안에서는 카메라 스킬 금지
        if (state.IsImprisoned)
            return false;

        if (isBeingHealed)
            return false;

        if (isDoingInteraction)
            return false;

        if (currentAction == SurvivorAction.DownHit)
            return false;

        if (currentAction == SurvivorAction.Stunned)
            return false;

        if (currentAction == SurvivorAction.Vault)
            return false;

        return true;
    }

    private void OnActChanged(SurvivorAction oldValue, SurvivorAction newValue)
    {
        ApplyState();
    }

    private void OnHealChanged(bool oldValue, bool newValue)
    {
        ApplyUse();
    }

    private void ApplyState()
    {
        ApplyLock();
        ApplyUse();
    }

    // 다운피격, 스턴일 때만 이동 잠금
    private void ApplyLock()
    {
        if (move == null)
            return;

        bool lockMove = false;

        if (currentAction == SurvivorAction.DownHit)
            lockMove = true;

        if (currentAction == SurvivorAction.Stunned)
            lockMove = true;

        move.SetMoveLock(lockMove);

        if (lockMove)
            move.StopAnimation();
    }

    // 상태에 따라 SurvivorInteractor 자체를 켜고 끈다.
    // 중요:
    // 감옥 상태는 여기서 막지 않는다.
    // 감옥 안 상호작용은 SurvivorInteractor.CanUseThis()가
    // "자기 감옥만 허용"하도록 이미 필터링하고 있기 때문이다.
    public void ApplyUse()
    {
        if (interactor == null)
            return;

        SurvivorState state = GetComponent<SurvivorState>();
        if (state == null)
            return;

        bool canUse = true;

        if (state.IsDowned)
            canUse = false;

        if (state.IsDead)
            canUse = false;

        // 감옥 상태는 Interactor를 끄지 않는다.
        // 그래야 안에서 탈출 시도 입력을 받을 수 있다.

        if (isBeingHealed)
            canUse = false;

        if (currentAction == SurvivorAction.DownHit)
            canUse = false;

        if (currentAction == SurvivorAction.Stunned)
            canUse = false;

        interactor.enabled = canUse;
    }

    [Server]
    public IEnumerator DownHitRoutine(float time)
    {
        currentAction = SurvivorAction.DownHit;
        isCamSkill = false;

        if (interactor != null)
            interactor.ForceStopInteract();

        RpcDownHit();

        yield return new WaitForSeconds(time);

        if (currentAction == SurvivorAction.DownHit)
        {
            currentAction = SurvivorAction.None;
            ApplyState();
        }
    }

    [ClientRpc]
    private void RpcDownHit()
    {
        if (interactor != null)
            interactor.ForceStopInteract();

        if (move != null)
        {
            move.SetMoveLock(true);
            move.StopAnimation();
            move.SetCamAnim(false);
        }

        if (animator != null)
            animator.SetTrigger("DownHit");
    }

    [Server]
    public IEnumerator StunRoutine(float time)
    {
        currentAction = SurvivorAction.Stunned;
        isCamSkill = false;
        ApplyState();

        yield return new WaitForSeconds(time);

        if (currentAction == SurvivorAction.Stunned)
        {
            currentAction = SurvivorAction.None;
            ApplyState();
        }
    }

    [Server]
    public void ForceResetActionServer()
    {
        currentAction = SurvivorAction.None;
    }
}