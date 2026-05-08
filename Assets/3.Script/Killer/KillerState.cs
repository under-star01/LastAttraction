using UnityEngine;
using Mirror;
using System.Collections;
using UnityEngine.Rendering;          // GraphicsSettings 참조용
using UnityEngine.Rendering.Universal;

public enum KillerCondition
{
    Idle,
    Lunging,
    Recovering,
    Hit,
    Vaulting,
    Breaking,
    Incage,
    Planting,
    Lobby
}

public class KillerState : NetworkBehaviour
{
    private Animator animator;

    [Header("Rage 파티클")]
    [SerializeField] private ParticleSystem rageParticle;

    [Header("Sync Variables")]
    [SyncVar(hook = nameof(OnConditionChanged))]
    private KillerCondition currentCondition = KillerCondition.Lobby;
    public KillerCondition CurrentCondition => currentCondition;

    [SyncVar(hook = nameof(OnRageChanged))]
    [SerializeField] private bool isRaging = false;
    public bool IsRaging => isRaging;

    [Header("분노(Rage) 설정")]
    [SerializeField] private float rageBuildThreshold = 1.0f; // 1초간 촬영 당하면 분노
    [SerializeField] private float rageDuration = 10.0f;       // Rage 유지 시간
    [SerializeField] private float detectRange = 15f;          // 감지 거리
    [SerializeField] private LayerMask survivorLayer;          // 생존자 레이어

    [Header("Rage 오디오")]
    [SerializeField] private AudioKey rageStartSoundKey = AudioKey.KillerRageStart;
    [SerializeField] private Vector3 rageStartSoundOffset = new Vector3(0f, 1.2f, 0f);

    private float currentRageBuildTime = 0f;
    private Coroutine rageTimerCoroutine;

    private ScriptableRendererFeature rageEffectFeature; // URP 전용 피처

    public bool CanMove =>
        currentCondition == KillerCondition.Idle ||
        currentCondition == KillerCondition.Lunging ||
        currentCondition == KillerCondition.Recovering ||
        currentCondition == KillerCondition.Planting;

    public bool CanLook =>
        currentCondition != KillerCondition.Hit &&
        currentCondition != KillerCondition.Vaulting &&
        currentCondition != KillerCondition.Breaking &&
        currentCondition != KillerCondition.Incage &&
        currentCondition != KillerCondition.Lobby;

    public bool CanAttack => currentCondition == KillerCondition.Idle;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();

        InitURPFeature();

        if (rageParticle == null)
        {
            Transform fxTransform = transform.Find("Fire Effects White");

            if (fxTransform != null)
                rageParticle = fxTransform.GetComponent<ParticleSystem>();
        }

