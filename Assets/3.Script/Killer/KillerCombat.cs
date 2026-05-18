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
    public float baseAttackAnimationLength = 2.666f;

    [Header("오디오")]
    [SerializeField] private AudioKey weaponSwingSoundKey = AudioKey.KillerWeaponSwing; // 무기 휘두르는 소리
    [SerializeField] private AudioKey attackHitSoundKey = AudioKey.KillerAttackHit;     // 타격 성공 소리
    [SerializeField] private Vector3 weaponSwingSoundOffset = new Vector3(0f, 1.2f, 0f);
    [SerializeField] private Vector3 attackHitSoundOffset = new Vector3(0f, 1.0f, 0f);
    [SerializeField] private float weaponSwingMinInterval = 0.08f; // 애니메이션 이벤트 중복 방지

    private KillerInput input;
    private KillerState state;
    private KillerSkillUI killerSkillUI;
    private Animator animator;
    private TrapHandler trapHandler;

    private float currentLungeTime;
    private float currentPenaltyTime;
    private bool hasHitTarget;
    private uint hitSurvivorNetId;
    private bool isEndingAttack;

    private float lastWeaponSwingServerTime;

    private void Awake()
    {
        input = GetComponent<KillerInput>();
        state = GetComponent<KillerState>();
        animator = GetComponentInChildren<Animator>();
        trapHandler = GetComponent<TrapHandler>();
    }

    private void Update()
    {
        if (animator != null && state != null)
        {
            // 모든 클라이언트에서 현재 상태가 Lunging이면 Run 애니메이션을 튼다.
            animator.SetBool("isLunging", state.CurrentCondition == KillerCondition.Lunging);
        }

        if (!isLocalPlayer)
            return;

        if (state == null || input == null)
            return;

        // 트랩 설치 모드 중에는 공격 입력을 막는다.
        if ((trapHandler != null && trapHandler.IsBuildMode) || state.CurrentCondition == KillerCondition.Planting)
            return;

        if (state.CurrentCondition == KillerCondition.Recovering)
        {
            HandleRecovery();
            return;
        }

        if (state.CanAttack || state.CurrentCondition == KillerCondition.Lunging)
            HandleAttackInput();
    }

    private void BindUI()
    {
        if (killerSkillUI != null)
            return;

        if (InGameUIManager.Instance != null)
            killerSkillUI = InGameUIManager.Instance.GetKillerSkillUI();
    }

    private void HandleRecovery()
    {
        currentPenaltyTime -= Time.deltaTime;

        if (currentPenaltyTime <= 0f)
        {
            isEndingAttack = false;
            CmdResetToIdle();
        }
    }

    private void HandleAttackInput()
    {
        if (!isLocalPlayer)
            return;

        if (input == null || state == null)
            return;

        // 트랩 설치 모드에서는 공격 입력을 무시한다.
        if (trapHandler != null && trapHandler.IsBuildMode)
            return;

        if (input.IsAttackPressed)
        {
            if (state.CurrentCondition != KillerCondition.Lunging)
            {
                // 현재 공격 가능한 상태가 아니면 입력을 무시한다.
                if (!state.CanAttack)
                    return;

                // 공격 시작 값 초기화
                hasHitTarget = false;
                currentLungeTime = 0f;
                hitSurvivorNetId = 0;
                isEndingAttack = false;

                BindUI();

                if (killerSkillUI != null)
                    killerSkillUI.SetAttackUsing();

                CmdStartLunge();
            }

            if (isEndingAttack)
                return;

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
            if (isEndingAttack)
                return;

            isEndingAttack = true;
            CmdEndLunge(currentLungeTime, hasHitTarget, hitSurvivorNetId);
        }
    }

    private void CheckHitDetection()
    {
        if (hasHitTarget || attackPoint == null)
            return;

        // 벽 / 장애물에 먼저 닿으면 벽 타격으로 판정한다.
        if (Physics.CheckSphere(attackPoint.position, attackRadius * 0.5f, obstacleLayer))
        {
            hasHitTarget = true;
            currentPenaltyTime = wallHitPenalty;
            hitSurvivorNetId = 0;
            return;
        }

        Collider[] hitSurvivors = Physics.OverlapSphere(attackPoint.position, attackRadius, survivorLayer);

        foreach (var hit in hitSurvivors)
        {
            SurvivorState sState = hit.GetComponentInParent<SurvivorState>();

            if (sState == null)
                continue;

            NetworkIdentity id = sState.GetComponent<NetworkIdentity>();

            if (id == null)
                continue;

            hasHitTarget = true;
            currentPenaltyTime = hitSuccessPenalty;
            hitSurvivorNetId = id.netId;
            return;
        }
    }

    // 자식 Animator의 Animation Event에서 호출된다.
    // 실제 소리는 서버를 거쳐 모든 클라이언트에게 3D로 재생한다.
    public void PlayKillerWeaponSwingByAnimationEvent()
    {
        // Animation Event는 모든 클라이언트의 Animator에서 호출될 수 있으므로
        // 실제 살인마를 조종하는 로컬 플레이어만 서버에 요청한다.
        if (!isLocalPlayer)
            return;

        if (state == null)
            return;

        CmdPlayKillerWeaponSwingByAnimationEvent();
    }

    [Command]
    private void CmdPlayKillerWeaponSwingByAnimationEvent()
    {
        if (!CanServerPlayWeaponSwingSound())
            return;

        lastWeaponSwingServerTime = Time.time;

        NetworkAudioManager.PlayAudioForEveryone(
            weaponSwingSoundKey,
            AudioDimension.Sound3D,
            transform.position + weaponSwingSoundOffset
        );
    }

    [Server]
    private bool CanServerPlayWeaponSwingSound()
    {
        if (NetworkAudioManager.Instance == null)
            return false;

        if (weaponSwingSoundKey == AudioKey.None)
            return false;

        if (state == null)
            return false;

        // 같은 애니메이션 이벤트가 너무 짧은 시간에 중복 호출되는 것을 방지한다.
        if (Time.time - lastWeaponSwingServerTime < weaponSwingMinInterval)
            return false;

        return true;
    }

    [Command]
    private void CmdStartLunge()
    {
        if (state == null)
            return;

        if (state.CurrentCondition != KillerCondition.Idle)
            return;

        state.ChangeState(KillerCondition.Lunging);
    }

    [Command]
    private void CmdEndLunge(float lungeTime, bool isHit, uint survivorNetId)
    {
        if (state == null)
            return;

        if (state.CurrentCondition != KillerCondition.Lunging)
            return;

        state.ChangeState(KillerCondition.Recovering);

        float finalPenalty;

        if (isHit)
        {
            // survivorNetId가 있으면 생존자 타격, 없으면 벽 타격
            finalPenalty = survivorNetId != 0 ? hitSuccessPenalty : wallHitPenalty;
        }
        else
        {
            // 헛스윙 패널티 계산
            finalPenalty = Mathf.Max(1.2f, lungeTime * hitFailPenalty);
        }

        if (isHit && survivorNetId != 0)
        {
            if (NetworkServer.spawned.TryGetValue(survivorNetId, out NetworkIdentity identity))
            {
                SurvivorState sState = identity.GetComponentInParent<SurvivorState>();

                if (sState != null)
                {
                    // 실제로 Healthy / Injured 상태인 생존자를 맞췄을 때만 성공 타격음을 낸다.
                    // Downed, Dead, Imprisoned 상태를 다시 건드렸을 때는 성공음이 중복으로 나지 않는다.
                    bool canDamage = sState.IsHealthy || sState.IsInjured;

                    if (canDamage)
                        ServerPlayAttackHitSound(identity.transform.position);

                    sState.TakeHit();
                }
            }
        }

        if (isHit && survivorNetId != 0)
            Debug.Log("킬러 공격 명중");
        else
            Debug.Log("헛스윙 또는 장애물에 막힘");

        float animSpeed = baseAttackAnimationLength / finalPenalty;
        RpcSyncAttackResult(animSpeed, finalPenalty);
    }

    // 서버에서 실제 생존자 타격 성공 순간에만 재생한다.
    [Server]
    private void ServerPlayAttackHitSound(Vector3 hitPosition)
    {
        if (NetworkAudioManager.Instance == null)
            return;

        if (attackHitSoundKey == AudioKey.None)
            return;

        NetworkAudioManager.PlayAudioForEveryone(
            attackHitSoundKey,
            AudioDimension.Sound3D,
            hitPosition + attackHitSoundOffset
        );
    }

    [Command]
    private void CmdResetToIdle()
    {
        if (state == null)
            return;

        if (state.CurrentCondition == KillerCondition.Recovering)
            state.ChangeState(KillerCondition.Idle);
    }

    [ClientRpc]
    private void RpcSyncAttackResult(float speed, float penalty)
    {
        if (animator != null)
            animator.SetFloat("AttackSpeed", Mathf.Clamp(speed, 0.8f, 3.0f));

        if (isLocalPlayer)
        {
            currentPenaltyTime = penalty;

            BindUI();

            if (killerSkillUI != null)
                killerSkillUI.StartAttackCooldown(penalty);
        }
    }
}