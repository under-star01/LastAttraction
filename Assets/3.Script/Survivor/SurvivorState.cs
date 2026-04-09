using System.Collections;
using Mirror;
using UnityEngine;

// 생존자 상태 종류
public enum SurvivorCondition
{
    Healthy,      // 정상0
    Injured,      // 부상1
    Downed,       // 다운2
    Imprisoned,   // 감옥3
    Dead          // 사망4
}

public class SurvivorState : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Animator animator;                 // 애니메이터
    [SerializeField] private SurvivorInteractor interactor;    // 상호작용 스크립트

    [Header("다운 연출")]
    [SerializeField] private float downHitDuration = 3f;       // 다운 피격 연출 시간

    [Header("감옥 시간")]
    [SerializeField] private float prisonFullTime = 120f;      // 기본 감옥 시간 2분
    [SerializeField] private float prisonHalfTime = 60f;       // 다음 단계 감옥 시간 1분

    private SurvivorMove move;                                 // 이동 스크립트

    private int normalLayer;                                   // 일반 레이어
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

    // 현재 들어가 있는 감옥의 netId
    // 감옥 상태일 때 SurvivorInteractor가 "내 감옥만" 상호작용하게 할 때 사용
    [SyncVar]
    private uint currentPrisonId;

    // 다음 감옥 단계
    // 0 = 다음 감옥 120초
    // 1 = 다음 감옥 60초
    // 2 = 다음 감옥 즉사
    [SyncVar]
    private int prisonStep;

    public SurvivorCondition CurrentCondition => currentCondition;

    public bool IsHealthy => currentCondition == SurvivorCondition.Healthy;
    public bool IsInjured => currentCondition == SurvivorCondition.Injured;
    public bool IsDowned => currentCondition == SurvivorCondition.Downed;
    public bool IsImprisoned => currentCondition == SurvivorCondition.Imprisoned;
    public bool IsDead => currentCondition == SurvivorCondition.Dead;

    public bool IsBusy => isToDowned;
    public bool IsBeingHealed => isBeingHealed;
    public bool IsDoingInteraction => isDoingInteraction;
    public uint CurrentPrisonId => currentPrisonId;

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
        // 디버그 입력은 내 로컬 플레이어만 가능
        if (!isLocalPlayer)
            return;

        // F1 누르면 피격 테스트
        if (Input.GetKeyDown(KeyCode.F1))
            CmdDebugTakeHit();

        // F2 누르면 감옥 보내기 테스트
        if (Input.GetKeyDown(KeyCode.F2))
            CmdDebugGoPrison();
    }

    // 디버그용 피격 요청
    [Command]
    private void CmdDebugTakeHit()
    {
        TakeHit();
    }

    // 디버그용 감옥 보내기 요청
    [Command]
    private void CmdDebugGoPrison()
    {
        // 이미 감옥 / 사망 상태면 무시
        if (IsImprisoned || IsDead)
            return;

        // 다운 상태가 아니면 다운 상태로 바꾼 뒤 보내기
        if (!IsDowned)
            currentCondition = SurvivorCondition.Downed;

        // 비어 있는 감옥 찾기
        Prison prison = PrisonManager.Instance.GetEmpty();

        // 감옥에 넣기
        prison.SetPrisoner(this);
    }

    // 서버에서만 피격 처리
    [Server]
    public void TakeHit()
    {
        // 이미 다운 연출 중이거나 감옥/사망 상태면 무시
        if (isToDowned || IsImprisoned || IsDead)
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

    // 서버에서 부상 -> 정상 회복
    [Server]
    public void HealToHealthy()
    {
        if (isToDowned || IsImprisoned || IsDead)
            return;

        currentCondition = SurvivorCondition.Healthy;
    }

    // 서버에서 다운 -> 부상 회복
    [Server]
    public void RecoverToInjured()
    {
        if (isToDowned || IsImprisoned || IsDead)
            return;

        currentCondition = SurvivorCondition.Injured;
    }

    // 서버에서 힐받는 상태 변경
    [Server]
    public void SetBeingHealedServer(bool value)
    {
        isBeingHealed = value;
    }

    // 서버에서 Hold 상호작용 중 여부 저장
    [Server]
    public void SetDoingInteractionServer(bool value)
    {
        isDoingInteraction = value;
    }

    // 이 생존자가 다음 감옥에 들어갈 때 시작할 시간 반환
    [Server]
    public float GetPrisonStartTime()
    {
        if (prisonStep == 0)
            return prisonFullTime;

        if (prisonStep == 1)
            return prisonHalfTime;

        // 2 이상이면 다음 감옥은 즉사
        return 0f;
    }

    // 감옥에 들어가기
    [Server]
    public bool EnterPrison(uint prisonId)
    {
        // 즉사 단계면 바로 사망
        if (prisonStep >= 2)
        {
            Die();
            return false;
        }

        // 기존 상호작용 상태 정리
        isDoingInteraction = false;
        isBeingHealed = false;

        currentPrisonId = prisonId;
        currentCondition = SurvivorCondition.Imprisoned;
        return true;
    }

    // 감옥에서 나왔을 때 호출
    // remainTime 기준으로 다음 감옥 단계 저장
    [Server]
    public void LeavePrison(float remainTime)
    {
        currentPrisonId = 0;

        // 60초 초과 남기고 나왔으면 다음 감옥은 60초부터
        if (remainTime > prisonHalfTime)
            prisonStep = 1;
        else
            prisonStep = 2; // 60초 이하로 나왔으면 다음 감옥은 즉사

        // 살아서 나온 경우엔 부상 상태로 복귀
        currentCondition = SurvivorCondition.Injured;
    }

    // 사망 처리
    [Server]
    public void Die()
    {
        if (animator != null)
            animator.SetTrigger("DownHit");

        currentPrisonId = 0;
        isDoingInteraction = false;
        isBeingHealed = false;
        currentCondition = SurvivorCondition.Dead;
    }

    // 서버에서 다운 연출 시작
    [Server]
    private IEnumerator DownRoutine()
    {
        isToDowned = true;

        // 다운되면 현재 상호작용 상태 강제 해제
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

    // 상태에 따라 상호작용 가능 여부 적용
    private void ApplyInteract()
    {
        if (interactor == null)
            return;

        // 다운 / 힐받는 중 / 사망 상태면 상호작용 불가
        interactor.enabled = !IsDowned && !IsBeingHealed && !IsDead;
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

    // 애니메이터 파라미터 갱신
    private void UpdateAnim()
    {
        if (animator == null)
            return;

        animator.SetInteger("Condition", (int)currentCondition);
        animator.SetBool("IsImprisoned", IsImprisoned);
    }
}