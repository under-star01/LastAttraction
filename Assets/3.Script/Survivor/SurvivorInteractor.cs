using UnityEngine;

public class SurvivorInteractor : MonoBehaviour
{
    private SurvivorInput input;

    // 현재 상호작용 가능한 대상 1개
    private IInteractable currentInteractable;

    // 현재 Hold 상호작용 진행 중인지
    private bool isInteracting;

    public bool IsInteracting => isInteracting;

    private void Awake()
    {
        input = GetComponent<SurvivorInput>();
    }

    private void Update()
    {
        HandleInteraction();
    }

    // 현재 대상의 타입에 따라 처리 분기
    private void HandleInteraction()
    {
        if (currentInteractable == null)
        {
            isInteracting = false;
            return;
        }

        if (currentInteractable.InteractType == InteractType.Hold)
        {
            HandleHoldInteraction();
        }
        else
        {
            HandlePressInteraction();
        }
    }

    // Hold 타입: 버튼 누르고 있는 동안만 상호작용
    private void HandleHoldInteraction()
    {
        if (input.IsInteracting1)
        {
            if (!isInteracting)
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

    // Press 타입: 버튼 누른 프레임에 즉시 실행
    private void HandlePressInteraction()
    {
        if (input.IsInteracting2)
        {
            currentInteractable.BeginInteract();
        }
    }

    // 상호작용 가능 대상 등록
    public void SetInteractable(IInteractable interactable)
    {
        currentInteractable = interactable;
    }

    // 현재 등록된 대상이 맞을 때만 해제
    public void ClearInteractable(IInteractable interactable)
    {
        if (currentInteractable != interactable)
            return;

        // Hold 중이었다면 정상 종료 처리
        if (isInteracting)
        {
            isInteracting = false;
            currentInteractable.EndInteract();
        }

        currentInteractable = null;
    }
}