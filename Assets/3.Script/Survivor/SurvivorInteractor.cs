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
    [SerializeField] private ProgressUI progressUI;

    public bool IsInteracting => isInteracting;
    public ProgressUI ProgressUI => progressUI;

    private void Awake()
    {
        input = GetComponent<SurvivorInput>();
        state = GetComponent<SurvivorState>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 씬이 바뀔 때마다 다시 UI를 잡을 수 있게 등록
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public override void OnStopClient()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        base.OnStopClient();
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // 로컬 플레이어 생성 시 1차 연결
        TryBindProgressUI();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 로컬 플레이어만 씬 전환 후 다시 UI 연결
        if (!isLocalPlayer)
            return;

        TryBindProgressUI();
    }

    private void Update()
    {
        if (!isLocalPlayer)
            return;

        // 혹시 씬 로드 타이밍 때문에 아직 못 잡았으면 계속 재시도
        if (progressUI == null)
        {
            TryBindProgressUI();
        }

        if (state != null && state.IsDowned)
        {
            ForceClear();
            return;
        }

        if (!isInteracting && input != null && input.IsCrouching)
            return;

        HandleInteraction();
    }

    // 현재 씬의 Binder에서 ProgressUI를 다시 찾아 연결
    private void TryBindProgressUI()
    {
        if (LobbySceneBinder.Instance == null)
            return;

        progressUI = LobbySceneBinder.Instance.GetProgressUI();

        Debug.Log($"[SurvivorInteractor] ProgressUI 연결: {progressUI}");
    }

    private void HandleInteraction()
    {
        if (currentInteractable == null)
        {
            isInteracting = false;
            return;
        }

        if (currentInteractable.InteractType == InteractType.Hold)
            HandleHoldInteraction();
        else
            HandlePressInteraction();
    }

    private void HandleHoldInteraction()
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

    private void HandlePressInteraction()
    {
        if (input == null)
            return;

        if (input.IsCrouching)
            return;

        if (input.IsInteracting2)
        {
            currentInteractable.BeginInteract();
        }
    }

    public void SetInteractable(IInteractable interactable)
    {
        if (!isLocalPlayer)
            return;

        if (!enabled)
            return;

        if (state != null && state.IsDowned)
            return;

        currentInteractable = interactable;
    }

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
        ForceClear();
    }

    private void ForceClear()
    {
        if (isInteracting && currentInteractable != null)
        {
            isInteracting = false;
            currentInteractable.EndInteract();
        }

        currentInteractable = null;
    }
}