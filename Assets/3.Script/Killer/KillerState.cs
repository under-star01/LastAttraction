using UnityEngine;
using Mirror;

// 살인마 상태 정의
public enum KillerCondition { Idle, Lunging, Recovering, Hit, Vaulting, Breaking, Carrying }

public class KillerState : NetworkBehaviour
{
    private NetworkAnimator networkAnimator;
    private Animator animator;
    private KillerMove move;

    [Header("Sync Variables")]
    // [SyncVar] 뒤에 훅(hook)을 완전히 제거했습니다.
    [SyncVar]
    private KillerCondition currentCondition = KillerCondition.Idle;

    // --- [외부 참조용 프로퍼티] ---
    public KillerCondition CurrentCondition => currentCondition;

    // 이동 가능 상태: 평상시, 공격(런지) 중, 공격 후딜레이일 때
    public bool CanMove =>
        currentCondition == KillerCondition.Idle ||
        currentCondition == KillerCondition.Lunging ||
        currentCondition == KillerCondition.Recovering;

    // 시야 회전 가능 상태: 피격, 판자 파괴, 창틀 넘기 중이 아닐 때
    public bool CanLook =>
        currentCondition != KillerCondition.Hit &&
        currentCondition != KillerCondition.Vaulting &&
        currentCondition != KillerCondition.Breaking;

    // 공격 시작 가능 상태: 아무것도 안 하는 Idle 상태일 때만
    public bool CanAttack => currentCondition == KillerCondition.Idle;

    // 공격 애니메이션(후딜레이) 중인지 확인
    public bool IsInAttackAnimation => currentCondition == KillerCondition.Recovering;

    private void Awake()
    {
        // 컴포넌트 참조 초기화
        animator = GetComponentInChildren<Animator>();
        networkAnimator = GetComponent<NetworkAnimator>();
        move = GetComponent<KillerMove>();
    }

    // --- [서버 전용 상태 변경 함수] ---
    [Server]
    public void ChangeState(KillerCondition newState)
    {
        if (currentCondition == newState) return;

        // 1. 상태를 먼저 변경 (SyncVar로 데이터 동기화)
        currentCondition = newState;

        // 2. 해당 상태에 필요한 트리거 애니메이션 실행
        // 여기서 networkAnimator.SetTrigger를 하면 서버(나)와 모든 클라이언트에게 전파됩니다.
        ExecuteAnimationTrigger(newState);
    }

    // 트리거 실행 로직 (서버에서 호출하여 전 네트워크에 방송)
    private void ExecuteAnimationTrigger(KillerCondition condition)
    {
        if (networkAnimator == null) networkAnimator = GetComponent<NetworkAnimator>();
        if (animator == null) return;

        switch (condition)
        {
            case KillerCondition.Lunging:
                networkAnimator.SetTrigger("Attack"); // 공격 시작
                break;
            case KillerCondition.Hit:
                networkAnimator.SetTrigger("Hit");    // 피격(스턴)
                break;
            case KillerCondition.Breaking:
                networkAnimator.SetTrigger("Break");  // 판자 파괴
                break;
        }
    }

    // --- [상태 관리] 매 프레임 파라미터 업데이트 ---
    private void Update()
    {
        if (animator == null) return;

        // 'Busy' 상태 정의: 트리거 애니메이션이 재생 중일 때
        // 런지(Lunging)를 여기에 포함시켜야 공격 애니메이션이 이동 애니메이션에 의해 취소되지 않습니다.
        bool isBusy = currentCondition == KillerCondition.Recovering ||
                      currentCondition == KillerCondition.Hit ||
                      currentCondition == KillerCondition.Breaking ||
                      currentCondition == KillerCondition.Lunging;

        if (!isBusy)
        {
            // 평상시: 이동 애니메이션 (Speed) 업데이트
            // move.SyncedMoveSpeed는 KillerMove에서 추가한 public 프로퍼티입니다.
            animator.SetFloat("Speed", move.SyncedMoveSpeed, 0.1f, Time.deltaTime);

            // 바쁘지 않을 때는 런지 달리기(isLunging)를 무조건 끕니다.
            animator.SetBool("isLunging", false);
        }
        else
        {
            // 바쁜 상태일 때는 발이 미끄러지지 않게 Speed를 0으로 고정합니다.
            animator.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);

            // 런지 상태일 때만 애니메이터의 isLunging(런지 달리기 모션)을 켭니다.
            // 단, 애니메이터 설정에서 'Attack' 트리거가 'isLunging'보다 우선순위가 높아야 합니다.
            animator.SetBool("isLunging", currentCondition == KillerCondition.Lunging);
        }
    }
}