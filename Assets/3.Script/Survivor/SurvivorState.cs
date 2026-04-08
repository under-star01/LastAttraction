using System.Collections;
using Mirror;
using UnityEngine;

// 생존자 상태 종류
public enum SurvivorCondition
{
    Healthy,      // 정상
    Injured,      // 부상
    Downed,       // 다운
    Imprisoned    // 감옥에 갇힘
}

public class SurvivorState : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Animator animator;                 // 애니메이터
    [SerializeField] private SurvivorInteractor interactor;    // 상호작용 스크립트

    [Header("다운 연출")]
    [SerializeField] private float downHitDuration = 3f;       // 다운 피격 연출 시간

    private SurvivorMove move;                                 // 이동 스크립트

    private int normalLayer;                                   // 기본 레이어
    private int downedLayer;                                   // 다운 레이어

    // 현재 상태는 서버가 가지고 있고 자동 동기화됨
    [SyncVar(hook = nameof(OnCondition))]
    private SurvivorCondition currentCondition = SurvivorCondition.Healthy;

    // 다운 피격 연출 중인지 여부
    [SyncVar(hook = nameof(OnBusy))]
    private bool isToDowned;

    // 현재 다른 생존자에게 힐을 받고 있는 중인지 여부
    [SyncVar(hook = nameof(OnHealed))]
    private bool isBeingHealed;

    // 현재 Hold 상호작용 중인지 여부
    [SyncVar]
    private bool isDoingInteraction;

    public SurvivorCondition CurrentCondition => currentCondition;

    public bool IsHealthy => currentCondition == SurvivorCondition.Healthy;
    public bool IsInjured => currentCondition == SurvivorCondition.Injured;
    public bool IsDowned => currentCondition == SurvivorCondition.Downed;
    public bool IsImprisoned => currentCondition == SurvivorCondition.Imprisoned;

    public bool IsBusy => isToDowned;
    public bool IsBeingHealed => isBeingHealed;
    public bool IsDoingInteraction => isDoingInteraction;

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

        // 접속 직후 현재 상태를 외형에 바로 반영
        ApplyInteract();
        ApplyLayer();
        UpdateAnim();
    }

    private void Update()
    {
        // 디버그용 피격 테스트
        if (!isLocalPlayer)
            return;

        if (Input.GetKeyDown(KeyCode.F1))
            CmdDebugTakeHit();
    }

    // 디버그용 피격 요청
    [Command]
    private void CmdDebugTakeHit()
    {
        TakeHit();
    }

    // 서버에서만 피격 처리
    [Server]
    public void TakeHit()
    {
        // 이미 다운 연출 중이거나 감옥 상태면 중복 처리 막기
        if (isToDowned || IsImprisoned)
            return;

        // 정상 -> 부상
        if (currentCondition == SurvivorCondition.Healthy)
        {
            currentCondition = SurvivorCondition.Injured;
            return;
        }

        // 부상 -> 다운
        if (currentCondition == SurvivorCondition.Injured)
            StartCoroutine(DownRoutine());
    }

    // 서버에서만 부상 -> 정상 회복
    [Server]
    public void HealToHealthy()
    {
        if (isToDowned || IsImprisoned)
            return;

        currentCondition = SurvivorCondition.Healthy;
    }

    // 서버에서만 다운 -> 부상 회복
    [Server]
    public void RecoverToInjured()
    {
        if (isToDowned || IsImprisoned)
            return;

        currentCondition = SurvivorCondition.Injured;
    }

    // 서버에서만 힐받는 상태 변경
    [Server]
    public void SetBeingHealedServer(bool value)
    {
        isBeingHealed = value;
    }

    // 서버에서만 현재 상호작용 중 여부 저장
    [Server]
    public void SetDoingInteractionServer(bool value)
    {
        isDoingInteraction = value;
    }

    // 감옥 상태로 변경
    [Server]
    public void SetPrison()
    {
        // 감옥에 들어가면 기존 상호작용/힐 상태 정리
        isDoingInteraction = false;
        isBeingHealed = false;

        currentCondition = SurvivorCondition.Imprisoned;
    }

    // 감옥에서 꺼내기
    [Server]
    public void OutPrison()
    {
        // 부상 상태로 복귀
        currentCondition = SurvivorCondition.Injured;
    }

    // 서버에서 다운 연출 시작
    [Server]
    private IEnumerator DownRoutine()
    {
        isToDowned = true;

        // 다운되면 현재 상호작용 상태도 강제로 해제
        isDoingInteraction = false;

        // 모든 클라이언트에서 다운 피격 애니메이션 실행
        RpcDownHit();

        yield return new WaitForSeconds(downHitDuration);

        currentCondition = SurvivorCondition.Downed;
        isToDowned = false;
    }

    // 모든 클라이언트에서 다운 피격 애니메이션 재생
    [ClientRpc]
    private void RpcDownHit()
    {
        if (move != null)
        {
            // 피격 연출 중 잠깐 이동 잠금
            move.SetMoveLock(true);
            move.StopAnimation();
        }

        if (animator != null)
            animator.SetTrigger("DownHit");

        StartCoroutine(LocalDown());
    }

    // 각 클라이언트 로컬에서 피격 연출 시간만큼 잠금 유지
    private IEnumerator LocalDown()
    {
        yield return new WaitForSeconds(downHitDuration);

        // 완전히 다운 상태가 됐다면 잠금 해제
        if (move != null && IsDowned)
            move.SetMoveLock(false);
    }

    // 상태가 바뀌면 외형 / 상호작용 / 레이어 갱신
    private void OnCondition(SurvivorCondition oldValue, SurvivorCondition newValue)
    {
        ApplyInteract();
        ApplyLayer();
        UpdateAnim();
    }

    // 다운 연출 중 여부가 바뀌면 이동 잠금 반영
    private void OnBusy(bool oldValue, bool newValue)
    {
        if (move != null)
            move.SetMoveLock(newValue);
    }

    // 힐받는 상태가 바뀌면 상호작용 가능 여부 반영
    private void OnHealed(bool oldValue, bool newValue)
    {
        ApplyInteract();
    }

    // 다운 상태거나 감옥 상태거나 힐받는 중이면 일반 상호작용 막기
    private void ApplyInteract()
    {
        if (interactor != null)
            interactor.enabled = !IsDowned && !IsImprisoned && !IsBeingHealed;
    }

    // 상태에 따라 레이어 변경
    private void ApplyLayer()
    {
        int targetLayer = normalLayer;

        if (IsDowned)
            targetLayer = downedLayer;

        if (targetLayer == -1)
            return;

        SetLayer(transform, targetLayer);
    }

    // 자기 자신 + 자식들 레이어까지 전부 변경
    private void SetLayer(Transform target, int layer)
    {
        // 힐 트리거는 레이어 변경 제외
        if (target.GetComponent<SurvivorHeal>() != null)
            return;

        target.gameObject.layer = layer;

        foreach (Transform child in target)
            SetLayer(child, layer);
    }

    // 현재 상태를 애니메이터 파라미터에 반영
    private void UpdateAnim()
    {
        if (animator != null)
        {
            animator.SetInteger("Condition", (int)currentCondition);
            animator.SetBool("IsImprisoned", IsImprisoned);
        }
    }
}