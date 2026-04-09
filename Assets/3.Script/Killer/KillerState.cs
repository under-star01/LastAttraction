using UnityEngine;
using Mirror;

// 살인마의 상태 정의
public enum KillerCondition { Idle, Lunging, Recovering, Hit, Vaulting, Breaking, Carrying }

public class KillerState : NetworkBehaviour
{
    private NetworkAnimator networkAnimator;
    private Animator animator;
    private KillerMove move;

    [Header("Sync Variables")]
    // [SyncVar]는 데이터 동기화용으로만 사용합니다.
    [SyncVar]
    private KillerCondition currentCondition = KillerCondition.Idle;

    // --- [외부 참조용 프로퍼티] ---
    public KillerCondition CurrentCondition => currentCondition;

    // 이동 가능 여부
    public bool CanMove =>
        currentCondition == KillerCondition.Idle ||
        currentCondition == KillerCondition.Lunging ||
        currentCondition == KillerCondition.Recovering;

    // 시야 회전 가능 여부
    public bool CanLook =>
        currentCondition != KillerCondition.Hit &&
        currentCondition != KillerCondition.Vaulting &&
        currentCondition != KillerCondition.Breaking;

    // 공격 시작 가능 여부
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

        // 1. 상태 데이터 변경 (클라이언트들에게 전달됨)
        currentCondition = newState;

        // 2. [서버/호스트 애니메이션 처리]
        // 서버에서 직접 애니메이션을 실행합니다. 
        // 서버(호스트)의 Scene/Game 뷰에 애니메이션이 출력되고, 
        // NetworkAnimator를 통해 모든 클라이언트에게 신호가 전파됩니다.
        ExecuteAnimationTrigger(newState);
    }

    // 애니메이션 트리거 실행 (서버와 내 화면을 동시에 챙깁니다)
    private void ExecuteAnimationTrigger(KillerCondition condition)
    {
        if (animator == null || networkAnimator == null) return;

        switch (condition)
        {
            case KillerCondition.Lunging:
                // 내 애니메이터에 직접 명령 (서버/호스트 화면 출력)
                animator.SetTrigger("Attack");
                // 네트워크를 통해 다른 사람들에게 전파
                networkAnimator.SetTrigger("Attack");
                break;

            case KillerCondition.Hit:
                animator.SetTrigger("Hit");
                networkAnimator.SetTrigger("Hit");
                break;

            case KillerCondition.Breaking:
                animator.SetTrigger("Break");
                networkAnimator.SetTrigger("Break");
                break;
        }
    }

    // --- [상태 관리] 매 프레임 변하는 파라미터 업데이트 ---
    private void Update()
    {
        if (animator == null) return;

        // [중요] 'Busy' 상태 체크
        // 런지(Lunging)를 포함시켜야 공격 트리거가 이동 파라미터(Speed)에 의해 씹히지 않습니다.
        bool isBusy = currentCondition == KillerCondition.Recovering ||
                      currentCondition == KillerCondition.Hit ||
                      currentCondition == KillerCondition.Breaking ||
                      currentCondition == KillerCondition.Lunging;

        if (!isBusy)
        {
            // 평상시: 이동 속도와 일반 상태 갱신
            // move.SyncedMoveSpeed가 KillerMove에 선언되어 있어야 합니다.
            animator.SetFloat("Speed", move.SyncedMoveSpeed, 0.1f, Time.deltaTime);
            animator.SetBool("isLunging", false);
        }
        else
        {
            // Busy 상태: 발 미끄러짐 방지를 위해 Speed는 0으로 고정
            animator.SetFloat("Speed", 0f, 0.1f, Time.deltaTime);

            // 런지 중일 때만 애니메이터의 isLunging(달리기 모션)을 활성화
            if (currentCondition == KillerCondition.Lunging)
            {
                animator.SetBool("isLunging", true);
            }
            else
            {
                animator.SetBool("isLunging", false);
            }
        }
    }
}