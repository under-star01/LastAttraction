using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 인게임 UI 전체를 관리한다.
// - 모든 생존자의 상태 UI
// - 생존자 입장에서만 모든 생존자의 현재 행동 UI 표시
// - 살인마 입장에서는 생존자 상태 UI만 표시하고 Action UI는 숨김
// - 내 로컬 플레이어의 클릭 / 우클릭 입력 UI
public class InGameUIManager : MonoBehaviour
{
    public static InGameUIManager Instance { get; private set; }

    [Header("공통 상태 UI")]
    [SerializeField] private GameObject survivorSlotsObject;
    [SerializeField] private SurvivorPlayerUI[] survivorSlots; // Survivor1~4 슬롯

    [Header("진행 / 상호작용 UI")]
    [SerializeField] private ProgressUI progressUI;
    [SerializeField] private QTEUI qteUI;

    [Header("생존자 전용 UI")]
    [SerializeField] private LocalActionUI localActionUI;
    [SerializeField] private GameObject objectiveProgressUIObject;
    [SerializeField] private GameObject skillUI_Survivor;
    [SerializeField] private CameraSkillUI cameraSkillUI;
    [SerializeField] private Image[] frameUI;

    [Header("살인마 전용 UI")]
    [SerializeField] private GameObject skillUI_Killer;
    [SerializeField] private KillerSkillUI killerSkillUI;

    [Header("결과 UI")]
    [SerializeField] private GameObject resultUI;

    [Header("상태 대체 아이콘")]
    [SerializeField] private Sprite downedIcon;
    [SerializeField] private Sprite imprisonedIcon;
    [SerializeField] private Sprite escapedIcon;
    [SerializeField] private Sprite deadIcon;

    [Header("행동 아이콘")]
    [SerializeField] private Sprite noneActionIcon;
    [SerializeField] private Sprite cameraActionIcon;
    [SerializeField] private Sprite evidenceActionIcon;
    [SerializeField] private Sprite healGiveActionIcon;
    [SerializeField] private Sprite healReceiveActionIcon;
    [SerializeField] private Sprite prisonActionIcon;
    [SerializeField] private Sprite uploadActionIcon;

    [Header("갱신 설정")]
    [SerializeField] private float refreshInterval = 0.1f;

    [Header("Post Processing")]
    [SerializeField] private Volume globalVolume;

    private float refreshTimer;

    private readonly List<SurvivorState> survivors = new List<SurvivorState>();

    private SurvivorHeal[] heals = new SurvivorHeal[0];
    private EvidencePoint[] evidences = new EvidencePoint[0];
    private Prison[] prisons = new Prison[0];
    private UploadComputer[] uploadComputers = new UploadComputer[0];

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        RefreshSceneObjects();
        HideAllSlots();

        if (resultUI != null)
            resultUI.SetActive(false);

