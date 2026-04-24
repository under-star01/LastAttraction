using UnityEngine;
using Mirror;

public enum KillerCondition { Idle, Lunging, Recovering, Hit, Vaulting, Breaking, Incage, Planting }

public class KillerState : NetworkBehaviour
{
    private Animator animator;

    [Header("Sync Variables")]
    [SyncVar(hook = nameof(OnConditionChanged))]
    private KillerCondition currentCondition = KillerCondition.Idle;
    public KillerCondition CurrentCondition => currentCondition;

    [SyncVar(hook = nameof(OnRageChanged))]
    private bool isRaging = false;
    public bool IsRaging => isRaging;


    public bool CanMove =>
        currentCondition == KillerCondition.Idle ||
        currentCondition == KillerCondition.Lunging ||
        currentCondition == KillerCondition.Recovering ||
        currentCondition == KillerCondition.Planting;

    public bool CanLook =>
        currentCondition != KillerCondition.Hit &&
        currentCondition != KillerCondition.Vaulting &&
        currentCondition != KillerCondition.Breaking &&
        currentCondition != KillerCondition.Incage;

    public bool CanAttack => currentCondition == KillerCondition.Idle;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        if (!isLocalPlayer) return;
        if (Input.GetKeyDown(KeyCode.Alpha1)) ActivateRage();
    }

    [Server]
    public void ChangeState(KillerCondition newState)
    {
        if (currentCondition == newState) return;
        currentCondition = newState;
    }

    private void OnConditionChanged(KillerCondition oldState, KillerCondition newState)
    {
        if (isServer && !isClient) return;

        if (isLocalPlayer)
        {
            // [동기화 해결] 서버가 상태를 바꿨을 때 실행되어야 하는 트리거들
            // 피격(Hit)이나 공격 후딜레이(Recovering) 시작 시 애니메이션을 재생합니다.
            if (newState == KillerCondition.Hit || newState == KillerCondition.Recovering || newState == KillerCondition.Incage)
                PlayTrigger(newState);
        }
        else
        {
            // 타인 화면에서는 모든 상태 변화에 대해 트리거를 시도합니다.
            PlayTrigger(newState);
        }
    }

    public void PlayTrigger(KillerCondition condition)
    {
        if (animator == null) return;

        switch (condition)
        {
            // 런지(Lunging)는 bool 값에 의한 'Run' 애니메이션이므로 트리거를 쓰지 않습니다.
            case KillerCondition.Recovering:
                // 이제 공격 후딜레이 상태가 될 때 실제 공격 휘두르기 애니메이션이 나옵니다.
                animator.SetTrigger("Attack");
                break;
            case KillerCondition.Hit: animator.SetTrigger("Hit"); break;
            case KillerCondition.Breaking: animator.SetTrigger("Break"); break;
            case KillerCondition.Vaulting: animator.SetTrigger("Vault"); break;
            case KillerCondition.Incage: animator.SetTrigger("Incage"); break;
        }
    }

    [Command]
    public void CmdChangeKillerState(KillerCondition newState)
    {
        // 클라이언트의 요청을 받아 서버에서 실제 상태를 변경합니다.
        ChangeState(newState);
    }

    [Server]
    public void ActivateRage()
    {
        if (isRaging) return;
        isRaging = true;
    }

    private void OnRageChanged(bool oldVal, bool newVal)
    {
        
    }
}