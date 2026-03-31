using UnityEngine;

public enum SurvivorCondition
{
    Healthy,   // 정상
    Injured,   // 부상
    Downed     // 쓰러짐
}

public class SurvivorState : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private Animator animator;

    [Header("디버그")]
    [SerializeField] private SurvivorCondition debugCondition = SurvivorCondition.Healthy;

    private SurvivorMove move;

    public SurvivorCondition CurrentCondition { get; private set; } = SurvivorCondition.Healthy;

    public bool IsHealthy => CurrentCondition == SurvivorCondition.Healthy;
    public bool IsInjured => CurrentCondition == SurvivorCondition.Injured;
    public bool IsDowned => CurrentCondition == SurvivorCondition.Downed;

    private void Awake()
    {
        move = GetComponent<SurvivorMove>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        SetCondition(debugCondition);
    }

    private void Update()
    {
        if (CurrentCondition != debugCondition)
        {
            SetCondition(debugCondition);
        }
    }

    public void TakeHit()
    {
        if (CurrentCondition == SurvivorCondition.Healthy)
            SetCondition(SurvivorCondition.Injured);
        else if (CurrentCondition == SurvivorCondition.Injured)
            SetCondition(SurvivorCondition.Downed);

        debugCondition = CurrentCondition;
    }

    public void HealToHealthy()
    {
        SetCondition(SurvivorCondition.Healthy);
        debugCondition = CurrentCondition;
    }

    public void RecoverToInjured()
    {
        SetCondition(SurvivorCondition.Injured);
        debugCondition = CurrentCondition;
    }

    private void SetCondition(SurvivorCondition newCondition)
    {
        CurrentCondition = newCondition;

        if (move != null)
        {
            bool shouldLockMove = (CurrentCondition == SurvivorCondition.Downed);
            move.SetMoveLock(shouldLockMove);
        }

        UpdateAnimator();
    }

    private void UpdateAnimator()
    {
        if (animator == null)
            return;

        animator.SetInteger("Condition", (int)CurrentCondition);
    }
}