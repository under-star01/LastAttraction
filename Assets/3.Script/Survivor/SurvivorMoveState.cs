using Mirror;
using UnityEngine;

// 생존자의 "이동 상태" 전용
public enum SurvivorLocomotionState
{
    Idle,
    Walk,
    Run,
    Crouch,
    Crawl
}

public class SurvivorMoveState : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Animator animator;

    [SyncVar(hook = nameof(OnMoveStateChanged))]
    private SurvivorLocomotionState currentMoveState = SurvivorLocomotionState.Idle;

    [SyncVar(hook = nameof(OnIsMovingChanged))]
    private bool isMoving;

    public SurvivorLocomotionState CurrentMoveState => currentMoveState;
    public bool IsMoving => isMoving;

    public bool IsIdle => currentMoveState == SurvivorLocomotionState.Idle;
    public bool IsWalking => currentMoveState == SurvivorLocomotionState.Walk;
    public bool IsRunning => currentMoveState == SurvivorLocomotionState.Run;
    public bool IsCrouching => currentMoveState == SurvivorLocomotionState.Crouch;
    public bool IsCrawling => currentMoveState == SurvivorLocomotionState.Crawl;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    [Server]
    public void SetMoveState(SurvivorLocomotionState newState, bool moving)
    {
        currentMoveState = newState;
        isMoving = moving;
        ApplyAnimator();
    }

    private void OnMoveStateChanged(SurvivorLocomotionState oldValue, SurvivorLocomotionState newValue)
    {
        ApplyAnimator();
    }

    private void OnIsMovingChanged(bool oldValue, bool newValue)
    {
        ApplyAnimator();
    }

    // 이동 상태를 예전 Animator 파라미터 방식으로 반영
    private void ApplyAnimator()
    {
        if (animator == null)
            return;

        float moveSpeed = 0f;
        bool isCrouching = false;
        bool isDowned = false;

        if (currentMoveState == SurvivorLocomotionState.Walk)
        {
            moveSpeed = 0.5f;
        }
        else if (currentMoveState == SurvivorLocomotionState.Run)
        {
            moveSpeed = 1f;
        }
        else if (currentMoveState == SurvivorLocomotionState.Crouch)
        {
            isCrouching = true;

            if (isMoving)
                moveSpeed = 0.25f;
            else
                moveSpeed = 0f;
        }
        else if (currentMoveState == SurvivorLocomotionState.Crawl)
        {
            isDowned = true;

            if (isMoving)
                moveSpeed = 0.2f;
            else
                moveSpeed = 0f;
        }

        animator.SetFloat("MoveSpeed", moveSpeed, 0.1f, Time.deltaTime);
        animator.SetBool("IsCrouching", isCrouching);
        animator.SetBool("IsDowned", isDowned);
    }
}