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
    [SerializeField] private float downHitDuration = 3f; // 다운 피격 연출 시간

    private SurvivorMove move;

    private int normalLayer; // 기본 레이어
    private int downedLayer; // 다운 레이어

    // 현재 상태는 서버가 가지고 있고 자동 동기화됨
    [SyncVar(hook = nameof(OnCondition))]
    private SurvivorCondition currentCondition = SurvivorCondition.Healthy;

    // 다운 피격 연출 중인지 여부
    [SyncVar(hook = nameof(OnBusy))]
    private bool isToDowned;

    // 현재 다른 생존자에게 힐을 받고 있는 중인지 여부
    // 힐받는 동안 다른 상호작용을 막기 위해 사용
    [SyncVar(hook = nameof(OnHealed))]
    private bool isBeingHealed;

    // 추가:
    // 현재 이 생존자가 "조사 / 힐하기 / 발전기" 같은 Hold 상호작용을 하고 있는지
    // 이 값은 서버가 알고 있어야 다른 시스템(예: 힐 시작 차단)에서 신뢰할 수 있다
    [SyncVar]
    private bool isDoingInteraction;

    public SurvivorCondition CurrentCondition => currentCondition;

    public bool IsHealthy => currentCondition == SurvivorCondition.Healthy;
    public bool IsInjured => currentCondition == SurvivorCondition.Injured;
    public bool IsDowned => currentCondition == SurvivorCondition.Downed;
    public bool IsBusy => isToDowned;
    public bool IsBeingHealed => isBeingHealed;

    // 추가:
    // 다른 스크립트가 "이 대상이 현재 상호작용 중인가?"를 확인할 때 사용
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
        // 로컬 플레이어만 F1 입력 가능
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
        // 이미 다운 연출 중이면 중복 실행 방지
        if (isToDowned)
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

    // 서버에서만 힐받는 상태 변경
    [Server]
    public void SetBeingHealedServer(bool value)
    {
        isBeingHealed = value;
    }

    // 서버에서만 현재 상호작용 중 여부를 저장
    // SurvivorInteractor가 상호작용 시작/종료할 때 호출한다
    [Server]
    public void SetDoingInteractionServer(bool value)
    {
        isDoingInteraction = value;
    }

    // 서버에서 다운 연출 시작
    [Server]
    private IEnumerator DownRoutine()
    {
        isToDowned = true;

        // 다운되면 현재 상호작용 상태도 강제로 해제
        // 그래야 다운된 뒤에도 "상호작용 중"으로 남아 있지 않음
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
        // 이후 다운 상태 이동은 SurvivorMove 쪽에서 처리
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

    // 다운 상태거나 힐받는 중이면 상호작용 막기
    private void ApplyInteract()
    {
        if (interactor != null)
            interactor.enabled = !IsDowned && !IsBeingHealed;
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
        // 같이 바뀌면 힐이 안됨
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
            animator.SetInteger("Condition", (int)currentCondition);
    }
}