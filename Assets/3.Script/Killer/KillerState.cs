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

    // 런지 중이거나 평상시, 후딜레이일 때만 이동 가능
    public bool CanMove =>
        currentCondition == KillerCondition.Idle ||
        currentCondition == KillerCondition.Lunging ||
        currentCondition == KillerCondition.Recovering;

    // 스턴, 상호작용 중이 아닐 때만 시야 회전 가능
    public bool CanLook =>
        currentCondition != KillerCondition.Hit &&
        currentCondition != KillerCondition.Vaulting &&
        currentCondition != KillerCondition.Breaking;

    // 평상시일 때만 공격/상호작용 시작 가능
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

    // --- [1. 사건 관리] 상태가 바뀌는 "순간" 트리거 애니메이션 실행 [cite: 2026-04-06]
    private void OnConditionChanged(KillerCondition oldState, KillerCondition newState)
    {
        if (!isServer) return;

        if (networkAnimator == null) networkAnimator = GetComponent<NetworkAnimator>();

        switch (newState)
        {
            case KillerCondition.Lunging:
                networkAnimator.SetTrigger("Attack"); // 공격 시작 애니메이션
                break;
            case KillerCondition.Hit:
                networkAnimator.SetTrigger("Hit");    // 피격 애니메이션
                break;
            case KillerCondition.Breaking:
                networkAnimator.SetTrigger("Break");  // 판자 파괴 애니메이션
                break;
        }
    }

    // --- [2. 상태 관리] 매 프레임 변하는 애니메이션 파라미터 업데이트 [cite: 2026-04-06]
    private void Update()
    {
        if (animator == null) return;

        // 애니메이션이 섞이면 안 되는 '바쁜' 상태 체크 [cite: 2026-04-06]
        bool isBusy = currentCondition == KillerCondition.Recovering ||
                      currentCondition == KillerCondition.Hit ||
                      currentCondition == KillerCondition.Breaking;

        if (!isBusy)
        {
            // KillerMove의 동기화된 속도 데이터를 가져와 적용
            // 프로퍼티 이름이 SyncedMoveSpeed인지 확인해 보세요.
            animator.SetFloat("Speed", move.SyncedMoveSpeed, 0.1f, Time.deltaTime);
            animator.SetBool("isLunging", currentCondition == KillerCondition.Lunging);
        }
        else
        {
            // 상호작용 중에는 발이 미끄러지지 않게 속도 0 고정 [cite: 2026-04-06]
            animator.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);
            animator.SetBool("isLunging", false);
        }
    }
}