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
// 다칠 때 소리, 다운 피격음, 신음소리, 스턴 놀람 소리를 남자 / 여자 캐릭터별로 다르게 재생하기 위해 사용한다.
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
    [SerializeField] private AudioKey maleGroanSoundKey = AudioKey.SurvivorMaleGroan;             // 남자 신음소리 루프
    [SerializeField] private AudioKey femaleGroanSoundKey = AudioKey.SurvivorFemaleGroan;         // 여자 신음소리 루프
    [SerializeField] private AudioKey maleStunSoundKey = AudioKey.SurvivorMaleStun;               // 남자 QTE 실패 / 트랩 스턴 놀람 소리
    [SerializeField] private AudioKey femaleStunSoundKey = AudioKey.SurvivorFemaleStun;           // 여자 QTE 실패 / 트랩 스턴 놀람 소리

    [Header("감옥 시간")]
    [SerializeField] private float prisonFullTime = 120f;
    [SerializeField] private float prisonHalfTime = 60f;

    private SurvivorMove move;
    private SurvivorActionState actionState;

    private int normalLayer;
    private int downedLayer;

    private bool isGroanLoopPlaying;
    private AudioKey currentGroanLoopKey = AudioKey.None;

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

    public override void OnStopServer()
    {
        StopGroanLoopSound();
        base.OnStopServer();
    }

    private void Update()
    {
        // 신음소리는 서버에서만 시작/종료를 판단한다.
        // 실제 재생은 NetworkAudioManager를 통해 모든 클라이언트에서 처리된다.
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
            // 다운 피격 소리는 재생하지 않는다.
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

            if (actionState != null)
                StartCoroutine(actionState.HitRoutine(hitDuration));

            return;
        }

        // Injured -> Downed
        if (currentCondition == SurvivorCondition.Injured)
        {
            currentCondition = SurvivorCondition.Downed;
            ApplyAllStateServer();

            if (GameManager.Instance != null)
                GameManager.Instance.AddKillerResult(addDown: 1);

            PlayWorld3DSound(GetDownHitSoundKey());

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

        // 건강 상태가 되면 신음 루프를 즉시 멈춘다.
        StopGroanLoopSound();

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

        if (GameManager.Instance != null)
            GameManager.Instance.AddKillerResult(addPrison: 1);

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
        if (IsDead)
            return;

        currentPrisonId = 0;
        currentCondition = SurvivorCondition.Dead;

        if (GameManager.Instance != null)
            GameManager.Instance.AddKillerResult(addKill: 1);

        // 죽는 소리는 맞아서 다칠 때 소리와 통일한다.
        // 따라서 성별에 맞는 일반 피격 소리를 재생한다.
        PlayWorld3DSound(GetHitSoundKey());

        // 사망하면 신음 루프를 즉시 멈춘다.
        StopGroanLoopSound();

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

        if (GameManager.Instance != null)
            GameManager.Instance.AddKillerResult(addKill: 1);

        // 감옥 시간 초과 사망도 죽는 소리는 일반 피격 소리와 통일한다.
        // 맞아서 Downed가 되는 상황이 아니므로 성별 다운 피격 소리는 재생하지 않는다.
        PlayWorld3DSound(GetHitSoundKey());

        // 사망하면 신음 루프를 즉시 멈춘다.
        StopGroanLoopSound();

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

        // 탈출 상태에서는 신음 루프를 즉시 멈춘다.
        StopGroanLoopSound();

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

        // 이미 다운 피격 중이거나 스턴 중이면 중복 스턴과 중복 사운드를 막는다.
        if (actionState.CurrentAction == SurvivorAction.DownHit)
            return;

        if (actionState.CurrentAction == SurvivorAction.Stunned)
            return;

        // QTE 실패 / 트랩 밟음으로 실제 스턴이 시작될 때 성별에 맞는 놀람 소리를 재생한다.
        PlayWorld3DSound(GetStunSoundKey());

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

    // 신음 루프 사운드를 시작한다.
    // ownerNetId를 넘겨서 AudioManager가 이 생존자 Transform에 사운드를 붙일 수 있게 한다.
    [Server]
    private void StartGroanLoopSound(AudioKey key)
    {
        if (key == AudioKey.None)
            return;

        NetworkAudioManager.StartLoopAudioForEveryone(
            netId,
            key,
            AudioDimension.Sound3D,
            transform.position
        );

        isGroanLoopPlaying = true;
        currentGroanLoopKey = key;
    }

    // 현재 재생 중인 신음 루프 사운드를 멈춘다.
    [Server]
    private void StopGroanLoopSound()
    {
        if (!isGroanLoopPlaying)
            return;

        if (currentGroanLoopKey != AudioKey.None)
        {
            NetworkAudioManager.StopLoopAudioForEveryone(
                netId,
                currentGroanLoopKey
            );
        }

        isGroanLoopPlaying = false;
        currentGroanLoopKey = AudioKey.None;
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

    // 현재 생존자 성별에 맞는 QTE 실패 / 트랩 스턴 놀람 소리 AudioKey를 반환한다.
    private AudioKey GetStunSoundKey()
    {
        if (gender == SurvivorGender.Male)
            return maleStunSoundKey;

        if (gender == SurvivorGender.Female)
            return femaleStunSoundKey;

        return AudioKey.None;
    }

    // Injured / Downed / Imprisoned 상태일 때 성별에 맞는 신음 루프 사운드를 재생한다.
    // 루프 오브젝트는 AudioManager에서 ownerNetId 오브젝트에 붙기 때문에 생존자를 계속 따라다닌다.
    [Server]
    private void UpdateGroanSound()
    {
        bool shouldGroan = false;

        // 사망 / 탈출 중에는 신음소리를 내지 않는다.
        if (!IsDead && !IsEscaping)
            shouldGroan = IsInjured || IsDowned || IsImprisoned;

        if (!shouldGroan)
        {
            StopGroanLoopSound();
            return;
        }

        AudioKey groanKey = GetGroanSoundKey();

        if (groanKey == AudioKey.None)
        {
            StopGroanLoopSound();
            return;
        }

        // 이미 같은 신음 루프가 재생 중이면 다시 시작하지 않는다.
        if (isGroanLoopPlaying && currentGroanLoopKey == groanKey)
            return;

        // 성별이 바뀌었거나 다른 루프가 켜져 있으면 기존 루프를 끄고 새로 시작한다.
        StopGroanLoopSound();
        StartGroanLoopSound(groanKey);
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