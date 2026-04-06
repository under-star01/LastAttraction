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

    public bool IsInteracting => isInteracting;
    public ProgressUI ProgressUI => progressUI;

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

        // 씬이 바뀔 때마다 UI를 다시 연결할 수 있게 등록
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

        // 로컬 플레이어 생성 시 1차 연결
        BindUI();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!isLocalPlayer)
            return;

        // 씬 전환 후 다시 UI 연결
        BindUI();
    }

    // sceneLoaded 이벤트에서 호출할 함수
    private void OnScene(Scene scene, LoadSceneMode mode)
    {
        OnSceneLoaded(scene, mode);
    }

    private void Update()
    {
        // 로컬 플레이어만 상호작용 입력 처리
        if (!isLocalPlayer)
            return;

        // 씬 로드 타이밍 때문에 아직 UI를 못 잡았으면 재시도
        if (progressUI == null)
            BindUI();

        // 다운 상태면 상호작용 강제 종료
        if (state != null && state.IsDowned)
        {
            ClearForce();
            return;
        }

        // 상호작용 중이 아닐 때 앉아 있으면 상호작용 시작 막기
        if (!isInteracting && input != null && input.IsCrouching)
            return;

        HandleInteract();
    }

    // 현재 씬의 Binder에서 ProgressUI를 다시 찾아 연결
    private void BindUI()
    {
        if (LobbySceneBinder.Instance == null)
            return;

        progressUI = LobbySceneBinder.Instance.GetProgressUI();
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
    // 버튼을 누르고 있는 동안 상호작용 유지
    private void HandleHold()
    {
        if (input == null)
            return;

        if (input.IsInteracting1)
        {
            // 아직 시작 전이면 Begin 호출
            if (!isInteracting && !input.IsCrouching)
            {
                isInteracting = true;
                currentInteractable.BeginInteract();
            }
        }
        else
        {
            // 버튼을 떼면 종료
            if (isInteracting)
            {
                isInteracting = false;
                currentInteractable.EndInteract();
            }
        }
    }

    // Press 타입 처리
    // 버튼을 한 번 눌렀을 때 실행
    private void HandlePress()
    {
        if (input == null)
            return;

        if (input.IsCrouching)
            return;

        if (input.IsInteracting2)
            currentInteractable.BeginInteract();
    }

    // 현재 로컬 플레이어에게 상호작용 가능 대상 등록
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

    // 현재 등록된 상호작용 대상 해제
    public void ClearInteractable(IInteractable interactable)
    {
        if (!isLocalPlayer)
            return;

        if (currentInteractable != interactable)
            return;

        // Hold 상호작용 중이었다면 종료 처리도 같이 함
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

    // 비활성화되거나 다운 상태가 됐을 때 강제 정리
    private void ClearForce()
    {
        if (isInteracting && currentInteractable != null)
        {
            isInteracting = false;
            currentInteractable.EndInteract();
        }

        currentInteractable = null;
    }
}