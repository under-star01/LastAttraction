using System.Collections;
using Mirror;
using UnityEngine;

// 생존자의 "행동 상태" 전용
public enum SurvivorAction
{
    None,
    DownHit,
    Healing,
    Interacting,
    Stunned
}

public class SurvivorActionState : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private SurvivorMove move;
    [SerializeField] private SurvivorInteractor interactor;
    [SerializeField] private Animator animator;

    [SyncVar(hook = nameof(OnActionChanged))]
    private SurvivorAction currentAction = SurvivorAction.None;

    [SyncVar(hook = nameof(OnBeingHealedChanged))]
    private bool isBeingHealed;

    [SyncVar]
    private bool isDoingInteraction;

    public SurvivorAction CurrentAction => currentAction;

    // 다운 피격중 / 스턴중이면 강한 행동 제한 상태
    public bool IsBusy =>
        currentAction == SurvivorAction.DownHit ||
        currentAction == SurvivorAction.Stunned;

    public bool IsBeingHealed => isBeingHealed;
    public bool IsDoingInteraction => isDoingInteraction;

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
    public void SetAction(SurvivorAction newAction)
    {
        currentAction = newAction;
    }

    [Server]
    public void ClearAction(SurvivorAction action)
    {
        if (currentAction == action)
            currentAction = SurvivorAction.None;
    }

    // 힐받는 중 여부 저장
    [Server]
    public void SetBeingHealedServer(bool value)
    {
        isBeingHealed = value;
        ApplyInteractEnabled();
    }

    // Hold 상호작용 중 여부 저장
    [Server]
    public void SetDoingInteractionServer(bool value)
    {
        isDoingInteraction = value;
    }

    private void OnActionChanged(SurvivorAction oldValue, SurvivorAction newValue)
    {
        ApplyMoveLock();
        ApplyInteractEnabled();
    }

    private void OnBeingHealedChanged(bool oldValue, bool newValue)
    {
        ApplyInteractEnabled();
    }

    // 행동 상태에 따라 이동 잠금 적용
    private void ApplyMoveLock()
    {
        if (move == null)
            return;

        bool shouldLock = false;

        if (currentAction == SurvivorAction.DownHit)
            shouldLock = true;

        if (currentAction == SurvivorAction.Stunned)
            shouldLock = true;

        move.SetMoveLock(shouldLock);

        if (shouldLock)
            move.StopAnimation();
    }

    // 행동 상태 + 몸 상태를 합쳐서 상호작용 가능 여부 적용
    [Server]
    public void ApplyInteractEnabled()
    {
        ApplyInteractEnabledInternal();
    }

    private void ApplyInteractEnabledInternal()
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

    // 다운 피격 연출
    [Server]
    public IEnumerator DownHitRoutine(float duration)
    {
        currentAction = SurvivorAction.DownHit;

        if (interactor != null)
            interactor.ForceStopInteract();

        RpcPlayDownHit();

        yield return new WaitForSeconds(duration);

        if (currentAction == SurvivorAction.DownHit)
            currentAction = SurvivorAction.None;
    }

    [ClientRpc]
    private void RpcPlayDownHit()
    {
        if (interactor != null)
            interactor.ForceStopInteract();

        if (move != null)
        {
            move.SetMoveLock(true);
            move.StopAnimation();
        }

        if (animator != null)
            animator.SetTrigger("DownHit");
    }

    // 함정 스턴 같은 행동 제한
    [Server]
    public IEnumerator StunRoutine(float duration)
    {
        currentAction = SurvivorAction.Stunned;

        yield return new WaitForSeconds(duration);

        if (currentAction == SurvivorAction.Stunned)
            currentAction = SurvivorAction.None;
    }

    [Server]
    public void ForceResetActionServer()
    {
        // 피격 등으로 인해 현재 진행 중인 강제 행동(트랩 등)을 서버에서 즉시 종료시킴
        currentAction = SurvivorAction.None;
    }
}