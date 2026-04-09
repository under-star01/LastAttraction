using UnityEngine;
using Mirror;

public enum KillerCondition { Idle, Lunging, Recovering, Hit, Vaulting, Breaking, Carrying }

public class KillerState : NetworkBehaviour
{
<<<<<<< HEAD
    private NetworkAnimator networkAnimator;
<<<<<<< HEAD
<<<<<<< HEAD
    private Animator animator;
    private KillerMove move;

    [Header("Sync Variables")]
=======
    // [SyncVar]를 붙여야 서버에서 바꾼 상태가 모든 클라이언트에게 전달됩니다.
>>>>>>> parent of f190c4c (0409_killer_server1)
=======
    // [SyncVar]를 붙여야 서버에서 바꾼 상태가 모든 클라이언트에게 전달됩니다.
>>>>>>> parent of 7a73d10 (0409_killer_server2)
=======
    // [SyncVar]를 붙여야 서버에서 바꾼 상태가 모든 클라이언트에게 전달됩니다.
>>>>>>> parent of 7a73d10 (0409_killer_server2)
    [SyncVar(hook = nameof(OnConditionChanged))]
    private KillerCondition currentCondition = KillerCondition.Idle;

    public KillerCondition CurrentCondition => currentCondition;
    // 런지 중이거나 평상시일 때만 이동 가능
    public bool CanMove => 
        CurrentCondition == KillerCondition.Idle || 
        CurrentCondition == KillerCondition.Lunging ||
        CurrentCondition == KillerCondition.Recovering;

<<<<<<< HEAD
<<<<<<< HEAD
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
=======
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
>>>>>>> parent of 7a73d10 (0409_killer_server2)

=======
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

>>>>>>> parent of 7a73d10 (0409_killer_server2)
    // 상태 변경 함수
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

<<<<<<< HEAD
<<<<<<< HEAD
<<<<<<< HEAD
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
=======
=======
>>>>>>> parent of 7a73d10 (0409_killer_server2)
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
<<<<<<< HEAD
>>>>>>> parent of 7a73d10 (0409_killer_server2)
=======
>>>>>>> parent of 7a73d10 (0409_killer_server2)
                break;
        }
=======
    // 상태가 변했을 때 로그를 찍거나 특정 처리를 하고 싶다면 훅(Hook)을 사용합니다.
    private void OnConditionChanged(KillerCondition oldState, KillerCondition newState)
    {
        Debug.Log($"[KillerState] 상태 변경: {oldState} -> {newState}");
>>>>>>> parent of f190c4c (0409_killer_server1)
    }
<<<<<<< HEAD

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
        //if (isServer) return;

        switch (condition)
        {
            case KillerCondition.Lunging: animator.SetTrigger("Attack"); break;
            case KillerCondition.Hit: animator.SetTrigger("Hit"); break;
            case KillerCondition.Breaking: animator.SetTrigger("Break"); break;
        }
    }
<<<<<<< HEAD

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
=======
>>>>>>> parent of 7a73d10 (0409_killer_server2)
=======
>>>>>>> parent of 7a73d10 (0409_killer_server2)
}