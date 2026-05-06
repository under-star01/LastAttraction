using Mirror;
using UnityEngine;
using System.Collections;

// 몸 상태 전용
public enum SurvivorCondition
{
    Healthy,
    Injured,
    Downed,
    Imprisoned,
    Dead
}

public class SurvivorState : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Animator animator;
    [SerializeField] private SurvivorInteractor interactor;

    [Header("피격 연출")]
    [SerializeField] private float hitDuration = 0.8f;      // Healthy -> Injured 일반 피격 애니메이션 시간
    [SerializeField] private float downHitDuration = 3f;    // Injured -> Downed 다운 피격 시간

    [Header("감옥 시간")]
    [SerializeField] private float prisonFullTime = 120f;
    [SerializeField] private float prisonHalfTime = 60f;

    private SurvivorMove move;
    private SurvivorActionState actionState;

    private int normalLayer;
    private int downedLayer;

    [SyncVar(hook = nameof(OnConditionChanged))]
    private SurvivorCondition currentCondition = SurvivorCondition.Healthy;

    [SyncVar]
    private uint currentPrisonId;

    [SyncVar]
    private int prisonStep;

    [SyncVar]
    private bool isEscaping;

    public SurvivorCondition CurrentCondition => currentCondition;

    public bool IsHealthy => currentCondition == SurvivorCondition.Healthy;
    public bool IsInjured => currentCondition == SurvivorCondition.Injured;
    public bool IsDowned => currentCondition == SurvivorCondition.Downed;
    public bool IsImprisoned => currentCondition == SurvivorCondition.Imprisoned;
    public bool IsDead => currentCondition == SurvivorCondition.Dead;
    public bool IsEscaping => isEscaping;

    public uint CurrentPrisonId => currentPrisonId;

    public int PrisonStep => prisonStep;
    public float PrisonFullTime => prisonFullTime;
    public float PrisonHalfTime => prisonHalfTime;

    private void Awake()
    {
        move = GetComponent<SurvivorMove>();
        actionState = GetComponent<SurvivorActionState>();

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

        ApplyLayer();
        UpdateAnim();

        if (actionState != null)
            actionState.ApplyUse();
    }

    private void Update()
    {
        if (!isLocalPlayer)
            return;

        if (Input.GetKeyDown(KeyCode.F1))
            CmdDebugTakeHit();

        if (Input.GetKeyDown(KeyCode.F2))
            CmdDebugGoPrison();
    }

    [Command]
    private void CmdDebugTakeHit()
    {
        TakeHit();
    }

    [Command]
    private void CmdDebugGoPrison()
    {
        if (IsImprisoned || IsDead)
            return;

        if (!IsDowned)
        {
            currentCondition = SurvivorCondition.Downed;
            ApplyAllStateServer();
        }

        Prison prison = PrisonManager.Instance.GetEmpty();
        if (prison != null)
            prison.SetPrisoner(this);
    }

    // 피격 처리
    [Server]
    public void TakeHit()
    {
        // 다운 피격 중에는 추가 피격 무시
        if (actionState != null && actionState.CurrentAction == SurvivorAction.DownHit)
            return;

        if (IsImprisoned || IsDead || IsEscaping)
            return;

        StopAllCoroutines();

        // 피격되는 순간 서버 상태와 소유 클라이언트의 실제 Hold 상호작용을 같이 끊는다.
        // 서버에서 ForceStopInteract()만 호출하면 로컬 activeInteractable이 없어
        // Evidence, Upload, Heal, Prison 진행이 계속 남을 수 있다.
        if (interactor != null)
            interactor.ForceStopInteractFromServer();

        // 서버 행동 상태 초기화
        if (actionState != null)
            actionState.ForceResetActionServer();

        // Healthy -> Injured
        if (currentCondition == SurvivorCondition.Healthy)
        {
            currentCondition = SurvivorCondition.Injured;
            ApplyAllStateServer();

            if (actionState != null)
                StartCoroutine(actionState.HitRoutine(hitDuration));

            return;
        }

        // Injured -> Downed
        if (currentCondition == SurvivorCondition.Injured)
        {
            currentCondition = SurvivorCondition.Downed;
            ApplyAllStateServer();

            if (actionState != null)
                StartCoroutine(actionState.DownHitRoutine(downHitDuration));

            return;
        }
    }

    [Server]
    public void HealToHealthy()
    {
        if (IsImprisoned || IsDead || IsEscaping)
            return;

        currentCondition = SurvivorCondition.Healthy;
        ApplyAllStateServer();
    }

    [Server]
    public void RecoverToInjured()
    {
        if (IsImprisoned || IsDead || IsEscaping)
            return;

        currentCondition = SurvivorCondition.Injured;
        ApplyAllStateServer();
    }

    // 다음 감옥 시작 시간 계산
    [Server]
    public float GetPrisonStartTime()
    {
        if (prisonStep == 0)
            return prisonFullTime;

        if (prisonStep == 1)
            return prisonHalfTime;

        return 0f;
    }

    // 감옥 진입
    [Server]
    public bool EnterPrison(uint prisonId)
    {
        if (IsEscaping)
            return false;

        // 이미 2단계까지 진행된 생존자가 다시 잡히면 감옥에 넣지 않고 사망 처리한다.
        if (prisonStep >= 2)
        {
            Die();
            return false;
        }

        prisonStep++;

        currentPrisonId = prisonId;
        currentCondition = SurvivorCondition.Imprisoned;

        if (actionState != null)
        {
            actionState.SetInteract(false);
            actionState.SetHeal(false);
            actionState.SetCam(false);
            actionState.SetAct(SurvivorAction.None);
        }

        ApplyAllStateServer();
        return true;
    }

    // 감옥 탈출 후 상태
    [Server]
    public void LeavePrison(float remainTime)
    {
        currentPrisonId = 0;

        if (remainTime <= prisonHalfTime && prisonStep < 2)
            prisonStep = 2;

        currentCondition = SurvivorCondition.Injured;
        ApplyAllStateServer();
    }

    // 감옥에 갇힌 상태에서 시간이 절반 이하로 줄어들면 2단계 판정으로 바꾼다.
    [Server]
    public void MarkPrisonHalfPassed()
    {
        if (!IsImprisoned)
            return;

        if (prisonStep < 2)
            prisonStep = 2;
    }

    [Server]
    public void Die()
    {
        currentPrisonId = 0;
        currentCondition = SurvivorCondition.Dead;

        if (interactor != null)
            interactor.ForceStopInteractFromServer();

        if (actionState != null)
        {
            actionState.SetInteract(false);
            actionState.SetHeal(false);
            actionState.SetCam(false);
            actionState.SetAct(SurvivorAction.None);
        }

        ApplyAllStateServer();
    }

    // 감옥 시간이 다 되어 죽을 때 사용한다.
    // 일반 Die와 다르게 DownHit 애니메이션 트리거를 함께 실행한다.
    [Server]
    public void DieByPrisonTime()
    {
        if (IsDead)
            return;

        currentPrisonId = 0;
        currentCondition = SurvivorCondition.Dead;

        if (interactor != null)
            interactor.ForceStopInteractFromServer();

        if (actionState != null)
        {
            actionState.SetInteract(false);
            actionState.SetHeal(false);
            actionState.SetCam(false);
            actionState.SetAct(SurvivorAction.None);
        }

        ApplyAllStateServer();

        if (actionState != null)
            StartCoroutine(actionState.DownHitRoutine(downHitDuration));
    }

    [Server]
    public void SetEscape()
    {
        if (IsDead || IsImprisoned)
            return;

        isEscaping = true;

        StopAllCoroutines();

        if (interactor != null)
            interactor.ForceStopInteractFromServer();

        if (actionState != null)
        {
            actionState.SetInteract(false);
            actionState.SetHeal(false);
            actionState.SetCam(false);
            actionState.SetAct(SurvivorAction.None);
        }

        ApplyAllStateServer();
    }

    // 트랩, QTE 실패 등에서 공통으로 사용하는 스턴 함수
    [Server]
    public void ApplyStun(float duration)
    {
        if (duration <= 0f)
            return;

        if (IsDowned || IsDead || IsImprisoned || IsEscaping)
            return;

        if (actionState == null)
            return;

        StartCoroutine(actionState.StunRoutine(duration));
    }

    // 기존 Trap 코드가 ApplyTrapStun을 호출하는 경우를 위한 호환 함수
    [Server]
    public void ApplyTrapStun(float duration)
    {
        ApplyStun(duration);
    }

    // 서버에서 상태 즉시 반영
    [Server]
    private void ApplyAllStateServer()
    {
        ApplyLayer();
        UpdateAnim();

        if (actionState != null)
            actionState.ApplyUse();
    }

    private void OnConditionChanged(SurvivorCondition oldValue, SurvivorCondition newValue)
    {
        ApplyLayer();
        UpdateAnim();

        if (actionState != null)
            actionState.ApplyUse();
    }

    // 다운 상태일 때만 Downed 레이어 적용
    private void ApplyLayer()
    {
        int targetLayer = normalLayer;

        if (IsDowned)
            targetLayer = downedLayer;

        if (targetLayer == -1)
            return;

        SetLayerRecursive(transform, targetLayer);
    }

    private void SetLayerRecursive(Transform target, int layer)
    {
        if (target == null)
            return;

        int camLocalLayer = LayerMask.NameToLayer("CamLocal");
        int camWorldLayer = LayerMask.NameToLayer("CamWorld");
        int hideSelfLayer = LayerMask.NameToLayer("HideSelf");

        // 힐 트리거는 레이어 변경 제외
        if (target.GetComponent<SurvivorHeal>() != null)
            return;

        // 카메라 모델 / 스킬 숨김용 레이어는 유지
        if (target.gameObject.layer == camLocalLayer)
            return;

        if (target.gameObject.layer == camWorldLayer)
            return;

        if (target.gameObject.layer == hideSelfLayer)
            return;

        target.gameObject.layer = layer;

        foreach (Transform child in target)
            SetLayerRecursive(child, layer);
    }

    // 몸 상태 애니메이터 반영
    private void UpdateAnim()
    {
        if (animator == null)
            return;

        animator.SetInteger("Condition", (int)currentCondition);
    }
}