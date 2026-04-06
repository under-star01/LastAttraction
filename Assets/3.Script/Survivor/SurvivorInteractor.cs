using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SurvivorInteractor : NetworkBehaviour
{
    private SurvivorInput input;
    private SurvivorState state;

    private IInteractable currentInteractable;
    private bool isInteracting;

    [Header("UI")]
    [SerializeField] private ProgressUI progressUI; // 이 로컬 플레이어가 사용할 진행도 UI

    // 현재 ProgressUI를 사용 중인 오브젝트
    // 예: EvidencePoint, SurvivorHeal
    private object progressOwner;

    public bool IsInteracting => isInteracting;

    public ProgressUI ProgressUI
    {
        get
        {
            if (progressUI == null)
                BindUI();

            return progressUI;
        }
    }

    // 현재 내가 잡고 있는 상호작용 대상인지 확인
    public bool IsCurrentInteractable(IInteractable interactable)
    {
        return currentInteractable == interactable;
    }

    private void Awake()
    {
        input = GetComponent<SurvivorInput>();
        state = GetComponent<SurvivorState>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 씬이 바뀔 때마다 UI를 다시 연결
        SceneManager.sceneLoaded += OnScene;
    }

    public override void OnStopClient()
    {
        SceneManager.sceneLoaded -= OnScene;
        base.OnStopClient();
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // 로컬 플레이어 생성 시 UI 연결
        BindUI();
        ForceHideProgress();
    }

    private void OnScene(Scene scene, LoadSceneMode mode)
    {
        if (!isLocalPlayer)
            return;

        // 씬 전환 후 다시 연결
        BindUI();
        ForceHideProgress();
    }

    private void Update()
    {
        // 로컬 플레이어만 상호작용 입력 처리
        if (!isLocalPlayer)
            return;

        // 아직 UI를 못 잡았으면 계속 재시도
        if (progressUI == null)
            BindUI();

        // 다운 상태면 상호작용 강제 종료
        if (state != null && state.IsDowned)
        {
            ClearForce();
            return;
        }

        // 상호작용 중이 아닐 때 앉아 있으면 시작 막기
        if (!isInteracting && input != null && input.IsCrouching)
            return;

        HandleInteract();
    }

    // ProgressUI 연결
    private void BindUI()
    {
        // 1순위: Binder에서 가져오기
        if (LobbySceneBinder.Instance != null)
        {
            progressUI = LobbySceneBinder.Instance.GetProgressUI();
        }

        // 2순위: 씬에서 직접 찾기
        if (progressUI == null)
        {
            progressUI = FindFirstObjectByType<ProgressUI>(FindObjectsInactive.Include);
        }
    }

    // 진행도 UI 표시/업데이트
    // owner가 같을 때만 갱신하게 해서
    // 다른 상호작용이 UI를 덮어쓰는 문제를 줄임
    public void ShowProgress(object owner, float value)
    {
        if (!isLocalPlayer)
            return;

        if (progressUI == null)
            BindUI();

        if (progressUI == null)
            return;

        // 이미 다른 오브젝트가 사용 중이면 무시
        if (progressOwner != null && progressOwner != owner)
            return;

        progressOwner = owner;

        progressUI.Show();
        progressUI.SetProgress(value);
    }

    // 진행도 UI 숨기기
    // reset = true 일 때만 게이지를 0으로 초기화
    public void HideProgress(object owner, bool reset)
    {
        if (!isLocalPlayer)
            return;

        if (progressUI == null)
            return;

        // 현재 owner가 아니면 숨기지 않음
        if (progressOwner != owner)
            return;

        progressOwner = null;

        if (reset)
            progressUI.ResetUI();
        else
            progressUI.Hide();
    }

    // 강제로 UI 정리
    public void ForceHideProgress()
    {
        progressOwner = null;

        if (progressUI != null)
            progressUI.ResetUI();
    }

    // 현재 상호작용 대상 타입에 따라 처리
    private void HandleInteract()
    {
        if (currentInteractable == null)
        {
            isInteracting = false;
            return;
        }

        if (currentInteractable.InteractType == InteractType.Hold)
            HandleHold();
        else
            HandlePress();
    }

    // Hold 타입 처리
    private void HandleHold()
    {
        if (input == null)
            return;

        if (input.IsInteracting1)
        {
            if (!isInteracting && !input.IsCrouching)
            {
                isInteracting = true;
                currentInteractable.BeginInteract();
            }
        }
        else
        {
            if (isInteracting)
            {
                isInteracting = false;
                currentInteractable.EndInteract();
            }
        }
    }

    // Press 타입 처리
    private void HandlePress()
    {
        if (input == null)
            return;

        if (input.IsCrouching)
            return;

        if (input.IsInteracting2)
            currentInteractable.BeginInteract();
    }

    // 상호작용 가능 대상 등록
    public void SetInteractable(IInteractable interactable)
    {
        if (!isLocalPlayer)
            return;

        if (!enabled)
            return;

        if (state != null && state.IsDowned)
            return;

        if (progressUI == null)
            BindUI();

        currentInteractable = interactable;
    }

    // 상호작용 대상 해제
    public void ClearInteractable(IInteractable interactable)
    {
        if (!isLocalPlayer)
            return;

        if (currentInteractable != interactable)
            return;

        if (isInteracting)
        {
            isInteracting = false;
            currentInteractable.EndInteract();
        }

        currentInteractable = null;
    }

    private void OnDisable()
    {
        ClearForce();
    }

    // 강제 정리
    private void ClearForce()
    {
        if (isInteracting && currentInteractable != null)
        {
            isInteracting = false;
            currentInteractable.EndInteract();
        }

        currentInteractable = null;
        ForceHideProgress();
    }
}