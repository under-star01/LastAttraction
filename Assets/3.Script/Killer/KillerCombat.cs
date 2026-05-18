using UnityEngine;
using Mirror;
using System.Collections;

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
    [SerializeField] private AudioKey weaponSwingSoundKey = AudioKey.KillerWeaponSwing;
    [SerializeField] private AudioKey attackHitSoundKey = AudioKey.KillerAttackHit;
    [SerializeField] private Vector3 weaponSwingSoundOffset = new Vector3(0f, 1.2f, 0f);
    [SerializeField] private Vector3 attackHitSoundOffset = new Vector3(0f, 1.0f, 0f);
    [SerializeField] private float weaponSwingMinInterval = 0.08f;

    private KillerInput input;
    private KillerState state;
    private KillerSkillUI killerSkillUI;
    private Animator animator;
    private TrapHandler trapHandler;

    private float currentLungeTime;
    private float currentPenaltyTime;
    private bool hasRecoveryPenalty;
    private bool hasHitTarget;
    private uint hitSurvivorNetId;
    private bool isEndingAttack;

    private float lastWeaponSwingServerTime;
    private Coroutine serverRecoveryCoroutine;

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
            animator.SetBool("isLunging", state.CurrentCondition == KillerCondition.Lunging);

        if (!isLocalPlayer)
            return;

        if (state == null || input == null)
            return;

        if ((trapHandler != null && trapHandler.IsBuildMode) || state.CurrentCondition == KillerCondition.Planting)
            return;

        if (state.CurrentCondition == KillerCondition.Recovering)
        {
            HandleRecoveryUIOnly();
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

    // 클라이언트에서는 Recovering 상태를 Idle로 바꾸지 않는다.
    // 서버가 정확한 패널티 시간 뒤에 Idle로 바꾼다.
    private void HandleRecoveryUIOnly()
    {
        if (!hasRecoveryPenalty)
            return;

        currentPenaltyTime -= Time.deltaTime;

        if (currentPenaltyTime <= 0f)
        {
            currentPenaltyTime = 0f;
            hasRecoveryPenalty = false;
            isEndingAttack = false;
        }
    }

    private void HandleAttackInput()
    {
        if (!isLocalPlayer)
            return;

        if (input == null || state == null)
            return;

        if (trapHandler != null && trapHandler.IsBuildMode)
            return;

        if (input.IsAttackPressed)
        {
            if (state.CurrentCondition != KillerCondition.Lunging)
            {
                if (!state.CanAttack)
                    return;

                hasHitTarget = false;
                currentLungeTime = 0f;
                hitSurvivorNetId = 0;
                isEndingAttack = false;
                hasRecoveryPenalty = false;

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

    // Animation Event에서만 호출된다.
    // 킬러 본인은 2D로 즉시 듣고, 생존자들은 서버를 통해 3D로 듣는다.
    public void PlayKillerWeaponSwingByAnimationEvent()
    {
        if (!isLocalPlayer)
            return;

        if (state == null)
            return;

        if (weaponSwingSoundKey == AudioKey.None)
            return;

        AudioManager.PlayLocalAudio(weaponSwingSoundKey, AudioDimension.Sound2D);

        CmdPlayKillerWeaponSwingByAnimationEvent();
    }

    [Command]
    private void CmdPlayKillerWeaponSwingByAnimationEvent()
    {
        if (!CanServerPlayWeaponSwingSound())
            return;

        lastWeaponSwingServerTime = Time.time;

        NetworkAudioManager.PlayAudioForSurvivors(
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

        if (serverRecoveryCoroutine != null)
        {
            StopCoroutine(serverRecoveryCoroutine);
            serverRecoveryCoroutine = null;
        }

        state.ChangeState(KillerCondition.Lunging);
    }

    [Command]
    private void CmdEndLunge(float lungeTime, bool isHit, uint survivorNetId)
    {
        if (state == null)
            return;

        if (state.CurrentCondition != KillerCondition.Lunging)
            return;

        float finalPenalty;

        if (isHit)
            finalPenalty = survivorNetId != 0 ? hitSuccessPenalty : wallHitPenalty;
        else
            finalPenalty = Mathf.Max(1.2f, lungeTime * hitFailPenalty);

        float animSpeed = baseAttackAnimationLength / finalPenalty;

        // Attack 트리거가 실행되기 전에 클라이언트에 패널티 시간과 애니메이션 속도를 먼저 준비시킨다.
        RpcSyncAttackResult(animSpeed, finalPenalty);

        // Recovering으로 바뀌면 KillerState에서 Attack 트리거가 실행된다.
        state.ChangeState(KillerCondition.Recovering);

        if (isHit && survivorNetId != 0)
        {
            if (NetworkServer.spawned.TryGetValue(survivorNetId, out NetworkIdentity identity))
            {
                SurvivorState sState = identity.GetComponentInParent<SurvivorState>();

                if (sState != null)
                {
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

        if (serverRecoveryCoroutine != null)
            StopCoroutine(serverRecoveryCoroutine);

        serverRecoveryCoroutine = StartCoroutine(ServerRecoveryRoutine(finalPenalty));
    }

    [Server]
    private IEnumerator ServerRecoveryRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);

        serverRecoveryCoroutine = null;

        if (state != null && state.CurrentCondition == KillerCondition.Recovering)
            state.ChangeState(KillerCondition.Idle);
    }

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

    [ClientRpc]
    private void RpcSyncAttackResult(float speed, float penalty)
    {
        if (animator != null)
            animator.SetFloat("AttackSpeed", Mathf.Clamp(speed, 0.8f, 3.0f));

        if (isLocalPlayer)
        {
            currentPenaltyTime = penalty;
            hasRecoveryPenalty = true;

            BindUI();

            if (killerSkillUI != null)
                killerSkillUI.StartAttackCooldown(penalty);
        }
    }
}