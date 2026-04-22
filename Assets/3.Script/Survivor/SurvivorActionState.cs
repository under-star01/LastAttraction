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
    // Vault는 스킬 금지용으로도 같이 사용한다.
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

    // 서버에서 현재 행동 상태를 바꾼다.
    [Server]
    public void SetAct(SurvivorAction act)
    {
        currentAction = act;
        ApplyState();
    }

    // 특정 행동 상태를 해제한다.
    // 다른 상태로 이미 바뀌어 있으면 건드리지 않는다.
    [Server]
    public void ClearAct(SurvivorAction act)
    {
        if (currentAction != act)
            return;

        currentAction = SurvivorAction.None;
        ApplyState();
    }

    // 힐받는 중 여부 저장
    [Server]
    public void SetHeal(bool value)
    {
        isBeingHealed = value;
        ApplyUse();
    }

    // Hold 상호작용 중 여부 저장
    [Server]
    public void SetInteract(bool value)
    {
        isDoingInteraction = value;
    }

    // 카메라 스킬 사용 중 여부 저장
    [Server]
    public void SetCam(bool value)
    {
        isCamSkill = value;
    }

    // 지금 카메라 스킬 사용 가능한지 검사
    // 스킬 스크립트 쪽에서 이 함수만 보고 판단하게 한다.
    public bool CanCam()
    {
        SurvivorState state = GetComponent<SurvivorState>();
        if (state == null)
            return false;

        if (state.IsDead)
            return false;

        if (state.IsDowned)
            return false;

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

    // 상태가 바뀌면 이동 잠금과 상호작용 허용 여부를 갱신
    private void ApplyState()
    {
        ApplyLock();
        ApplyUse();
    }

    // 다운피격, 스턴일 때만 이동 잠금
    // Vault는 이동을 따로 막는 게 아니라
    // Window / Pallet 루틴에서 직접 컨트롤하므로 여기서는 잠그지 않는다.
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
    // 이렇게 하면 상호작용 입력이 자연스럽게 막힌다.
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

        if (state.IsImprisoned)
            canUse = false;

        if (isBeingHealed)
            canUse = false;

        if (currentAction == SurvivorAction.DownHit)
            canUse = false;

        if (currentAction == SurvivorAction.Stunned)
            canUse = false;

        interactor.enabled = canUse;
    }

    // 다운 피격 연출
    // 이때는 상호작용, 스킬, 일반 행동을 끊어준다.
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

            // 스킬 애니메이션도 강제로 끈다.
            move.SetCamAnim(false);
        }

        if (animator != null)
            animator.SetTrigger("DownHit");
    }

    // 스턴 연출
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
        // 피격 등으로 인해 현재 진행 중인 강제 행동(트랩 등)을 서버에서 즉시 종료시킴
        currentAction = SurvivorAction.None;
    }

    [Server]
    public void ForceResetActionServer()
    {
        // 피격 등으로 인해 현재 진행 중인 강제 행동(트랩 등)을 서버에서 즉시 종료시킴
        currentAction = SurvivorAction.None;
    }
}