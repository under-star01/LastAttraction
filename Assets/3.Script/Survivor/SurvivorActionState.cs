using System.Collections;
using Mirror;
using UnityEngine;

// 생존자의 현재 행동 상태
public enum SurvivorAction
{
    None,
    Hit,
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

    [SyncVar(hook = nameof(OnActChanged))]
    private SurvivorAction currentAction = SurvivorAction.None;

    [SyncVar(hook = nameof(OnHealChanged))]
    private bool isBeingHealed;

    [SyncVar]
    private bool isDoingInteraction;

    [SyncVar]
    private bool isCamSkill;

    public SurvivorAction CurrentAction => currentAction;
    public bool IsBeingHealed => isBeingHealed;
    public bool IsDoingInteraction => isDoingInteraction;
    public bool IsCamSkill => isCamSkill;
    public bool IsVault => currentAction == SurvivorAction.Vault;

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

    // DownHit, Stunned 상태에서는 이동을 막는다.
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

    // 상태에 따라 SurvivorInteractor 사용 가능 여부를 바꾼다.
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

        if (isBeingHealed)
            canUse = false;

        if (currentAction == SurvivorAction.DownHit)
            canUse = false;

        if (currentAction == SurvivorAction.Stunned)
            canUse = false;

        interactor.enabled = canUse;
    }

    // 일반 피격 연출
    [Server]
    public IEnumerator HitRoutine(float time)
    {
        if (time <= 0f)
            yield break;

        if (currentAction == SurvivorAction.DownHit)
            yield break;

        if (currentAction == SurvivorAction.Stunned)
            yield break;

        if (currentAction == SurvivorAction.Vault)
            yield break;

        currentAction = SurvivorAction.Hit;
        isCamSkill = false;
        isDoingInteraction = false;

        // 서버 루틴이므로 소유 클라이언트에게 실제 상호작용 종료를 요청한다.
        if (interactor != null)
            interactor.ForceStopInteractFromServer();

        if (move != null)
        {
            move.SetCamAnim(false);
            move.SetSearching(false);
            move.SetVaulting(false);
            move.SetStunned(false);

            move.PlayAnimation("Hit");
        }
        else if (animator != null)
        {
            animator.SetBool("IsCameraSkill", false);
            animator.SetBool("IsSearching", false);
            animator.SetBool("IsVaulting", false);
            animator.SetBool("IsStunned", false);
            animator.SetTrigger("Hit");
        }

        ApplyState();

        yield return new WaitForSeconds(time);

        if (currentAction == SurvivorAction.Hit)
        {
            currentAction = SurvivorAction.None;
            ApplyState();
        }
    }

    // 다운 피격 연출
    [Server]
    public IEnumerator DownHitRoutine(float time)
    {
        currentAction = SurvivorAction.DownHit;
        isCamSkill = false;
        isDoingInteraction = false;

        if (move != null)
            move.SetStunned(false);

        // 서버 루틴이므로 소유 클라이언트에게 실제 상호작용 종료를 요청한다.
        if (interactor != null)
            interactor.ForceStopInteractFromServer();

        ApplyState();

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
        // 이 함수는 클라이언트에서 실행되므로 기존 ForceStopInteract를 직접 호출해도 된다.
        if (interactor != null)
            interactor.ForceStopInteract();

        if (move != null)
        {
            move.SetMoveLock(true);
            move.StopAnimation();

            move.SetCamAnim(false);
            move.SetSearching(false);
            move.SetVaulting(false);
            move.SetStunned(false);
        }

        if (animator != null)
            animator.SetTrigger("DownHit");
    }

    // 트랩 / QTE 실패 등에서 공통으로 사용하는 스턴 루틴
    [Server]
    public IEnumerator StunRoutine(float time)
    {
        if (time <= 0f)
            yield break;

        if (currentAction == SurvivorAction.DownHit)
            yield break;

        if (currentAction == SurvivorAction.Stunned)
            yield break;

        currentAction = SurvivorAction.Stunned;
        isCamSkill = false;
        isDoingInteraction = false;

        // 서버 루틴이므로 소유 클라이언트에게 실제 상호작용 종료를 요청한다.
        if (interactor != null)
            interactor.ForceStopInteractFromServer();

        if (move != null)
        {
            move.SetCamAnim(false);
            move.SetSearching(false);
            move.SetVaulting(false);

            move.StopAnimation();
            move.SetStunned(true);
            move.PlayAnimation("Stun");
        }
        else if (animator != null)
        {
            animator.SetBool("IsStunned", true);
            animator.SetTrigger("Stun");
        }

        ApplyState();

        yield return new WaitForSeconds(time);

        if (currentAction == SurvivorAction.Stunned)
        {
            currentAction = SurvivorAction.None;

            if (move != null)
                move.SetStunned(false);
            else if (animator != null)
                animator.SetBool("IsStunned", false);

            ApplyState();
        }
    }

    // 다른 상태에서 강제로 행동 상태를 초기화할 때 사용
    [Server]
    public void ForceResetActionServer()
    {
        currentAction = SurvivorAction.None;
        isDoingInteraction = false;
        isCamSkill = false;

        if (move != null)
            move.SetStunned(false);
        else if (animator != null)
            animator.SetBool("IsStunned", false);

        ApplyState();
    }
}