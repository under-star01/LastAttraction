using System.Collections;
using Mirror;
using UnityEngine;

public enum SurvivorCondition
{
    Healthy,   // 정상
    Injured,   // 부상
    Downed     // 쓰러짐
}

public class SurvivorState : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Animator animator;
    [SerializeField] private SurvivorInteractor interactor;

    [Header("다운 연출")]
    [SerializeField] private float downHitDuration = 1.2f; // 다운 피격 애니메이션 시간

    private SurvivorMove move;

    private int normalLayer; // 기본 레이어 번호
    private int downedLayer; // 다운 레이어 번호

    // 실제 상태는 서버가 가지고, 클라이언트들에게 자동 동기화
    [SyncVar(hook = nameof(OnConditionChanged))]
    private SurvivorCondition currentCondition = SurvivorCondition.Healthy;

    // 다운 피격 연출 중 여부도 동기화
    [SyncVar(hook = nameof(OnBusyChanged))]
    private bool isToDowned;

    public SurvivorCondition CurrentCondition => currentCondition;

    public bool IsHealthy => currentCondition == SurvivorCondition.Healthy;
    public bool IsInjured => currentCondition == SurvivorCondition.Injured;
    public bool IsDowned => currentCondition == SurvivorCondition.Downed;
    public bool IsBusy => isToDowned;

    private void Awake()
    {
        move = GetComponent<SurvivorMove>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (interactor == null)
            interactor = GetComponent<SurvivorInteractor>();

        normalLayer = LayerMask.NameToLayer("Survivor");
        downedLayer = LayerMask.NameToLayer("Downed");
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 접속 직후 현재 동기화된 상태를 외형에 반영
        ApplyInteractionState();
        ApplyLayer();
        UpdateAnimator();
    }

    // 서버에서만 피격 처리
    [Server]
    public void TakeHit()
    {
        if (isToDowned)
            return;

        if (currentCondition == SurvivorCondition.Healthy)
        {
            currentCondition = SurvivorCondition.Injured;
        }
        else if (currentCondition == SurvivorCondition.Injured)
        {
            StartCoroutine(DownedRoutine());
        }
    }

    // 서버에서만 완전 회복
    [Server]
    public void HealToHealthy()
    {
        if (isToDowned)
            return;

        currentCondition = SurvivorCondition.Healthy;
    }

    // 서버에서만 다운 -> 부상 회복
    [Server]
    public void RecoverToInjured()
    {
        if (isToDowned)
            return;

        currentCondition = SurvivorCondition.Injured;
    }

    // 서버에서만 다운 연출 시작
    [Server]
    private IEnumerator DownedRoutine()
    {
        isToDowned = true;

        // 다운 피격 애니메이션 재생
        RpcPlayDownHit();

        yield return new WaitForSeconds(downHitDuration);

        currentCondition = SurvivorCondition.Downed;
        isToDowned = false;
    }

    // 모든 클라이언트에서 다운 피격 애니메이션 재생
    [ClientRpc]
    private void RpcPlayDownHit()
    {
        if (move != null)
        {
            move.SetMoveLock(true);
            move.StopAnimation();
        }

        if (animator != null)
        {
            animator.SetTrigger("DownHit");
        }

        StartCoroutine(LocalUnlockAfterDownHit());
    }

    // 각 클라이언트 로컬에서 다운 피격 애니메이션 시간만큼 잠깐 잠금 유지
    private IEnumerator LocalUnlockAfterDownHit()
    {
        yield return new WaitForSeconds(downHitDuration);

        if (move != null && IsDowned)
        {
            move.SetMoveLock(false);
        }
    }

    // 상태값이 바뀌면 자동 호출
    private void OnConditionChanged(SurvivorCondition oldValue, SurvivorCondition newValue)
    {
        ApplyInteractionState();
        ApplyLayer();
        UpdateAnimator();
    }

    // 바쁜 상태(다운 연출 중)가 바뀌면 자동 호출
    private void OnBusyChanged(bool oldValue, bool newValue)
    {
        if (move != null)
        {
            move.SetMoveLock(newValue);
        }
    }

    // 다운 상태면 상호작용 막기
    private void ApplyInteractionState()
    {
        if (interactor != null)
        {
            interactor.enabled = !IsDowned;
        }
    }

    // 상태에 따라 레이어 변경
    private void ApplyLayer()
    {
        int targetLayer = normalLayer;

        if (IsDowned)
        {
            targetLayer = downedLayer;
        }

        if (targetLayer == -1)
            return;

        SetLayerRecursive(transform, targetLayer);
    }

    // 자기 자신 + 자식들 레이어까지 전부 변경
    private void SetLayerRecursive(Transform target, int layer)
    {
        if (target.GetComponent<SurvivorHeal>() != null)
            return;

        target.gameObject.layer = layer;

        foreach (Transform child in target)
        {
            SetLayerRecursive(child, layer);
        }
    }

    // 애니메이터 파라미터 반영
    private void UpdateAnimator()
    {
        if (animator == null)
            return;

        animator.SetInteger("Condition", (int)currentCondition);
    }
}