        if (CustomNetworkManager.Instance != null)
            SetRoleUI(CustomNetworkManager.Instance.CurrentLocalJoinRole);
        else
            SetRoleUI(JoinRole.None);
    }

    private void Update()
    {
        // 매 프레임 FindObjects를 하면 비용이 크므로 일정 간격으로만 씬 오브젝트를 다시 찾는다.
        refreshTimer += Time.deltaTime;

        if (refreshTimer >= refreshInterval)
        {
            refreshTimer = 0f;
            RefreshSceneObjects();
        }

        UpdateSurvivorListUI();
        UpdateLocalActionUI();
    }

    public ProgressUI GetProgressUI()
    {
        return progressUI;
    }

    public QTEUI GetQTEUI()
    {
        return qteUI;
    }

    public CameraSkillUI GetCameraSkillUI()
    {
        return cameraSkillUI;
    }

    public Image[] GetFrameUI()
    {
        return frameUI;
    }

    public KillerSkillUI GetKillerSkillUI()
    {
        return killerSkillUI;
    }

    // 씬 안의 생존자와 상호작용 오브젝트를 찾는다.
    private void RefreshSceneObjects()
    {
        survivors.Clear();

        SurvivorState[] foundSurvivors = FindObjectsByType<SurvivorState>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        for (int i = 0; i < foundSurvivors.Length; i++)
        {
            if (foundSurvivors[i] != null)
                survivors.Add(foundSurvivors[i]);
        }

        // netId 기준으로 정렬해서 각 클라이언트에서 UI 순서가 최대한 같게 보이게 한다.
        survivors.Sort((a, b) => a.netId.CompareTo(b.netId));

        heals = FindObjectsByType<SurvivorHeal>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        evidences = FindObjectsByType<EvidencePoint>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        prisons = FindObjectsByType<Prison>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        uploadComputers = FindObjectsByType<UploadComputer>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );
    }

    // 생존자 리스트 UI 갱신
    private void UpdateSurvivorListUI()
    {
        if (survivorSlots == null || survivorSlots.Length == 0)
            return;

        if (!ShouldShowSurvivorList())
        {
            HideAllSlots();
            return;
        }

        bool canSeeAction = IsLocalSurvivor();

        for (int i = 0; i < survivorSlots.Length; i++)
        {
            SurvivorPlayerUI slot = survivorSlots[i];

            if (slot == null)
                continue;

            if (i >= survivors.Count || survivors[i] == null)
            {
                slot.SetVisible(false);
                continue;
            }

            SurvivorState survivor = survivors[i];

            slot.SetVisible(true);

            // DB 닉네임과 프리팹별 초상화는 PlayerUIProfile에서 가져온다.
            slot.SetName(GetSurvivorName(survivor));
            slot.SetPortrait(GetPortrait(survivor));

            // 건강 / 부상 / 다운 / 감옥 / 탈출 / 사망 상태 표시
            ApplyConditionUI(slot, survivor);

            // 감옥 단계와 감옥 시간 UI는 생존자와 살인마 모두 볼 수 있다.
            slot.SetCatchCount(GetCatchCountForUI(survivor));
            UpdatePrisonTimer(slot, survivor);

            // 생존자만 Action UI를 볼 수 있다.
            // 살인마는 상태 UI는 보지만, 생존자가 무엇을 하는지는 볼 수 없다.
            if (canSeeAction)
                UpdateActionSlot(slot, survivor);
            else
                slot.SetAction(null, false, 0f);
        }
    }

    // 생존자 리스트 자체를 보여줄지 판단한다.
    private bool ShouldShowSurvivorList()
    {
        if (NetworkClient.localPlayer == null)
            return false;

        // 생존자는 생존자 UI를 본다.
        if (IsLocalSurvivor())
            return true;

        // 살인마도 생존자 상태 UI는 본다.
        if (IsLocalKiller())
            return true;

        return false;
    }

    // 현재 로컬 플레이어가 생존자인지 확인한다.
    private bool IsLocalSurvivor()
    {
        if (NetworkClient.localPlayer == null)
            return false;

        return NetworkClient.localPlayer.GetComponent<SurvivorState>() != null;
    }

    // 현재 로컬 플레이어가 살인마인지 확인한다.
    private bool IsLocalKiller()
    {
        if (NetworkClient.localPlayer == null)
            return false;

        return NetworkClient.localPlayer.GetComponent<KillerInput>() != null;
    }

    public void SetRoleUI(JoinRole role)
    {
        bool isSurvivor = role == JoinRole.Survivor;
        bool isKiller = role == JoinRole.Killer;

        if (skillUI_Survivor != null)
            skillUI_Survivor.SetActive(isSurvivor);

        if (skillUI_Killer != null)
            skillUI_Killer.SetActive(isKiller);

        if (objectiveProgressUIObject != null)
            objectiveProgressUIObject.SetActive(isSurvivor);


        ApplyVignetteByRole();
    }

    private void HideAllSlots()
    {
        if (survivorSlots == null)
            return;

        for (int i = 0; i < survivorSlots.Length; i++)
        {
            if (survivorSlots[i] == null)
                continue;

            survivorSlots[i].Clear();
            survivorSlots[i].SetVisible(false);
        }
    }

    public void ShowResultUI()
    {
        if (objectiveProgressUIObject != null)
            objectiveProgressUIObject.SetActive(false);

        if (survivorSlotsObject != null)
            survivorSlotsObject.SetActive(false);

        if (skillUI_Survivor != null)
            skillUI_Survivor.SetActive(false);

        if (skillUI_Killer != null)
            skillUI_Killer.SetActive(false);

        if (progressUI != null)
            progressUI.gameObject.SetActive(false);

        if (qteUI != null)
            qteUI.gameObject.SetActive(false);

        if (cameraSkillUI != null)
            cameraSkillUI.gameObject.SetActive(false);

        if (frameUI != null)
        {
            for (int i = 0; i < frameUI.Length; i++)
            {
                if (frameUI[i] != null)
                    frameUI[i].gameObject.SetActive(false);
            }
        }

        if (resultUI != null)
            resultUI.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log("[InGameUIManager] 기존 UI 비활성화 / ResultUI 활성화");
    }

    public void OnClickReturnLobby()
    {
        if (CustomNetworkManager.Instance == null)
            return;

        CustomNetworkManager.Instance.LeaveCurrentGameToLobby();
    }

    // DB 닉네임 가져오기
    private string GetSurvivorName(SurvivorState survivor)
    {
        if (survivor == null)
            return "NickName";

        PlayerUIProfile profile = survivor.GetComponent<PlayerUIProfile>();

        if (profile == null)
            profile = survivor.GetComponentInChildren<PlayerUIProfile>();

        if (profile == null)
            return "NickName";

        return profile.DisplayName;
    }

    // 생존자 프리팹별 초상화 가져오기
    private Sprite GetPortrait(SurvivorState survivor)
    {
        if (survivor == null)
            return null;

        PlayerUIProfile profile = survivor.GetComponent<PlayerUIProfile>();

        if (profile == null)
            profile = survivor.GetComponentInChildren<PlayerUIProfile>();

        if (profile == null)
            return null;

        return profile.Portrait;
    }

    // 생존자 몸 상태 UI 처리
    private void ApplyConditionUI(SurvivorPlayerUI slot, SurvivorState survivor)
    {
        if (slot == null || survivor == null)
            return;

        // 현재 SurvivorState는 탈출 상태를 enum이 아니라 bool isEscaping으로 관리한다.
        // 그래서 enum 상태보다 먼저 검사해야 한다.
        if (survivor.IsEscaping)
        {
            // 탈출 상태는 초상화를 탈출 아이콘으로 대체한다.
            slot.SetConditionUI(
                showPortrait: false,
                showInjury: false,
                replaceSprite: escapedIcon
            );

            return;
        }

        switch (survivor.CurrentCondition)
        {
            case SurvivorCondition.Healthy:
                // 건강 상태는 생존자 기본 초상화만 보여준다.
                slot.SetConditionUI(
                    showPortrait: true,
                    showInjury: false,
                    replaceSprite: null
                );
                break;

            case SurvivorCondition.Injured:
                // 부상 상태는 기본 초상화 위에 상처 오버레이만 덮는다.
                slot.SetConditionUI(
                    showPortrait: true,
                    showInjury: true,
                    replaceSprite: null
                );
                break;

            case SurvivorCondition.Downed:
                // 다운 상태는 초상화를 다운 아이콘으로 대체한다.
                slot.SetConditionUI(
                    showPortrait: false,
                    showInjury: false,
                    replaceSprite: downedIcon
                );
                break;

            case SurvivorCondition.Imprisoned:
                // 감옥 상태는 초상화를 감옥 아이콘으로 대체한다.
                slot.SetConditionUI(
                    showPortrait: false,
                    showInjury: false,
                    replaceSprite: imprisonedIcon
                );
                break;

            case SurvivorCondition.Dead:
                // 사망 상태는 초상화를 사망 아이콘으로 대체한다.
                slot.SetConditionUI(
                    showPortrait: false,
                    showInjury: false,
                    replaceSprite: deadIcon
                );
                break;
        }
    }

    // 감옥 남은 시간 Slider 갱신
    private void UpdatePrisonTimer(SurvivorPlayerUI slot, SurvivorState survivor)
    {
        if (slot == null || survivor == null)
            return;

        Prison prison = GetCurrentPrison(survivor);

        bool show = survivor.IsImprisoned && prison != null;
        float remain01 = show ? prison.RemainTime01 : 0f;

        slot.SetPrisonTimer(show, remain01);
    }

    // 현재 행동 아이콘과 진행도 갱신
    // 이 함수는 생존자 입장에서만 호출된다.
    private void UpdateActionSlot(SurvivorPlayerUI slot, SurvivorState survivor)
    {
        if (slot == null || survivor == null)
            return;

        uint id = survivor.netId;

        // 1. 카메라 스킬 사용 중
        SurvivorCameraSkill cameraSkill = survivor.GetComponent<SurvivorCameraSkill>();
        if (cameraSkill != null && cameraSkill.IsUse)
        {
            slot.SetAction(cameraActionIcon, false, 0f);
            return;
        }

        // 2. 힐 주는 중 / 힐 받는 중
        SurvivorHeal heal = GetHealBySurvivor(id, out bool isHealer, out bool isTarget);
        if (heal != null)
        {
            if (isHealer)
            {
                slot.SetAction(healGiveActionIcon, true, heal.Progress01);
                return;
            }

            if (isTarget)
            {
                slot.SetAction(healReceiveActionIcon, true, heal.Progress01);
                return;
            }
        }

        // 3. 증거 수집 중
        EvidencePoint evidence = GetEvidenceByUser(id);
        if (evidence != null)
        {
            slot.SetAction(evidenceActionIcon, true, evidence.Progress01);
            return;
        }

        // 4. 감옥 상호작용 중
        Prison prison = GetPrisonInteractByUser(id);
        if (prison != null)
        {
            slot.SetAction(prisonActionIcon, true, prison.Progress01);
            return;
        }

        // 5. 컴퓨터 업로드 중
        UploadComputer computer = GetUploadByUser(id);
        if (computer != null)
        {
            slot.SetAction(uploadActionIcon, true, computer.UploadProgress01);
            return;
        }

        // 6. 아무 행동 없음
        slot.SetAction(noneActionIcon, false, 0f);
    }

    // 감옥 단계 UI 계산
    private int GetCatchCountForUI(SurvivorState survivor)
    {
        if (survivor == null)
            return 0;

        int count = survivor.PrisonStep;

        Prison prison = GetCurrentPrison(survivor);

        if (survivor.IsImprisoned && prison != null)
        {
            // 첫 번째 감옥에 들어간 순간에는 1칸 표시.
            // 첫 번째 감옥에서 시간이 절반 이하가 되면 2칸 표시.
            if (survivor.PrisonStep == 1 && prison.RemainTime <= survivor.PrisonHalfTime)
                count = 2;

            // 두 번째 감옥에 들어간 상태면 2칸 표시.
            if (survivor.PrisonStep >= 2)
                count = 2;
        }

        return Mathf.Clamp(count, 0, 2);
    }

    private Prison GetCurrentPrison(SurvivorState survivor)
    {
        if (survivor == null)
            return null;

        if (!survivor.IsImprisoned)
            return null;

        uint prisonId = survivor.CurrentPrisonId;

        for (int i = 0; i < prisons.Length; i++)
        {
            if (prisons[i] == null)
                continue;

            if (prisons[i].netId == prisonId)
                return prisons[i];
        }

        return null;
    }

    private SurvivorHeal GetHealBySurvivor(uint survivorId, out bool isHealer, out bool isTarget)
    {
        isHealer = false;
        isTarget = false;

        for (int i = 0; i < heals.Length; i++)
        {
            SurvivorHeal heal = heals[i];

            if (heal == null || !heal.IsHealing)
                continue;

            if (heal.HealerNetId == survivorId)
            {
                isHealer = true;
                return heal;
            }

            if (heal.TargetNetId == survivorId)
            {
                isTarget = true;
                return heal;
            }
        }

        return null;
    }

    private EvidencePoint GetEvidenceByUser(uint survivorId)
    {
        for (int i = 0; i < evidences.Length; i++)
        {
            EvidencePoint evidence = evidences[i];

            if (evidence == null)
                continue;

            if (evidence.IsInteractingForUI && evidence.CurrentInteractorNetId == survivorId)
                return evidence;
        }

        return null;
    }

    private Prison GetPrisonInteractByUser(uint survivorId)
    {
        for (int i = 0; i < prisons.Length; i++)
        {
            Prison prison = prisons[i];

            if (prison == null)
                continue;

            if (prison.IsInteractingForUI && prison.CurrentUserId == survivorId)
                return prison;
        }

        return null;
    }

    private UploadComputer GetUploadByUser(uint survivorId)
    {
        for (int i = 0; i < uploadComputers.Length; i++)
        {
            UploadComputer computer = uploadComputers[i];

            if (computer == null)
                continue;

            if (computer.IsUserUploading(survivorId))
                return computer;
        }

        return null;
    }

    // 내 로컬 플레이어의 클릭 / 우클릭 아이콘 흐림 처리
    private void UpdateLocalActionUI()
    {
        if (localActionUI == null)
            return;

        // 살인마는 생존자용 클릭 / 우클릭 UI를 사용하지 않으므로 기본 상태로 둔다.
        if (!IsLocalSurvivor())
        {
            localActionUI.SetClickUsed(false);
            localActionUI.SetRightClickUsed(false);
            return;
        }

        if (NetworkClient.localPlayer == null)
        {
            localActionUI.SetClickUsed(false);
            localActionUI.SetRightClickUsed(false);
            return;
        }

        SurvivorInteractor interactor = NetworkClient.localPlayer.GetComponent<SurvivorInteractor>();
        SurvivorCameraSkill cameraSkill = NetworkClient.localPlayer.GetComponent<SurvivorCameraSkill>();
        SurvivorActionState actionState = NetworkClient.localPlayer.GetComponent<SurvivorActionState>();

        bool clickUsed = false;
        bool rightClickUsed = false;

        // Hold 상호작용 중이면 클릭 아이콘을 흐리게 한다.
        if (interactor != null && interactor.IsInteracting)
            clickUsed = true;

        // DownHit, Stunned, Vault 같은 강한 행동 상태도 클릭 사용 중처럼 표시한다.
        if (actionState != null && actionState.IsBusy)
            clickUsed = true;

        // 카메라 스킬 중이면 우클릭 아이콘을 흐리게 한다.
        if (cameraSkill != null && cameraSkill.IsUse)
            rightClickUsed = true;

        localActionUI.SetClickUsed(clickUsed);
        localActionUI.SetRightClickUsed(rightClickUsed);
    }

    private void ApplyVignetteByRole()
    {
        if (globalVolume == null || globalVolume.profile == null)
            return;

        if (!globalVolume.profile.TryGet(out Vignette vignette))
            return;

        JoinRole role = JoinRole.None;

        if (CustomNetworkManager.Instance != null)
            role = CustomNetworkManager.Instance.CurrentLocalJoinRole;

        if (role == JoinRole.Killer)
        {
            vignette.intensity.Override(0.5f);
            vignette.smoothness.Override(0.15f);
        }
        else if (role == JoinRole.Survivor)
        {
            vignette.intensity.Override(0.55f);
            vignette.smoothness.Override(0.2f);
        }
    }
}