using UnityEngine;
using Mirror;

public class KillerCombat : NetworkBehaviour
{
    [Header("Lunge Settings")]
    public float maxLungeDuration = 1.2f;
    public float hitFailPenalty = 2.0f;
    public float hitSuccessPenalty = 2.5f;
    public float wallHitPenalty = 3.0f;

    [Header("Hit Detection")]
    public Transform attackPoint;
    public float attackRadius = 1.0f;
    public LayerMask survivorLayer;
    public LayerMask obstacleLayer;

    [Header("Animation Settings")]
    public float baseAttackAnimationLength = 3.333f;

    private KillerInput input;
    private KillerState state;
    private Animator animator;
    private NetworkAnimator networkAnimator;

    // [클라이언트 전용] 로컬 상태 관리
    private float currentLungeTime;
    private float currentPenaltyTime;
    private bool hasHitTarget;
    private bool isEndingAttack;
    private uint hitSurvivorNetId;

    // [서버 전용] 패널티 타이머 (서버에서만 시간을 계산) [cite: 2026-04-06]
    private float serverPenaltyTimer;

    void Awake()
    {
        input = GetComponent<KillerInput>();
        state = GetComponent<KillerState>();
        animator = GetComponentInChildren<Animator>();
        networkAnimator = GetComponent<NetworkAnimator>();
    }

    void Update()
    {
        // 1. 애니메이션 동기화 (모든 클라이언트 공통) [cite: 2026-04-06]
        UpdateAnimationState();

        // 2. 서버 로직: 서버가 직접 시간을 재고 상태를 Idle로 변경 [cite: 2026-04-06]
        if (isServer && state.CurrentCondition == KillerCondition.Recovering)
        {
            serverPenaltyTimer -= Time.deltaTime;
            if (serverPenaltyTimer <= 0f)
            {
                state.ChangeState(KillerCondition.Idle);
            }
        }

        // 3. 로컬 플레이어 로직: 입력 및 런지 판정
        if (!isLocalPlayer) return;

        // 로컬 패널티 타이머 (UI 연동이나 내부 플래그 초기화용)
        if (state.CurrentCondition == KillerCondition.Recovering)
        {
            currentPenaltyTime -= Time.deltaTime;
            if (currentPenaltyTime <= 0f) isEndingAttack = false;
            return;
        }

        if (state.CanAttack || state.CurrentCondition == KillerCondition.Lunging)
        {
            HandleAttackInput();
        }
    }

    private void UpdateAnimationState()
    {
        if (animator == null) return;

        bool isBusy = state.CurrentCondition == KillerCondition.Recovering ||
                      state.CurrentCondition == KillerCondition.Hit ||
                      state.CurrentCondition == KillerCondition.Breaking;

        // 후딜레이나 피격 중에는 이동 애니메이션 파라미터를 갱신하지 않음 [cite: 2026-04-06]
        if (!isBusy)
        {
            animator.SetBool("isLunging", state.CurrentCondition == KillerCondition.Lunging);
        }
    }

    private void HandleAttackInput()
    {
        if (input.IsAttackPressed)
        {
            if (state.CurrentCondition != KillerCondition.Lunging)
            {
                hasHitTarget = false;
                currentLungeTime = 0f;
                hitSurvivorNetId = 0;
                isEndingAttack = false;
                CmdStartLunge();
            }

            if (isEndingAttack) return;

            currentLungeTime += Time.deltaTime;
            currentLungeTime = Mathf.Clamp(currentLungeTime, 0.1f, maxLungeDuration);

            CheckHitDetection();

            if (currentLungeTime >= maxLungeDuration || hasHitTarget)
            {
                isEndingAttack = true;
                CmdEndLunge(currentLungeTime, hasHitTarget, hitSurvivorNetId);
            }
        }
        else if (state.CurrentCondition == KillerCondition.Lunging)
        {
            if (isEndingAttack) return;
            isEndingAttack = true;
            CmdEndLunge(currentLungeTime, hasHitTarget, hitSurvivorNetId);
        }
    }

    private void CheckHitDetection()
    {
        if (hasHitTarget || attackPoint == null) return;

        if (Physics.CheckSphere(attackPoint.position, attackRadius * 0.5f, obstacleLayer))
        {
            hasHitTarget = true;
            hitSurvivorNetId = 0;
            return;
        }

        Collider[] hitSurvivors = Physics.OverlapSphere(attackPoint.position, attackRadius, survivorLayer);
        if (hitSurvivors.Length > 0)
        {
            foreach (Collider hit in hitSurvivors)
            {
                if (hit == null) continue;
                NetworkIdentity identity = hit.GetComponentInParent<NetworkIdentity>();
                if (identity != null)
                {
                    hasHitTarget = true;
                    hitSurvivorNetId = identity.netId;
                    return;
                }
            }
        }
    }

    [Command]
    private void CmdStartLunge()
    {
        state.ChangeState(KillerCondition.Lunging);
        if (networkAnimator != null) networkAnimator.SetTrigger("Attack");
    }

    [Command]
    private void CmdEndLunge(float lungeTime, bool isHit, uint survivorNetId)
    {
        if (state.CurrentCondition == KillerCondition.Recovering) return;

        state.ChangeState(KillerCondition.Recovering);

        // [핵심] 서버에서 페널티 시간을 직접 계산하고 타이머를 시작합니다. [cite: 2026-04-06]
        float finalPenalty;
        if (isHit)
        {
            finalPenalty = (survivorNetId != 0) ? hitSuccessPenalty : wallHitPenalty;
        }
        else
        {
            finalPenalty = Mathf.Max(1.2f, lungeTime * hitFailPenalty);
        }

        serverPenaltyTimer = finalPenalty;

        // 생존자 타격 적용
        if (isHit && survivorNetId != 0)
        {
            if (NetworkServer.spawned.TryGetValue(survivorNetId, out NetworkIdentity identity))
            {
                SurvivorState survivorState = identity.GetComponentInParent<SurvivorState>();
                if (survivorState != null) survivorState.TakeHit();
            }
        }

        // 애니메이션 속도 동기화
        float animSpeed = baseAttackAnimationLength / finalPenalty;
        SyncAttackEffect(animSpeed);
    }

    [ClientRpc]
    private void SyncAttackEffect(float speed)
    {
        if (animator != null)
        {
            animator.SetFloat("AttackSpeed", Mathf.Clamp(speed, 1.0f, 3.0f));
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRadius);
    }
}