using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SurvivorInteractor : NetworkBehaviour
{
    [Header("UI")]
    [SerializeField] private ProgressUI progressUI;   // Hold 상호작용 진행도 UI
    [SerializeField] private QTEUI qteUI;             // 증거 조사 QTE UI

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

        // 현재 대상 타입에 맞게 Hold/Press 상호작용을 처리한다.
        HandleInteract();
    }

    private void BindUI()
    {
        // LobbySceneBinder가 있으면 씬에 배치된 UI를 우선 연결한다.
        if (SceneBinder.Instance != null)
        {
            progressUI = SceneBinder.Instance.GetProgressUI();
            qteUI = SceneBinder.Instance.GetQTEUI();
        }

        // ProgressUI가 없으면 씬 전체에서 찾는다.
        if (progressUI == null)
            progressUI = FindFirstObjectByType<ProgressUI>(FindObjectsInactive.Include);

        // QTEUI가 없으면 씬 전체에서 찾는다.
        if (qteUI == null)
            qteUI = FindFirstObjectByType<QTEUI>(FindObjectsInactive.Include);
    }

    public void ShowProgress(object owner, float value)
    {
        // ProgressUI는 로컬 플레이어만 보여준다.
        if (!isLocalPlayer)
            return;

        // UI 참조가 없으면 다시 찾는다.
        if (progressUI == null)
            BindUI();

        // 그래도 없으면 표시할 수 없다.
        if (progressUI == null)
            return;

        // 이미 다른 오브젝트가 ProgressUI를 사용 중이면 건드리지 않는다.
        if (progressOwner != null && progressOwner != owner)
            return;

        // 이 owner가 ProgressUI를 점유한다.
        progressOwner = owner;

        // UI를 표시하고 진행도를 갱신한다.
        progressUI.Show();
        progressUI.SetProgress(value);
    }

    public void HideProgress(object owner, bool reset)
    {
        // ProgressUI는 로컬 플레이어만 숨긴다.
        if (!isLocalPlayer)
            return;

        // UI 참조가 없으면 처리하지 않는다.
        if (progressUI == null)
            return;

        // 내가 소유한 ProgressUI가 아니면 숨기지 않는다.
        if (progressOwner != owner)
            return;

        // ProgressUI 점유를 해제한다.
        progressOwner = null;

        // UI를 숨긴다.
        progressUI.Hide();

        // 완전 종료 상황이면 진행도를 0으로 초기화한다.
        if (reset)
            progressUI.SetProgress(0f);
    }

    public void ForceHideProgress()
    {
        // 어떤 owner가 쓰고 있든 강제로 ProgressUI 점유를 해제한다.
        progressOwner = null;

        // UI가 있으면 숨기고 진행도를 초기화한다.
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

            // 대상이 사라졌으면 목록에서 제거한다.
            if (interactable == null)
            {
                nearbyInteractables.RemoveAt(i);
                continue;
            }

            // MonoBehaviour가 아니거나 비활성화된 대상이면 제거한다.
            MonoBehaviour behaviour = interactable as MonoBehaviour;
            if (behaviour == null || !behaviour.isActiveAndEnabled)
            {
                nearbyInteractables.RemoveAt(i);
                continue;
            }

            // 감옥 상태처럼 현재 상태에서 사용할 수 없는 대상은 제외한다.
            if (!CanUseThis(interactable))
                continue;

            // 대상 타입별 우선순위를 계산한다.
            int priority = GetPriority(interactable);

            // 같은 우선순위일 때 가까운 대상을 고르기 위한 거리 계산이다.
            float sqrDistance = (behaviour.transform.position - transform.position).sqrMagnitude;

            // 아직 후보가 없으면 현재 대상을 후보로 등록한다.
            if (best == null)
            {
                best = interactable;
                bestPriority = priority;
                bestDistance = sqrDistance;
                continue;
            }

            // 더 높은 우선순위면 교체한다.
            if (priority > bestPriority)
            {
                best = interactable;
                bestPriority = priority;
                bestDistance = sqrDistance;
                continue;
            }

            // 우선순위가 같으면 더 가까운 대상을 선택한다.
            if (priority == bestPriority && sqrDistance < bestDistance)
            {
                best = interactable;
                bestPriority = priority;
                bestDistance = sqrDistance;
            }
        }

        // 최종 선택된 대상을 현재 상호작용 대상으로 저장한다.
        currentInteractable = best;
    }

    // 상호작용 대상의 우선순위를 정한다.
    private int GetPriority(IInteractable interactable)
    {
        // 감옥 구출/탈출은 가장 높은 우선순위다.
        if (interactable is Prison)
            return 1000;

        // 업로드 컴퓨터는 게임 목표라 높은 우선순위다.
        if (interactable is UploadComputer)
            return 800;

        // 생존자 힐은 감옥/업로드보다 낮다.
        if (interactable is SurvivorHeal)
            return 300;

        // 증거 조사는 힐보다 낮다.
        if (interactable is EvidencePoint)
            return 200;

        // 판자와 창틀은 같은 우선순위다.
        if (interactable is Pallet)
            return 100;

        if (interactable is Window)
            return 100;

        // 기타 대상은 가장 낮은 우선순위다.
        return 0;
    }

    // 감옥에 갇힌 상태에서는 자기 감옥만 상호작용 가능하게 제한한다.
    private bool CanUseThis(IInteractable interactable)
    {
        // 상태 컴포넌트가 없으면 제한하지 않는다.
        if (state == null)
            return true;

        // 감옥 상태가 아니면 모든 대상 사용 가능하다.
        if (!state.IsImprisoned)
            return true;

        // 감옥 상태일 때는 Prison만 사용 가능하다.
        Prison prison = interactable as Prison;
        if (prison == null)
            return false;

        // 자기 자신이 갇힌 감옥만 사용 가능하다.
        return prison.netId == state.CurrentPrisonId;
    }

    private void HandleInteract()
    {
        // 현재 사용할 수 있는 대상이 없다면 진행 중인 Hold를 종료한다.
        if (currentInteractable == null)
        {
            if (isInteracting)
            {
                // 로컬 상호작용 상태 해제
                isInteracting = false;

                // 서버의 행동 상태에도 상호작용 종료를 알린다.
                SetInteractionState(false);

                // 실제 진행 중이던 대상에게 종료를 알린다.
                if (activeInteractable != null)
                    activeInteractable.EndInteract();

                // 진행 대상 초기화
                activeInteractable = null;

                // 대상이 사라져 Hold가 끝났으므로 같은 입력으로 다음 Hold를 바로 시작하지 못하게 한다.
                waitRelease = true;
            }

            return;
        }

        // 대상이 Hold 타입이면 누르고 있는 동안 진행한다.
        if (currentInteractable.InteractType == InteractType.Hold)
            HandleHold();
        else
            HandlePress();
    }

    private void HandleHold()
    {
        // 입력 컴포넌트가 없으면 처리하지 않는다.
        if (input == null)
            return;

        // 다운, 사망, 강한 행동 상태에서는 Hold를 시작하지 않는다.
        if (state != null)
        {
            bool isBusy = actionState != null && actionState.IsBusy;

            if (state.IsDowned || isBusy || state.IsDead)
                return;
        }

        // Interact1을 누르고 있는 동안 Hold 상호작용을 처리한다.
        if (input.IsInteracting1)
        {
            // 이전 Hold가 끝난 뒤 아직 키를 떼지 않았다면 새 Hold 시작 금지
            // 감옥 구출 후 힐로 바로 이어지는 버그를 막는 핵심 부분이다.
            if (waitRelease)
                return;

            // 아직 Hold 중이 아니고 앉기 중이 아니라면 새 Hold를 시작한다.
            if (!isInteracting && !input.IsCrouching)
            {
                // 현재 대상이 없으면 시작할 수 없다.
                if (currentInteractable == null)
                    return;

                // Hold 시작 시 카메라 스킬 애니메이션을 정리한다.
                if (move != null)
                    move.SetCamAnim(false);

                // 로컬 Hold 상태를 켠다.
                isInteracting = true;

                // 현재 대상을 실제 진행 대상으로 고정한다.
                activeInteractable = currentInteractable;

                // 서버 행동 상태에 상호작용 중임을 저장한다.
                SetInteractionState(true);

                // 대상의 상호작용 시작 함수를 호출한다.
                activeInteractable.BeginInteract(gameObject);
            }
        }
        else
        {
            // Interact1을 뗐으므로 다음 Hold를 다시 시작할 수 있다.
            waitRelease = false;

            // 진행 중인 Hold가 있다면 종료한다.
            if (isInteracting)
            {
                // 로컬 Hold 상태 해제
                isInteracting = false;

                // 서버 행동 상태에 상호작용 종료를 알린다.
                SetInteractionState(false);

                // 실제 진행 중이던 대상에게 종료를 알린다.
                if (activeInteractable != null)
                    activeInteractable.EndInteract();

                // 진행 대상 초기화
                activeInteractable = null;
            }
        }
    }

    private void HandlePress()
    {
        // 입력 컴포넌트가 없으면 처리하지 않는다.
        if (input == null)
            return;

        // 앉기 중에는 Press 상호작용을 시작하지 않는다.
        if (input.IsCrouching)
            return;

        // 다운, 사망, 강한 행동 상태에서는 Press를 시작하지 않는다.
        if (state != null)
        {
            bool isBusy = actionState != null && actionState.IsBusy;

            if (state.IsDowned || isBusy || state.IsDead)
                return;
        }

        // Press 입력은 누른 순간 한 번만 실행한다.
        if (input.IsInteracting2)
            currentInteractable.BeginInteract(gameObject);
    }

    public void SetInteractable(IInteractable interactable)
    {
        // 후보 등록은 로컬 플레이어에게만 한다.
        if (!isLocalPlayer)
            return;

        // Interactor가 비활성화된 상태라면 후보 등록하지 않는다.
        if (!enabled)
            return;

        // 사용 불가능한 상태라면 후보 등록하지 않는다.
        if (state != null)
        {
            bool isBusy = actionState != null && actionState.IsBusy;

            if (state.IsDowned || isBusy || state.IsDead)
                return;
        }

        // null 대상은 등록하지 않는다.
        if (interactable == null)
            return;

        // 중복 등록을 방지한다.
        if (!nearbyInteractables.Contains(interactable))
            nearbyInteractables.Add(interactable);
    }

    public void ClearInteractable(IInteractable interactable)
    {
        // 후보 제거도 로컬 플레이어에게만 한다.
        if (!isLocalPlayer)
            return;

        // null 대상은 처리하지 않는다.
        if (interactable == null)
            return;

        // 주변 후보 목록에서 제거한다.
        nearbyInteractables.Remove(interactable);

        // 제거되는 대상이 현재 진행 중인 대상이면 Hold를 강제로 종료한다.
        if (activeInteractable == interactable)
        {
            if (isInteracting)
            {
                // 로컬 Hold 상태 해제
                isInteracting = false;

                // 서버 행동 상태에 상호작용 종료를 알린다.
                SetInteractionState(false);

                // 대상에게 종료를 알린다.
                activeInteractable.EndInteract();

                // 진행 중이던 Hold 대상이 외부에서 제거되었으므로 입력을 한 번 떼야 다음 Hold 가능
                waitRelease = true;
            }

            // 진행 대상 초기화
            activeInteractable = null;
        }

        // 제거되는 대상이 현재 선택된 대상이면 선택도 해제한다.
        if (currentInteractable == interactable)
            currentInteractable = null;
    }

    private void OnDisable()
    {
        // 컴포넌트가 꺼질 때 진행 중인 상호작용을 안전하게 정리한다.
        ClearForce();
    }

    private void ClearForce()
    {
        // 진행 중인 Hold가 있으면 강제로 종료한다.
        if (isInteracting && activeInteractable != null)
        {
            // 로컬 Hold 상태 해제
            isInteracting = false;

            // 서버 행동 상태에 상호작용 종료를 알린다.
            SetInteractionState(false);

            // 대상에게 종료를 알린다.
            activeInteractable.EndInteract();

            // 강제 종료 후에도 같은 입력이 바로 다음 Hold로 이어지지 않게 한다.
            waitRelease = true;
        }

        // 진행 대상과 현재 대상을 초기화한다.
        activeInteractable = null;
        currentInteractable = null;

        // 주변 후보 목록을 모두 비운다.
        nearbyInteractables.Clear();

        // ProgressUI를 강제로 숨긴다.
        ForceHideProgress();

        // QTE가 열려 있으면 닫는다.
        if (qteUI != null)
            qteUI.ForceClose(false);
    }

    // 서버에 Hold 상호작용 중인지 저장한다.
    private void SetInteractionState(bool value)
    {
        // 행동 상태 컴포넌트가 없으면 처리하지 않는다.
        if (actionState == null)
            return;

        // 서버라면 직접 행동 상태를 변경한다.
        if (isServer)
        {
            // Hold 상호작용 상태 저장
            actionState.SetInteract(value);

            // 상호작용을 시작하면 카메라 스킬 상태는 꺼준다.
            if (value)
                actionState.SetCam(false);
        }
        // 클라이언트라면 Command로 서버에 요청한다.
        else if (isLocalPlayer)
        {
            CmdSetInteractionState(value);
        }
    }

    // 피격, 스턴, 다운 등 외부 상황으로 현재 상호작용을 강제 종료할 때 사용한다.
    public void ForceStopInteract()
    {
        // 진행 중인 Hold가 있으면 종료한다.
        if (isInteracting && activeInteractable != null)
        {
            // 로컬 Hold 상태 해제
            isInteracting = false;

            // 서버 행동 상태에 상호작용 종료를 알린다.
            SetInteractionState(false);

            // 대상에게 종료를 알린다.
            activeInteractable.EndInteract();

            // 피격/스턴으로 끊겼을 때도 같은 입력이 다음 Hold로 이어지지 않게 한다.
            waitRelease = true;
        }

        // 진행 대상과 현재 대상을 초기화한다.
        activeInteractable = null;
        currentInteractable = null;

        // ProgressUI를 강제로 숨긴다.
        ForceHideProgress();

        // QTE가 열려 있으면 닫는다.
        if (qteUI != null)
            qteUI.ForceClose(false);
    }

    [Command]
    private void CmdSetInteractionState(bool value)
    {
        // 서버에서 행동 상태 컴포넌트가 없으면 처리하지 않는다.
        if (actionState == null)
            return;

        // 서버에 Hold 상호작용 상태를 저장한다.
        actionState.SetInteract(value);

        // 상호작용 중에는 카메라 스킬 상태를 끈다.
        if (value)
            actionState.SetCam(false);
    }
}