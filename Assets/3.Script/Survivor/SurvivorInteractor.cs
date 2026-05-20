using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SurvivorInteractor : NetworkBehaviour
{
    [Header("UI")]
    [SerializeField] private ProgressUI progressUI;   // Hold 상호작용 진행도 UI
    [SerializeField] private QTEUI qteUI;             // 증거 조사 QTE UI

    [Header("키 안내 UI")]
    [SerializeField] private InteractionPromptUI interactionPromptUI;

    // 생존자 입력 컴포넌트
    private SurvivorInput input;

    // 생존자 몸 상태 컴포넌트
    private SurvivorState state;

    // 생존자 행동 상태 컴포넌트
    private SurvivorActionState actionState;

    // 생존자 이동/애니메이션 제어 컴포넌트
    private SurvivorMove move;

    // 현재 후보로 선택된 상호작용 대상
    private IInteractable currentInteractable;

    // 실제로 진행 중인 상호작용 대상
    // Hold 중에는 후보가 바뀌어도 진행 대상은 유지해야 하므로 따로 저장한다.
    private IInteractable activeInteractable;

    // 현재 Hold 상호작용을 진행 중인지 여부
    private bool isInteracting;

    // Hold 상호작용이 끝난 뒤, 입력키를 한 번 뗄 때까지 다음 Hold 시작을 막는 값
    // 감옥 구출 완료 후 같은 입력이 바로 힐로 이어지는 문제를 막는다.
    private bool waitRelease;

    // 현재 ProgressUI를 점유하고 있는 오브젝트
    // 여러 상호작용이 동시에 UI를 숨기거나 갱신하는 것을 막기 위한 owner 구조다.
    private object progressOwner;

    // 현재 로컬 플레이어 주변에 있는 상호작용 대상 목록
    private readonly List<IInteractable> nearbyInteractables = new List<IInteractable>();

    // 외부에서 현재 Hold 상호작용 중인지 확인할 때 사용
    public bool IsInteracting => isInteracting;

    // ProgressUI 접근용 프로퍼티
    // UI 참조가 사라졌거나 씬이 바뀌었을 수 있으므로 필요할 때 다시 찾는다.
    public ProgressUI ProgressUI
    {
        get
        {
            if (progressUI == null)
                BindUI();

            return progressUI;
        }
    }

    // QTEUI 접근용 프로퍼티
    // 증거 조사에서 QTE UI가 필요할 때 사용한다.
    public QTEUI QTEUI
    {
        get
        {
            if (qteUI == null)
                BindUI();

            return qteUI;
        }
    }

    // 특정 상호작용 대상이 현재 선택된 대상인지 확인한다.
    public bool IsCurrentInteractable(IInteractable interactable)
    {
        return currentInteractable == interactable;
    }

    private void Awake()
    {
        // 같은 생존자 오브젝트에 붙은 컴포넌트들을 캐싱한다.
        input = GetComponent<SurvivorInput>();
        state = GetComponent<SurvivorState>();
        actionState = GetComponent<SurvivorActionState>();
        move = GetComponent<SurvivorMove>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 씬 전환 후 UI가 새로 생길 수 있으므로 씬 로드 이벤트를 등록한다.
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public override void OnStopClient()
    {
        // 클라이언트 정지 시 씬 로드 이벤트를 해제한다.
        SceneManager.sceneLoaded -= OnSceneLoaded;

        base.OnStopClient();
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // 로컬 플레이어가 시작될 때 Hold 입력 대기 상태를 초기화한다.
        waitRelease = false;

        // 씬에 있는 UI를 연결한다.
        BindUI();

        // 시작 시 ProgressUI는 숨긴다.
        ForceHideProgress();

        // 시작 시 QTE UI도 닫아둔다.
        if (qteUI != null)
            qteUI.ForceClose(false);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // UI 처리는 로컬 플레이어에게만 필요하다.
        if (!isLocalPlayer)
            return;

        // 씬이 바뀌면 입력 대기 상태를 초기화한다.
        waitRelease = false;

        // 새 씬의 UI를 다시 연결한다.
        BindUI();

        // 씬 전환 직후 남아 있을 수 있는 ProgressUI를 숨긴다.
        ForceHideProgress();

        // 씬 전환 직후 QTE UI도 닫는다.
        if (qteUI != null)
            qteUI.ForceClose(false);
    }

    private void Update()
    {
        // 상호작용 입력 처리는 내 로컬 플레이어에서만 한다.
        if (!isLocalPlayer)
            return;

        // 다운, 사망, 강한 행동 상태에서는 상호작용을 전부 끊는다.
        if (state != null)
        {
            bool isBusy = actionState != null && actionState.IsBusy;

            if (state.IsDowned || isBusy || state.IsDead)
            {
                ClearForce();
                return;
            }
        }

        // 상호작용 중이 아닐 때 앉기 중이면 새 상호작용을 시작하지 않는다.
        if (!isInteracting && input != null && input.IsCrouching)
            return;

        // Hold 종료 후 입력키를 한 번 떼면 다음 Hold 상호작용을 다시 허용한다.
        if (waitRelease && input != null && !input.IsInteracting1)
            waitRelease = false;

        // 주변 상호작용 대상 중 현재 가장 적절한 대상을 고른다.
        RefreshCurrentInteractable();

        UpdateInteractionPromptUI();

        // 현재 대상 타입에 맞게 Hold/Press 상호작용을 처리한다.
        HandleInteract();
    }

    private void BindUI()
    {
        if (InGameUIManager.Instance != null)
        {
            progressUI = InGameUIManager.Instance.GetProgressUI();
            qteUI = InGameUIManager.Instance.GetQTEUI();
            interactionPromptUI = InGameUIManager.Instance.GetInteractionPromptUI();
        }

        if (progressUI == null)
            progressUI = FindFirstObjectByType<ProgressUI>(FindObjectsInactive.Include);

        if (qteUI == null)
            qteUI = FindFirstObjectByType<QTEUI>(FindObjectsInactive.Include);

        if (interactionPromptUI == null)
            interactionPromptUI = FindFirstObjectByType<InteractionPromptUI>(FindObjectsInactive.Include);
    }

    public void ShowProgress(object owner, float value)
    {
        // ProgressUI는 로컬 플레이어만 보여준다.
        if (!isLocalPlayer)
            return;

        if (progressUI == null)
            BindUI();

        if (progressUI == null)
            return;

        // 이미 다른 오브젝트가 ProgressUI를 사용 중이면 건드리지 않는다.
        if (progressOwner != null && progressOwner != owner)
            return;

        progressOwner = owner;

        progressUI.Show();
        progressUI.SetProgress(value);
    }

    public void HideProgress(object owner, bool reset)
    {
        // ProgressUI는 로컬 플레이어만 숨긴다.
        if (!isLocalPlayer)
            return;

        if (progressUI == null)
            return;

        // 내가 소유한 ProgressUI가 아니면 숨기지 않는다.
        if (progressOwner != owner)
            return;

        progressOwner = null;

        progressUI.Hide();

        if (reset)
            progressUI.SetProgress(0f);
    }

    public void ForceHideProgress()
    {
        // 어떤 owner가 쓰고 있든 강제로 ProgressUI 점유를 해제한다.
        progressOwner = null;

        if (progressUI != null)
        {
            progressUI.Hide();
            progressUI.SetProgress(0f);
        }
    }

    // 주변 상호작용 목록에서 우선순위가 가장 높은 대상을 현재 대상으로 선택한다.
    private void RefreshCurrentInteractable()
    {
        // Hold 상호작용 중에는 중간에 후보가 바뀌면 안 되므로 activeInteractable을 유지한다.
        if (isInteracting && activeInteractable != null)
        {
            currentInteractable = activeInteractable;
            return;
        }

        IInteractable best = null;
        int bestPriority = int.MinValue;
        float bestDistance = float.MaxValue;

        // 리스트를 뒤에서부터 순회해서 null이나 비활성화된 대상을 제거한다.
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

    // 상호작용 대상의 우선순위를 정한다.
    private int GetPriority(IInteractable interactable)
    {
        if (interactable is Prison)
            return 1000;

        if (interactable is UploadComputer)
            return 800;

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

    // 감옥에 갇힌 상태에서는 자기 감옥만 상호작용 가능하게 제한한다.
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
        // 현재 사용할 수 있는 대상이 없다면 진행 중인 Hold를 종료한다.
        if (currentInteractable == null)
        {
            if (isInteracting)
            {
                isInteracting = false;

                SetInteractionState(false);

                if (activeInteractable != null)
                    activeInteractable.EndInteract();

                activeInteractable = null;
                waitRelease = true;
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
            if (waitRelease)
                return;

            if (!isInteracting && !input.IsCrouching)
            {
                if (currentInteractable == null)
                    return;

                if (move != null)
                    move.SetCamAnim(false);

                isInteracting = true;
                activeInteractable = currentInteractable;

                SetInteractionState(true);

                HideInteractionPromptUI();

                activeInteractable.BeginInteract(gameObject);
            }
        }
        else
        {
            waitRelease = false;

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
        {
            HideInteractionPromptUI();
            currentInteractable.BeginInteract(gameObject);
        }
    }

    public void SetInteractable(IInteractable interactable)
    {
        // 후보 등록은 로컬 플레이어에게만 한다.
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
        // 후보 제거도 로컬 플레이어에게만 한다.
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

                waitRelease = true;
            }

            activeInteractable = null;
        }

        if (currentInteractable == interactable)
            currentInteractable = null;

        HideInteractionPromptUI();
    }

    private void OnDisable()
    {
        // 컴포넌트가 꺼질 때 진행 중인 상호작용을 안전하게 정리한다.
        ClearForce();
    }

    private void ClearForce()
    {
        if (isInteracting && activeInteractable != null)
        {
            isInteracting = false;

            SetInteractionState(false);

            activeInteractable.EndInteract();

            waitRelease = true;
        }

        activeInteractable = null;
        currentInteractable = null;

        nearbyInteractables.Clear();

        ForceHideProgress();

        if (qteUI != null)
            qteUI.ForceClose(false);

        HideInteractionPromptUI();
    }

    // 서버에 Hold 상호작용 중인지 저장한다.
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

    // 서버에서 피격/스턴/다운 등으로 상호작용을 강제 종료할 때 사용한다.
    // 서버 쪽 Interactor는 로컬 activeInteractable 정보를 모를 수 있으므로,
    // 소유 클라이언트에게 TargetRpc를 보내 실제 EndInteract를 실행시킨다.
    [Server]
    public void ForceStopInteractFromServer()
    {
        if (actionState != null)
        {
            actionState.SetInteract(false);
            actionState.SetCam(false);
        }

        if (connectionToClient != null)
            TargetForceStopInteract(connectionToClient);
    }

    // 소유 클라이언트에서 실제 상호작용을 끊는다.
    // 여기서 activeInteractable.EndInteract()가 실행되어
    // Evidence, UploadComputer, Prison, Heal 등의 CmdEnd가 서버로 전달된다.
    [TargetRpc]
    private void TargetForceStopInteract(NetworkConnectionToClient target)
    {
        ForceStopInteract();
    }

    // 피격, 스턴, 다운 등 외부 상황으로 현재 상호작용을 강제 종료할 때 사용한다.
    public void ForceStopInteract()
    {
        if (isInteracting && activeInteractable != null)
        {
            isInteracting = false;

            SetInteractionState(false);

            activeInteractable.EndInteract();

            waitRelease = true;
        }

        activeInteractable = null;
        currentInteractable = null;

        nearbyInteractables.Clear();

        ForceHideProgress();

        if (qteUI != null)
            qteUI.ForceClose(false);

        HideInteractionPromptUI();
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

    private void UpdateInteractionPromptUI()
    {
        if (!isLocalPlayer)
            return;

        if (interactionPromptUI == null)
            BindUI();

        if (interactionPromptUI == null)
            return;

        if (currentInteractable == null)
        {
            interactionPromptUI.Hide();
            return;
        }

        if (isInteracting)
        {
            interactionPromptUI.Hide();
            return;
        }

        if (waitRelease)
        {
            interactionPromptUI.Hide();
            return;
        }

        if (input != null && input.IsCrouching)
        {
            interactionPromptUI.Hide();
            return;
        }

        Sprite icon = null;

        if (InGameUIManager.Instance != null)
        {
            if (currentInteractable.InteractType == InteractType.Hold)
                icon = InGameUIManager.Instance.GetHoldInputIcon();
            else
                icon = InGameUIManager.Instance.GetPressInputIcon();
        }

        string action = GetSurvivorActionText(currentInteractable);

        if (string.IsNullOrWhiteSpace(action))
        {
            interactionPromptUI.Hide();
            return;
        }

        interactionPromptUI.Show(icon, action);
    }

    private string GetSurvivorActionText(IInteractable interactable)
    {
        if (interactable is EvidencePoint evidence)
        {
            if (!string.IsNullOrWhiteSpace(evidence.DisplayName))
                return $"{evidence.DisplayName} 조사";

            return "증거 조사";
        }

        if (interactable is UploadComputer)
            return "증거 업로드";

        if (interactable is Prison prison)
        {
            if (state != null && state.IsImprisoned && prison.PrisonerId == state.netId)
                return "감옥 탈출 시도";

            return "생존자 구출";
        }

        if (interactable is SurvivorHeal)
            return "생존자 치료";

        if (interactable is Pallet pallet)
        {
            if (pallet.IsDropped)
                return "판자 넘기";

            return "판자 내리기";
        }

        if (interactable is Window)
            return "창틀 넘기";

        return "상호작용";
    }

    private void HideInteractionPromptUI()
    {
        if (!isLocalPlayer)
            return;

        if (interactionPromptUI == null)
            return;

        interactionPromptUI.Hide();
    }
}