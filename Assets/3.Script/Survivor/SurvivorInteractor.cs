using Mirror;
using UnityEngine;

public class SurvivorInteractor : NetworkBehaviour
{
    private SurvivorInput input;
    private SurvivorState state;

    private IInteractable currentInteractable;
    private bool isInteracting;

    [Header("UI")]
    [SerializeField] private ProgressUI progressUI; // 이 로컬 플레이어가 사용할 진행도 UI

    public bool IsInteracting => isInteracting;

    // 다른 상호작용 스크립트(EvidencePoint, SurvivorHeal)가
    // 현재 로컬 플레이어의 ProgressUI를 가져갈 때 사용
    public ProgressUI ProgressUI => progressUI;

    private void Awake()
    {
        input = GetComponent<SurvivorInput>();
        state = GetComponent<SurvivorState>();
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // 로컬 플레이어가 생성되면
        // 씬에 있는 LobbySceneBinder에서 ProgressUI를 받아온다.
        // 즉, 프리팹에 직접 연결하지 않고 씬의 UI를 로컬 플레이어에게 연결하는 구조다.
        if (LobbySceneBinder.Instance != null)
        {
            progressUI = LobbySceneBinder.Instance.GetProgressUI();
        }
        else
        {
            Debug.LogWarning("[SurvivorInteractor] LobbySceneBinder.Instance가 없습니다.");
        }
    }

    private void Update()
    {
        // 로컬 플레이어만 상호작용 입력 처리
        if (!isLocalPlayer)
            return;

        // 다운 상태면 상호작용 강제 해제
        if (state != null && state.IsDowned)
        {
            ForceClear();
            return;
        }

        // 상호작용 중이 아닐 때 앉아 있으면 Hold/Press 시작 막기
        if (!isInteracting && input != null && input.IsCrouching)
            return;

        HandleInteraction();
    }

    private void HandleInteraction()
    {
        if (currentInteractable == null)
        {
            isInteracting = false;
            return;
        }

        // Hold 타입이면 누르고 있는 동안 유지
        if (currentInteractable.InteractType == InteractType.Hold)
            HandleHoldInteraction();
        // Press 타입이면 1번 누르면 실행
        else
            HandlePressInteraction();
    }

    private void HandleHoldInteraction()
    {
        if (input == null)
            return;

        if (input.IsInteracting1)
        {
            // 아직 상호작용 시작 전이면 BeginInteract 호출
            if (!isInteracting && !input.IsCrouching)
            {
                isInteracting = true;
                currentInteractable.BeginInteract();
            }
        }
        else
        {
            // 버튼을 떼면 EndInteract 호출
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

    // 현재 로컬 플레이어에게 상호작용 가능 대상을 등록
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
        ForceClear();
    }

    // 비활성화될 때 상호작용을 강제로 정리
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