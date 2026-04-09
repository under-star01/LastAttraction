using UnityEngine;
using Mirror;

public enum KillerCondition { Idle, Lunging, Recovering, Hit, Vaulting, Breaking, Carrying }

public class KillerState : NetworkBehaviour
{
    private NetworkAnimator networkAnimator;
    private Animator animator;
    private KillerMove move;

    [Header("Sync Variables")]
    [SyncVar(hook = nameof(OnConditionChanged))]
    private KillerCondition currentCondition = KillerCondition.Idle;

    // --- [외부 참조용 프로퍼티] ---
    public KillerCondition CurrentCondition => currentCondition;

    public bool CanMove =>
        currentCondition == KillerCondition.Idle ||
        currentCondition == KillerCondition.Lunging ||
        currentCondition == KillerCondition.Recovering;

    public bool CanLook =>
        currentCondition != KillerCondition.Hit &&
        currentCondition != KillerCondition.Vaulting &&
        currentCondition != KillerCondition.Breaking;

    public bool CanAttack => currentCondition == KillerCondition.Idle;
    public bool IsInAttackAnimation => currentCondition == KillerCondition.Recovering;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        networkAnimator = GetComponent<NetworkAnimator>();
        move = GetComponent<KillerMove>();
    }

    // --- [서버 전용 상태 변경 함수] ---
    [Server]
    public void ChangeState(KillerCondition newState)
    {
        if (currentCondition == newState) return;
        currentCondition = newState;
    }

    //// 트리거 전용 헬퍼 함수
    //private void TriggerAnimationEvent(KillerCondition condition)
    //{
    //    if (networkAnimator == null) networkAnimator = GetComponent<NetworkAnimator>();

    //    switch (condition)
    //    {
    //        case KillerCondition.Lunging:
    //            networkAnimator.SetTrigger("Attack");
    //            break;
    //        case KillerCondition.Hit:
    //            networkAnimator.SetTrigger("Hit");
    //            break;
    //        case KillerCondition.Breaking:
    //            networkAnimator.SetTrigger("Break");
    //            break;
    //    }
    //}

    // [핵심] SyncVar 훅은 모든 클라이언트(나 포함)에서 실행됩니다.
    private void OnConditionChanged(KillerCondition oldState, KillerCondition newState)
    {
        // 1. 내 화면(Local Player)에서 즉시 애니메이션을 틀어줍니다.
        // NetworkAnimator가 서버 신호를 가져오기 전에 로컬에서 먼저 반응하게 하여 '체감'을 높입니다. [cite: 2026-04-06]
        if (isLocalPlayer)
        {
            PlayTriggerAnimation(newState);
        }

        // 2. 서버일 때만 NetworkAnimator를 통해 '다른 생존자들'에게 애니메이션을 전파합니다.
        // 이렇게 하면 중복 실행 없이 전원 동기화가 가능합니다. [cite: 2026-04-06]
        if (isServer)
        {
            SyncTriggerToNetwork(newState);
        }
    }

    private void PlayTriggerAnimation(KillerCondition condition)
    {
        if (animator == null) return;
        switch (condition)
        {
            case KillerCondition.Lunging: animator.SetTrigger("Attack"); break;
            case KillerCondition.Hit: animator.SetTrigger("Hit"); break;
            case KillerCondition.Breaking: animator.SetTrigger("Break"); break;
        }
    }

    private void SyncTriggerToNetwork(KillerCondition condition)
    {
        if (networkAnimator == null) return;
        switch (condition)
        {
            case KillerCondition.Lunging: networkAnimator.SetTrigger("Attack"); break;
            case KillerCondition.Hit: networkAnimator.SetTrigger("Hit"); break;
            case KillerCondition.Breaking: networkAnimator.SetTrigger("Break"); break;
        }
    }

    // --- [상태 관리] 매 프레임 변하는 파라미터는 변수값에 따라 모든 클라이언트에서 업데이트 [cite: 2026-04-06]
    private void Update()
    {
        if (animator == null) return;

        // [중요] 런지(공격 중)도 Busy 상태에 넣어야 공격 트리거가 캔슬되지 않습니다. [cite: 2026-04-06]
        bool isBusy = currentCondition == KillerCondition.Recovering ||
                      currentCondition == KillerCondition.Hit ||
                      currentCondition == KillerCondition.Breaking ||
                      currentCondition == KillerCondition.Lunging;

        if (!isBusy)
        {
            animator.SetFloat("Speed", move.SyncedMoveSpeed, 0.1f, Time.deltaTime);
            animator.SetBool("isLunging", false);
        }
        else
        {
            // 공격 중일 때는 런지 달리기 애니메이션만 허용
            animator.SetBool("isLunging", currentCondition == KillerCondition.Lunging);
            animator.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);
        }
    }
}