using UnityEngine;
using Mirror;

public enum KillerCondition { Idle, Lunging, Recovering, Hit, Vaulting, Breaking, Carrying }

public class KillerState : NetworkBehaviour
{
    private NetworkAnimator networkAnimator;
    // [SyncVar]를 붙여야 서버에서 바꾼 상태가 모든 클라이언트에게 전달됩니다.
    [SyncVar(hook = nameof(OnConditionChanged))]
    private KillerCondition currentCondition = KillerCondition.Idle;

    public KillerCondition CurrentCondition => currentCondition;
    // 런지 중이거나 평상시일 때만 이동 가능
    public bool CanMove => 
        CurrentCondition == KillerCondition.Idle || 
        CurrentCondition == KillerCondition.Lunging ||
        CurrentCondition == KillerCondition.Recovering;

    // 스턴 상태가 아닐 때만 마우스 회전(시야) 가능
    public bool CanLook => 
        CurrentCondition != KillerCondition.Hit &&
        CurrentCondition != KillerCondition.Vaulting &&
        CurrentCondition != KillerCondition.Breaking;

    // --- [KillerCombat에서 사용하는 프로퍼티] ---
    // 아무것도 안 하는 평상시에만 공격 시작 가능
    public bool CanAttack => CurrentCondition == KillerCondition.Idle;

    // 공격 후딜레이(Recovering) 상태인지 확인
    public bool IsInAttackAnimation => CurrentCondition == KillerCondition.Recovering;

    // 상태 변경 함수
    [Server]
    public void ChangeState(KillerCondition newState)
    {
        if (currentCondition == newState) return;
        currentCondition = newState;
    }

    private void OnConditionChanged(KillerCondition oldState, KillerCondition newState)
    {
        if (networkAnimator == null) networkAnimator = GetComponent<NetworkAnimator>();

        // 상태가 변하는 "그 순간"에만 트리거를 한 번 빵! 터뜨려줍니다. [cite: 2026-04-06]
        switch (newState)
        {
            case KillerCondition.Lunging:
                networkAnimator.SetTrigger("Attack"); // 공격 시작
                break;
            case KillerCondition.Hit:
                networkAnimator.SetTrigger("Hit");    // 피격
                break;
            case KillerCondition.Breaking:
                networkAnimator.SetTrigger("Break"); // 판자 파괴
                break;
        }
    }
}