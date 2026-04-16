using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SurvivorInteractor : NetworkBehaviour
{
    [Header("UI")]
    [SerializeField] private ProgressUI progressUI;
    [SerializeField] private QTEUI qteUI;

    private SurvivorInput input;
    private SurvivorState state;
    private SurvivorActionState actionState;
    private SurvivorMove move;

    // 현재 선택된 상호작용 대상
    private IInteractable currentInteractable;

    // 실제 진행 중인 상호작용 대상
    private IInteractable activeInteractable;

    // Hold 상호작용 중인지
    private bool isInteracting;

    // 현재 ProgressUI를 쓰는 오브젝트
    private object progressOwner;

    // 범위 안 상호작용 대상 목록
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

    public QTEUI QTEUI
    {
        get
        {
            if (qteUI == null)
                BindUI();

            return qteUI;
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
        actionState = GetComponent<SurvivorActionState>();
        move = GetComponent<SurvivorMove>();
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

        if (qteUI != null)
            qteUI.ForceClose(false);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!isLocalPlayer)
            return;

        BindUI();
        ForceHideProgress();

        if (qteUI != null)
            qteUI.ForceClose(false);
    }

    private void Update()
    {
        if (!isLocalPlayer)
            return;

        if (state != null)
        {
            bool isBusy = actionState != null && actionState.IsBusy;

            if (state.IsDowned || isBusy || state.IsDead)
            {
                ClearForce();
                return;
            }
        }

        if (!isInteracting && input != null && input.IsCrouching)
            return;

        RefreshCurrentInteractable();
        HandleInteract();
    }

    private void BindUI()
    {
        if (LobbySceneBinder.Instance != null)
        {
            progressUI = LobbySceneBinder.Instance.GetProgressUI();
            qteUI = LobbySceneBinder.Instance.GetQTEUI();
        }

        if (progressUI == null)
            progressUI = FindFirstObjectByType<ProgressUI>(FindObjectsInactive.Include);

        if (qteUI == null)
            qteUI = FindFirstObjectByType<QTEUI>(FindObjectsInactive.Include);
    }

    public void ShowProgress(object owner, float value)
    {
        if (!isLocalPlayer)
            return;

        if (progressUI == null)
            BindUI();

        if (progressUI == null)
            return;

        if (progressOwner != null && progressOwner != owner)
            return;

        progressOwner = owner;
        progressUI.Show();
        progressUI.SetProgress(value);
    }

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

    // 범위 안 목록에서 가장 우선순위 높은 대상 선택
    private void RefreshCurrentInteractable()
    {
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

            if (!CanUseThis(interactable))
                continue;

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
        if (interactable is Prison)
            return 1000;

        if (interactable is SurvivorHeal)
            return 300;

        if (interactable is EvidencePoint)
            return 200;

        if (interactable is Pallet)
            return 100;

        if (interactable is Window)
            return 100;

        return 0;
    }

    // 감옥 상태일 때는 자기 감옥만 허용
    private bool CanUseThis(IInteractable interactable)
    {
        if (state == null)
            return true;

        if (!state.IsImprisoned)
            return true;

        Prison prison = interactable as Prison;
        if (prison == null)
            return false;

        return prison.netId == state.CurrentPrisonId;
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

        if (state != null)
        {
            bool isBusy = actionState != null && actionState.IsBusy;

            if (state.IsDowned || isBusy || state.IsDead)
                return;
        }

        if (input.IsInteracting1)
        {
            if (!isInteracting && !input.IsCrouching)
            {
                if (currentInteractable == null)
                    return;

                // Hold 상호작용 시작 시 촬영 애니메이션 정리
                if (move != null)
                    move.SetCamAnim(false);

                isInteracting = true;
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

        if (state != null)
        {
            bool isBusy = actionState != null && actionState.IsBusy;

            if (state.IsDowned || isBusy || state.IsDead)
                return;
        }

        if (input.IsInteracting2)
            currentInteractable.BeginInteract(gameObject);
    }

    public void SetInteractable(IInteractable interactable)
    {
        if (!isLocalPlayer)
            return;

        if (!enabled)
            return;

        if (state != null)
        {
            bool isBusy = actionState != null && actionState.IsBusy;

            if (state.IsDowned || isBusy || state.IsDead)
                return;
        }

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

        if (qteUI != null)
            qteUI.ForceClose(false);
    }

    // 행동 상태에 Hold 상호작용 여부 저장
    private void SetInteractionState(bool value)
    {
        if (actionState == null)
            return;

        if (isServer)
        {
            actionState.SetInteract(value);

            if (value)
                actionState.SetCam(false);
        }
        else if (isLocalPlayer)
        {
            CmdSetInteractionState(value);
        }
    }

    // 피격 등으로 현재 상호작용 강제 종료
    public void ForceStopInteract()
    {
        if (isInteracting && activeInteractable != null)
        {
            isInteracting = false;
            SetInteractionState(false);
            activeInteractable.EndInteract();
        }

        activeInteractable = null;
        currentInteractable = null;
        ForceHideProgress();

        if (qteUI != null)
            qteUI.ForceClose(false);
    }

    [Command]
    private void CmdSetInteractionState(bool value)
    {
        if (actionState == null)
            return;

        actionState.SetInteract(value);

        if (value)
            actionState.SetCam(false);
    }
}