using UnityEngine;
using Mirror;
using System.Collections;
using UnityEngine.Rendering;          // GraphicsSettings 참조용
using UnityEngine.Rendering.Universal;

public enum KillerCondition { Idle, Lunging, Recovering, Hit, Vaulting, Breaking, Incage, Planting, Lobby }

public class KillerState : NetworkBehaviour
{
    private Animator animator;

    [Header("Sync Variables")]
    [SyncVar(hook = nameof(OnConditionChanged))]
    private KillerCondition currentCondition = KillerCondition.Lobby;
    public KillerCondition CurrentCondition => currentCondition;

    [SyncVar(hook = nameof(OnRageChanged))]
    [SerializeField] private bool isRaging = false;
    public bool IsRaging => isRaging;

    [Header("분노(Rage) 설정")]
    [SerializeField] private float rageBuildThreshold = 1.0f; // 1초간 촬영 당하면 분노
    [SerializeField] private float rageDuration = 10.0f;       // 10초간 유지
    [SerializeField] private float detectRange = 15f;          // 감지 거리
    [SerializeField] private LayerMask survivorLayer;          // 생존자 레이어

    private float currentRageBuildTime = 0f;

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
    }

    private void Update()
    {
        if (!isLocalPlayer) return;
        if (Input.GetKeyDown(KeyCode.Alpha1)) CmdTestActivateRage();

        if (!isRaging)
        {
            CheckRageBuild();
        }
    }

    private void InitURPFeature()
    {
        // 1. 현재 사용 중인 파이프라인 에셋 가져오기
        var pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline is UniversalRenderPipelineAsset urpAsset)
        {
            // 2. 유니티 6/최신 버전에서 RendererData에 접근하는 올바른 방법
            // 현재 활성화된 렌더러를 가져와서 내부 리스트를 순회합니다.
            // 스크립트에서 직접 필드에 접근하는 대신 아래 방식을 사용합니다.

            // 리플렉션을 사용하지 않고 접근하기 위해 리스트를 직접 참조합니다.
            // (UniversalRenderPipelineAsset의 인스펙터에 등록된 렌더러 리스트)
            var rendererDataList = typeof(UniversalRenderPipelineAsset)
                .GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(urpAsset) as ScriptableRendererData[];

            if (rendererDataList != null)
            {
                foreach (var rendererData in rendererDataList)
                {
                    if (rendererData == null) continue;

                    foreach (var feature in rendererData.rendererFeatures)
                    {
                        if (feature != null && feature.name == "RageScreenFeature")
                        {
                            rageEffectFeature = feature;
                            // 초기 상태는 꺼둠
                            rageEffectFeature.SetActive(false);
                            return; // 찾았으면 종료
                        }
                    }
                }
            }
        }
    }

    [Server]
    public void ChangeState(KillerCondition newState)
    {
        if (currentCondition == newState) return;
        currentCondition = newState;
    }

    private void OnConditionChanged(KillerCondition oldState, KillerCondition newState)
    {
        if (isServer && !isClient) return;

        if (isLocalPlayer)
        {
            // [동기화 해결] 서버가 상태를 바꿨을 때 실행되어야 하는 트리거들
            // 피격(Hit)이나 공격 후딜레이(Recovering) 시작 시 애니메이션을 재생합니다.
            if (newState == KillerCondition.Hit || newState == KillerCondition.Recovering || newState == KillerCondition.Incage)
                PlayTrigger(newState);
        }
        else
        {
            // 타인 화면에서는 모든 상태 변화에 대해 트리거를 시도합니다.
            PlayTrigger(newState);
        }
    }

    public void PlayTrigger(KillerCondition condition)
    {
        if (animator == null) return;

        switch (condition)
        {
            // 런지(Lunging)는 bool 값에 의한 'Run' 애니메이션이므로 트리거를 쓰지 않습니다.
            case KillerCondition.Recovering:
                // 이제 공격 후딜레이 상태가 될 때 실제 공격 휘두르기 애니메이션이 나옵니다.
                animator.SetTrigger("Attack");
                break;
            case KillerCondition.Hit: animator.SetTrigger("Hit"); break;
            case KillerCondition.Breaking: animator.SetTrigger("Break"); break;
            case KillerCondition.Vaulting: animator.SetTrigger("Vault"); break;
            case KillerCondition.Incage: animator.SetTrigger("Incage"); break;
        }
    }

    private void CheckRageBuild()
    {
        Ray ray = new Ray(transform.position + Vector3.up * 1.5f, transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, detectRange, survivorLayer))
        {
            var survivorCam = hit.collider.GetComponentInParent<SurvivorCameraSkill>();
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
        if (isRaging == value) return;
        isRaging = value;

        if (value) StartCoroutine(RageTimerRoutine());
    }

    [Server]
    private IEnumerator RageTimerRoutine()
    {
        yield return new WaitForSeconds(rageDuration);
        isRaging = false;
    }

    [Command]
    public void CmdChangeKillerState(KillerCondition newState)
    {
        // 클라이언트의 요청을 받아 서버에서 실제 상태를 변경합니다.
        ChangeState(newState);
    }

    [Server]
    public void ActivateRage()
    {
        //Debug.Log($"[KillerState] ActivateRage 호출됨 / 현재 isRaging: {isRaging}");
        if (isRaging) return;
        isRaging = true;
        //Debug.Log("[KillerState] isRaging = true 설정 완료");
    }

    [Command]
    private void CmdTestActivateRage()
    {
        //Debug.Log("[KillerState] CmdTestActivateRage 호출됨 (서버)");
        ActivateRage();
    }

    private void OnRageChanged(bool oldVal, bool newVal)
    {
        // 1. 붉은 화면 효과 제어 (오직 본인 화면에서만)
        if (isLocalPlayer && rageEffectFeature != null)
        {
            rageEffectFeature.SetActive(newVal);
        }

        var detector = GetComponent<KillerDetector>();

        // 2. 분노가 활성화되었을 때 Detector 처리
        if (newVal)
        {
            detector?.SetActive(true);
        }
        else
        {
            detector?.SetActive(false);
        }
    }
}