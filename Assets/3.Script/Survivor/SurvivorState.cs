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

// 생존자 성별
// 다칠 때 소리, 다운 피격음, 신음소리를 남자 / 여자 캐릭터별로 다르게 재생하기 위해 사용한다.
public enum SurvivorGender
{
    Male,
    Female
}

public class SurvivorState : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private Animator animator;
    [SerializeField] private SurvivorInteractor interactor;

    [Header("피격 연출")]
    [SerializeField] private float hitDuration = 0.8f;      // Healthy -> Injured 일반 피격 애니메이션 시간
    [SerializeField] private float downHitDuration = 3f;    // Injured -> Downed 다운 피격 시간

    [Header("캐릭터 정보")]
    [SerializeField] private SurvivorGender gender = SurvivorGender.Male;

    [Header("오디오")]
    [SerializeField] private AudioKey maleHitSoundKey = AudioKey.SurvivorMaleHit;                 // 남자 다칠 때 / 죽을 때 소리
    [SerializeField] private AudioKey femaleHitSoundKey = AudioKey.SurvivorFemaleHit;             // 여자 다칠 때 / 죽을 때 소리
    [SerializeField] private AudioKey maleDownHitSoundKey = AudioKey.SurvivorMaleDownHit;         // 남자 다운 피격 소리
    [SerializeField] private AudioKey femaleDownHitSoundKey = AudioKey.SurvivorFemaleDownHit;     // 여자 다운 피격 소리
    [SerializeField] private AudioKey maleGroanSoundKey = AudioKey.SurvivorMaleGroan;             // 남자 신음소리
    [SerializeField] private AudioKey femaleGroanSoundKey = AudioKey.SurvivorFemaleGroan;         // 여자 신음소리
    [SerializeField] private float groanInterval = 2f;                                            // 신음 반복 간격

    [Header("감옥 시간")]
    [SerializeField] private float prisonFullTime = 120f;
    [SerializeField] private float prisonHalfTime = 60f;

    private SurvivorMove move;
    private SurvivorActionState actionState;

    private int normalLayer;
    private int downedLayer;

    private float groanTimer;

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
        // 신음소리는 서버에서만 계산한다.
        // 서버에서만 오디오 요청을 보내야 클라이언트마다 중복 재생되지 않는다.
        if (isServer)
            UpdateGroanSound();

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

            // 디버그로 바로 다운 상태가 될 때는 맞아서 다운된 상황이 아니므로
            // 다운 피격 소리는 재생하지 않고 신음 타이머만 맞춘다.
            groanTimer = groanInterval;

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

            // 맞아서 다칠 때는 성별에 맞는 일반 피격 소리를 재생한다.
            // AudioManager의 Max Distance를 짧게 잡으면 가까운 사람만 듣는다.
            PlayWorld3DSound(GetHitSoundKey());

            // 피격음과 신음소리가 바로 겹치지 않게 2초 뒤부터 신음 시작
            groanTimer = groanInterval;

            if (actionState != null)
                StartCoroutine(actionState.HitRoutine(hitDuration));

            return;
        }

        // Injured -> Downed
        if (currentCondition == SurvivorCondition.Injured)
        {
            currentCondition = SurvivorCondition.Downed;
            ApplyAllStateServer();

            // 꼭 맞아서 Downed 상태가 되는 순간에만 성별 다운 피격 소리를 재생한다.
            // AudioManager의 Max Distance를 크게 잡아서 맵 전체에 들리는 3D 사운드처럼 만든다.
            PlayWorld3DSound(GetDownHitSoundKey());

            // 다운 피격음과 신음소리가 바로 겹치지 않게 2초 뒤부터 신음 시작
            groanTimer = groanInterval;

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

        // 건강 상태가 되면 신음소리를 멈춘다.
        groanTimer = 0f;

        ApplyAllStateServer();
    }

    [Server]
    public void RecoverToInjured()
    {
        if (IsImprisoned || IsDead || IsEscaping)
            return;

        currentCondition = SurvivorCondition.Injured;

        // 다운에서 부상으로 회복된 뒤에도 Injured 상태이므로 2초 뒤부터 신음 시작
        groanTimer = groanInterval;

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

        // 감옥에 들어간 직후 바로 신음이 겹치지 않게 2초 뒤부터 시작
        groanTimer = groanInterval;

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

        // 감옥에서 나온 뒤 Injured 상태이므로 2초 뒤부터 신음 시작
        groanTimer = groanInterval;

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
        if (IsDead)
            return;

        currentPrisonId = 0;
        currentCondition = SurvivorCondition.Dead;

        // 죽는 소리는 맞아서 다칠 때 소리와 통일한다.
        // 따라서 성별에 맞는 일반 피격 소리를 재생한다.
        PlayWorld3DSound(GetHitSoundKey());

        // 사망하면 신음소리를 멈춘다.
        groanTimer = 0f;

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

        if (move != null)
            move.BeginDeadResult();

        KillerMove killerMove = FindFirstObjectByType<KillerMove>();

        if (killerMove != null)
            killerMove.CheckAllSurvivorsDeadAndShowResult();
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

        // 감옥 시간 초과 사망도 죽는 소리는 일반 피격 소리와 통일한다.
        // 맞아서 Downed가 되는 상황이 아니므로 성별 다운 피격 소리는 재생하지 않는다.
        PlayWorld3DSound(GetHitSoundKey());

        // 사망하면 신음소리를 멈춘다.
        groanTimer = 0f;

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

        if (move != null)
            move.BeginDeadResult();

        KillerMove killerMove = FindFirstObjectByType<KillerMove>();

        if (killerMove != null)
            killerMove.CheckAllSurvivorsDeadAndShowResult();
    }

    [Server]
    public void SetEscape()
    {
        if (IsDead || IsImprisoned)
            return;

        isEscaping = true;

        // 탈출 상태에서는 신음소리를 멈춘다.
        groanTimer = 0f;

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

    // 서버에서 모든 클라이언트에게 월드 위치 기준 3D 사운드를 재생한다.
    // 실제로 얼마나 멀리 들리는지는 AudioManager의 AudioData Max Distance에서 조절한다.
    [Server]
    private void PlayWorld3DSound(AudioKey key)
    {
        if (key == AudioKey.None)
            return;

        NetworkAudioManager.PlayAudioForEveryone(
            key,
            AudioDimension.Sound3D,
            transform.position
        );
    }

    // 현재 생존자 성별에 맞는 일반 피격 소리 AudioKey를 반환한다.
    private AudioKey GetHitSoundKey()
    {
        if (gender == SurvivorGender.Male)
            return maleHitSoundKey;

        if (gender == SurvivorGender.Female)
            return femaleHitSoundKey;

        return AudioKey.None;
    }

    // 현재 생존자 성별에 맞는 다운 피격 소리 AudioKey를 반환한다.
    private AudioKey GetDownHitSoundKey()
    {
        if (gender == SurvivorGender.Male)
            return maleDownHitSoundKey;

        if (gender == SurvivorGender.Female)
            return femaleDownHitSoundKey;

        return AudioKey.None;
    }

    // 현재 생존자 성별에 맞는 신음소리 AudioKey를 반환한다.
    private AudioKey GetGroanSoundKey()
    {
        if (gender == SurvivorGender.Male)
            return maleGroanSoundKey;

        if (gender == SurvivorGender.Female)
            return femaleGroanSoundKey;

        return AudioKey.None;
    }

    // Injured / Downed / Imprisoned 상태일 때 2초마다 성별에 맞는 신음소리를 3D로 재생한다.
    [Server]
    private void UpdateGroanSound()
    {
        // 사망 / 탈출 중에는 신음소리를 내지 않는다.
        if (IsDead || IsEscaping)
        {
            groanTimer = 0f;
            return;
        }

        // 다침 / 다운 / 감옥 상태일 때만 신음소리를 낸다.
        bool shouldGroan = IsInjured || IsDowned || IsImprisoned;

        if (!shouldGroan)
        {
            groanTimer = 0f;
            return;
        }

        AudioKey groanKey = GetGroanSoundKey();

        if (groanKey == AudioKey.None)
            return;

        groanTimer -= Time.deltaTime;

        if (groanTimer > 0f)
            return;

        // 신음소리는 3D로 재생하고 Max Distance를 짧게 잡아서 가까운 사람만 듣게 한다.
        PlayWorld3DSound(groanKey);

        groanTimer = groanInterval;
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