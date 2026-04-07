using UnityEngine;

public enum KillerCondition { Idle, Lunging, Recovering, Hit, Vaulting, Breaking, Carrying }

public class KillerState : MonoBehaviour
{
    // 현재 상태를 외부에서 읽을 수만 있게 설정
    public KillerCondition CurrentCondition { get; private set; } = KillerCondition.Idle;

    // --- [KillerMove에서 사용하는 프로퍼티] ---
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
    public void ChangeState(KillerCondition newState)
    {
        if (CurrentCondition == newState) return;

        CurrentCondition = newState;
        Debug.Log($"[KillerState] 상태 변경: {newState}");
    }
}