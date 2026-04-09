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

        // 1. [해결방법 적용] 서버에서 상태가 바뀌는 즉시 트리거를 당깁니다. [cite: 2026-04-06]
        // 서버가 여기서 한 번만 실행하면 Mirror가 모든 클라이언트에게 전파합니다. [cite: 2026-04-06]
        TriggerAnimationEvent(newState);

        // 2. 상태를 변경합니다. (이후 SyncVar 훅을 통해 클라이언트들의 Update가 반응함)
        currentCondition = newState;
    }

    // 트리거 전용 헬퍼 함수
    private void TriggerAnimationEvent(KillerCondition condition)
    {
        if (networkAnimator == null) networkAnimator = GetComponent<NetworkAnimator>();

        switch (condition)
        {
            case KillerCondition.Lunging:
                networkAnimator.SetTrigger("Attack");
                break;
            case KillerCondition.Hit:
                networkAnimator.SetTrigger("Hit");
                break;
            case KillerCondition.Breaking:
                networkAnimator.SetTrigger("Break");
                break;
        }
    }

    // SyncVar 훅은 이제 시각적 보정이나 로그용으로만 사용합니다. [cite: 2026-04-06]
    private void OnConditionChanged(KillerCondition oldState, KillerCondition newState)
    {
        // 트리거 로직을 ChangeState로 옮겼으므로 여기는 비워두거나 
        // 클라이언트 전용 효과(사운드 등)를 넣을 때 사용하세요. [cite: 2026-04-07]
    }

    [ClientRpc]
    private void RpcPlayLocalAnimation(KillerCondition condition)
    {
        // 서버에서 이미 실행했으므로 클라이언트에서만 실행 (권한 에러 방지용 일반 animator 사용) [cite: 2026-04-06]
        if (isServer) return;

        switch (condition)
        {
            case KillerCondition.Lunging: animator.SetTrigger("Attack"); break;
            case KillerCondition.Hit: animator.SetTrigger("Hit"); break;
            case KillerCondition.Breaking: animator.SetTrigger("Break"); break;
        }
    }

    // --- [상태 관리] 매 프레임 변하는 파라미터는 변수값에 따라 모든 클라이언트에서 업데이트 [cite: 2026-04-06]
    private void Update()
    {
        if (animator == null) return;

        bool isBusy = currentCondition == KillerCondition.Recovering ||
                      currentCondition == KillerCondition.Hit ||
                      currentCondition == KillerCondition.Breaking;

        if (!isBusy)
        {
            // KillerMove의 공개 프로퍼티 SyncedMoveSpeed를 참조합니다.
            animator.SetFloat("Speed", move.SyncedMoveSpeed, 0.1f, Time.deltaTime);
            animator.SetBool("isLunging", currentCondition == KillerCondition.Lunging);
        }
        else
        {
            animator.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);
            animator.SetBool("isLunging", false);
        }
    }
}