        // 게임 시작 시 Rage 파티클은 꺼진 상태로 보장한다.
        if (rageParticle != null)
            rageParticle.Stop();
    }

    public override void OnStopServer()
    {
        StopRageTimerServer();

        base.OnStopServer();
    }

    private void Update()
    {
        if (!isLocalPlayer)
            return;

        // 테스트용 Rage 발동
        if (Input.GetKeyDown(KeyCode.Alpha1))
            CmdTestActivateRage();

        if (!isRaging)
            CheckRageBuild();
    }

    private void InitURPFeature()
    {
        // 현재 사용 중인 파이프라인 에셋 가져오기
        var pipeline = GraphicsSettings.currentRenderPipeline;

        if (pipeline is UniversalRenderPipelineAsset urpAsset)
        {
            // UniversalRenderPipelineAsset의 RendererDataList에 접근해서
            // 이름이 RageScreenFeature인 Renderer Feature를 찾는다.
            var rendererDataList = typeof(UniversalRenderPipelineAsset)
                .GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(urpAsset) as ScriptableRendererData[];

            if (rendererDataList != null)
            {
                foreach (var rendererData in rendererDataList)
                {
                    if (rendererData == null)
                        continue;

                    foreach (var feature in rendererData.rendererFeatures)
                    {
                        if (feature != null && feature.name == "RageScreenFeature")
                        {
                            rageEffectFeature = feature;

                            // 초기 상태는 꺼둔다.
                            rageEffectFeature.SetActive(false);
                            return;
                        }
                    }
                }
            }
        }
    }

    [Server]
    public void ChangeState(KillerCondition newState)
    {
        if (currentCondition == newState)
            return;

        currentCondition = newState;
    }

    private void OnConditionChanged(KillerCondition oldState, KillerCondition newState)
    {
        if (isServer && !isClient)
            return;

        if (isLocalPlayer)
        {
            // 로컬 킬러 화면에서는 서버가 바꾼 주요 상태에 맞춰 애니메이션 트리거를 실행한다.
            if (newState == KillerCondition.Hit ||
                newState == KillerCondition.Recovering ||
                newState == KillerCondition.Incage)
            {
                PlayTrigger(newState);
            }
        }
        else
        {
            // 타인 화면에서는 모든 상태 변화에 대해 트리거를 시도한다.
            PlayTrigger(newState);
        }
    }

    public void PlayTrigger(KillerCondition condition)
    {
        if (animator == null)
            return;

        switch (condition)
        {
            // Lunging은 bool 값에 의한 Run 애니메이션이므로 트리거를 쓰지 않는다.
            case KillerCondition.Recovering:
                // 공격 후딜레이 상태가 될 때 실제 공격 휘두르기 애니메이션이 나온다.
                animator.SetTrigger("Attack");
                break;

            case KillerCondition.Hit:
                animator.SetTrigger("Hit");
                break;

            case KillerCondition.Breaking:
                animator.SetTrigger("Break");
                break;

            case KillerCondition.Vaulting:
                animator.SetTrigger("Vault");
                break;

            case KillerCondition.Incage:
                animator.SetTrigger("Incage");
                break;
        }
    }

    private void CheckRageBuild()
    {
        Ray ray = new Ray(transform.position + Vector3.up * 1.5f, transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, detectRange, survivorLayer))
        {
            SurvivorCameraSkill survivorCam = hit.collider.GetComponentInParent<SurvivorCameraSkill>();

            if (survivorCam != null && survivorCam.IsRecordingKiller)
            {
                currentRageBuildTime += Time.deltaTime;

                if (currentRageBuildTime >= rageBuildThreshold)
                {
                    currentRageBuildTime = 0f;
                    CmdSetRage(true);
                }

                return;
            }
        }

        currentRageBuildTime = 0f;
    }

    [Command]
    private void CmdSetRage(bool value)
    {
        if (isRaging == value)
            return;

        isRaging = value;

        if (value)
        {
            currentRageBuildTime = 0f;

            // Rage가 실제로 켜지는 서버 순간에만 시작 소리를 1회 재생한다.
            ServerPlayRageStartSound();

            StartRageTimerServer();
        }
        else
        {
            currentRageBuildTime = 0f;
            StopRageTimerServer();
        }
    }

    [Server]
    private void StartRageTimerServer()
    {
        if (rageTimerCoroutine != null)
            StopCoroutine(rageTimerCoroutine);

        rageTimerCoroutine = StartCoroutine(RageTimerRoutine());
    }

    [Server]
    private void StopRageTimerServer()
    {
        if (rageTimerCoroutine != null)
        {
            StopCoroutine(rageTimerCoroutine);
            rageTimerCoroutine = null;
        }
    }

    [Server]
    private IEnumerator RageTimerRoutine()
    {
        yield return new WaitForSeconds(rageDuration);

        rageTimerCoroutine = null;
        currentRageBuildTime = 0f;
        isRaging = false;
    }

    // 서버에서 Rage가 실제로 켜지는 순간 3D 사운드를 한 번 재생한다.
    [Server]
    private void ServerPlayRageStartSound()
    {
        if (NetworkAudioManager.Instance == null)
            return;

        if (rageStartSoundKey == AudioKey.None)
            return;

        NetworkAudioManager.PlayAudioForEveryone(
            rageStartSoundKey,
            AudioDimension.Sound3D,
            transform.position + rageStartSoundOffset
        );
    }

    [Command]
    public void CmdChangeKillerState(KillerCondition newState)
    {
        // 클라이언트의 요청을 받아 서버에서 실제 상태를 변경한다.
        ChangeState(newState);
    }

    [Server]
    public void ActivateRage()
    {
        if (isRaging)
            return;

        isRaging = true;
        currentRageBuildTime = 0f;

        // 테스트 / 외부 호출로 Rage가 켜져도 동일하게 소리를 재생한다.
        ServerPlayRageStartSound();

        StartRageTimerServer();
    }

    [Command]
    private void CmdTestActivateRage()
    {
        ActivateRage();
    }

    private void OnRageChanged(bool oldVal, bool newVal)
    {
        // 붉은 화면 효과는 오직 킬러 본인 화면에서만 적용한다.
        if (isLocalPlayer && rageEffectFeature != null)
            rageEffectFeature.SetActive(newVal);

        // Rage 파티클은 모든 클라이언트에서 상태에 맞춰 켜고 끈다.
        if (rageParticle != null)
        {
            if (newVal)
                rageParticle.Play();
            else
                rageParticle.Stop();
        }

        // Rage가 활성화되었을 때 생존자 탐지 효과를 켠다.
        KillerDetector detector = GetComponent<KillerDetector>();

        if (newVal)
            detector?.SetActive(true);
        else
            detector?.SetActive(false);
    }
}