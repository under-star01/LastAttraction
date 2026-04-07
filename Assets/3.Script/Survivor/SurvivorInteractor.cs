using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SurvivorInteractor : NetworkBehaviour
{
    [Header("UI")]
    [SerializeField] private ProgressUI progressUI;

    private SurvivorInput input;
    private SurvivorState state;

    // 현재 선택된 상호작용 대상
    private IInteractable currentInteractable;

    // 현재 실제로 진행 중인 상호작용 대상
    // 한번 Hold를 시작하면 끝날 때까지 이 대상을 유지한다
    private IInteractable activeInteractable;

    // Hold 타입 상호작용 중인지
    private bool isInteracting;

    // 현재 ProgressUI를 사용하는 오브젝트
    private object progressOwner;

    // 범위 안에 있는 상호작용 대상 목록
    private readonly List<IInteractable> nearbyInteractables = new List<IInteractable>();

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
        BindUI();
        ForceHideProgress();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!isLocalPlayer)
            return;

        BindUI();
        ForceHideProgress();
    }

    private void Update()
    {
        if (!isLocalPlayer)
            return;

        if (progressUI == null)
            BindUI();

        // 다운 상태 / 다운 피격 연출 중이면 상호작용 강제 종료
        if (state != null && (state.IsDowned || state.IsBusy))
        {
            ClearForce();
            return;
        }

        // 상호작용 중이 아닐 때만 앉기 상태로 시작 막기
        if (!isInteracting && input != null && input.IsCrouching)
            return;

        RefreshCurrentInteractable();
        HandleInteract();
    }

    private void BindUI()
    {
        if (LobbySceneBinder.Instance != null)
            progressUI = LobbySceneBinder.Instance.GetProgressUI();

        if (progressUI == null)
            progressUI = FindFirstObjectByType<ProgressUI>(FindObjectsInactive.Include);
    }

    public void ShowProgress(object owner, float value)
    {
        if (!isLocalPlayer)
            return;

        if (progressUI == null)
            BindUI();

        if (progressUI == null)
            return;

        // 이미 다른 오브젝트가 UI를 쓰고 있으면 무시
        if (progressOwner != null && progressOwner != owner)
            return;

        progressOwner = owner;
        progressUI.Show();
        progressUI.SetProgress(value);
    }

    // reset 파라미터는 기존 코드 호환용으로 남겨둠
    public void HideProgress(object owner, bool reset)
    {
        if (!isLocalPlayer)
            return;

        if (progressUI == null)
            return;

        if (progressOwner != owner)
            return;

        progressOwner = null;
        progressUI.Hide();

        if (reset)
            progressUI.SetProgress(0f);
    }

    public void ForceHideProgress()
    {
        progressOwner = null;

        if (progressUI != null)
        {
            progressUI.Hide();
            progressUI.SetProgress(0f);
        }
    }

    // 범위 안 목록에서 현재 선택 대상 결정
    private void RefreshCurrentInteractable()
    {
        // 이미 Hold 상호작용 중이면 도중에 대상 바꾸지 않음
        if (isInteracting && activeInteractable != null)
        {
            currentInteractable = activeInteractable;
            return;
        }

        IInteractable best = null;
        int bestPriority = int.MinValue;
        float bestDistance = float.MaxValue;

        for (int i = nearbyInteractables.Count - 1; i >= 0; i--)
        {
            IInteractable interactable = nearbyInteractables[i];

            if (interactable == null)
            {
                nearbyInteractables.RemoveAt(i);
                continue;
            }

            MonoBehaviour behaviour = interactable as MonoBehaviour;
            if (behaviour == null || !behaviour.isActiveAndEnabled)
            {
                nearbyInteractables.RemoveAt(i);
                continue;
            }

            int priority = GetPriority(interactable);
            float sqrDistance = (behaviour.transform.position - transform.position).sqrMagnitude;

            if (best == null)
            {
                best = interactable;
                bestPriority = priority;
                bestDistance = sqrDistance;
                continue;
            }

            if (priority > bestPriority)
            {
                best = interactable;
                bestPriority = priority;
                bestDistance = sqrDistance;
                continue;
            }

            if (priority == bestPriority && sqrDistance < bestDistance)
            {
                best = interactable;
                bestPriority = priority;
                bestDistance = sqrDistance;
            }
        }

        currentInteractable = best;
    }

    // 우선순위
    private int GetPriority(IInteractable interactable)
    {
        if (interactable is SurvivorHeal)
            return 300;

        if (interactable is EvidencePoint)
            return 200;

        if (interactable is Pallet)
            return 110;

        if (interactable is Window)
            return 100;

        return 0;
    }

    private void HandleInteract()
    {
        if (currentInteractable == null)
        {
            if (isInteracting)
            {
                isInteracting = false;
                SetInteractionState(false);

                if (activeInteractable != null)
                    activeInteractable.EndInteract();

                activeInteractable = null;
            }

            return;
        }

        if (currentInteractable.InteractType == InteractType.Hold)
            HandleHold();
        else
            HandlePress();
    }

    private void HandleHold()
    {
        if (input == null)
            return;

        if (state != null && (state.IsDowned || state.IsBusy))
            return;

        if (input.IsInteracting1)
        {
            if (!isInteracting && !input.IsCrouching)
            {
                if (currentInteractable == null)
                    return;

                isInteracting = true;

                // 시작 순간의 대상을 고정
                activeInteractable = currentInteractable;

                SetInteractionState(true);
                activeInteractable.BeginInteract(gameObject);
            }
        }
        else
        {
            if (isInteracting)
            {
                isInteracting = false;
                SetInteractionState(false);

                if (activeInteractable != null)
                    activeInteractable.EndInteract();

                activeInteractable = null;
            }
        }
    }

    private void HandlePress()
    {
        if (input == null)
            return;

        if (input.IsCrouching)
            return;

        if (state != null && (state.IsDowned || state.IsBusy))
            return;

        if (input.IsInteracting2)
            currentInteractable.BeginInteract(gameObject);
    }

    public void SetInteractable(IInteractable interactable)
    {
        if (!isLocalPlayer)
            return;

        if (!enabled)
            return;

        if (state != null && (state.IsDowned || state.IsBusy))
            return;

        if (interactable == null)
            return;

        if (!nearbyInteractables.Contains(interactable))
            nearbyInteractables.Add(interactable);
    }

    public void ClearInteractable(IInteractable interactable)
    {
        if (!isLocalPlayer)
            return;

        if (interactable == null)
            return;

        nearbyInteractables.Remove(interactable);

        // 현재 진행 중인 대상이 사라졌으면 강제 종료
        if (activeInteractable == interactable)
        {
            if (isInteracting)
            {
                isInteracting = false;
                SetInteractionState(false);
                activeInteractable.EndInteract();
            }

            activeInteractable = null;
        }

        if (currentInteractable == interactable)
            currentInteractable = null;
    }

    private void OnDisable()
    {
        ClearForce();
    }

    private void ClearForce()
    {
        if (isInteracting && activeInteractable != null)
        {
            isInteracting = false;
            SetInteractionState(false);
            activeInteractable.EndInteract();
        }

        activeInteractable = null;
        currentInteractable = null;
        nearbyInteractables.Clear();
        ForceHideProgress();
    }

    // 현재 생존자가 Hold 상호작용 중인지 서버에 저장
    private void SetInteractionState(bool value)
    {
        if (state == null)
            return;

        if (isServer)
        {
            state.SetDoingInteractionServer(value);
        }
        else if (isLocalPlayer)
        {
            CmdSetInteractionState(value);
        }
    }

    [Command]
    private void CmdSetInteractionState(bool value)
    {
        if (state == null)
            return;

        state.SetDoingInteractionServer(value);
    }
}