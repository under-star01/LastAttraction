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

        // 다운 상태이거나 다운 피격 연출 중이면 상호작용 강제 종료
        if (state != null && (state.IsDowned || state.IsBusy))
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
        // Binder에서 가져오기
        if (LobbySceneBinder.Instance != null)
        {
            progressUI = LobbySceneBinder.Instance.GetProgressUI();
        }

        // 씬에서 직접 찾기
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
            // 대상이 없어졌으면 현재 상호작용 상태도 정리
            if (isInteracting)
            {
                isInteracting = false;
                SetInteractionState(false);
            }

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

        // 다운 상태 또는 다운 피격 연출 중이면 Hold 시작 금지
        if (state != null && (state.IsDowned || state.IsBusy))
            return;

        if (input.IsInteracting1)
        {
            if (!isInteracting && !input.IsCrouching)
            {
                isInteracting = true;

                // 서버에도 현재 상호작용 시작 전달
                SetInteractionState(true);

                currentInteractable.BeginInteract();
            }
        }
        else
        {
            if (isInteracting)
            {
                isInteracting = false;

                // 상호작용 종료를 서버에 전달
                SetInteractionState(false);

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

        // 다운 상태 또는 다운 피격 연출 중이면 Press도 막음
        if (state != null && (state.IsDowned || state.IsBusy))
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

        // 다운 상태이거나 다운 피격 연출 중이면 등록 금지
        if (state != null && (state.IsDowned || state.IsBusy))
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

            // 상호작용이 끊기면 서버 상태도 같이 false
            SetInteractionState(false);

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

            // 강제 종료여도 서버 상태를 false로 내려줘야 함
            SetInteractionState(false);

            currentInteractable.EndInteract();
        }

        currentInteractable = null;
        ForceHideProgress();
    }

    // 현재 플레이어의 상호작용 중 여부를 서버 SurvivorState에 반영
    private void SetInteractionState(bool value)
    {
        if (state == null)
            return;

        // 호스트/서버에서 직접 실행 중이면 바로 반영
        if (isServer)
        {
            state.SetDoingInteractionServer(value);
        }
        // 일반 클라이언트면 Command로 서버에 전달
        else if (isLocalPlayer)
        {
            CmdSetInteractionState(value);
        }
    }

    // 클라이언트 -> 서버
    // 현재 상호작용 중 여부를 서버 SurvivorState에 저장
    [Command]
    private void CmdSetInteractionState(bool value)
    {
        if (state == null)
            return;

        state.SetDoingInteractionServer(value);
    }
}