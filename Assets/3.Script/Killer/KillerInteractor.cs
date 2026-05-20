using UnityEngine;
using Mirror;
using System.Collections;

public class KillerInteractor : NetworkBehaviour
{
    [Header("상호작용 검사")]
    public float interactRange = 2.0f;
    public LayerMask interactLayer;
    public LayerMask survivorLayer;

    [Header("키 안내 UI")]
    [SerializeField] private InteractionPromptUI interactionPromptUI;

    [Header("오디오")]
    [SerializeField] private AudioKey incageSoundKey = AudioKey.KillerIncage;
    [SerializeField] private Vector3 incageSoundOffset = new Vector3(0f, 1.0f, 0f);

    private KillerInput input;
    private KillerState state;
    private IInteractable currentTarget;
    private SurvivorState currentDownedSurvivor;

    private void Awake()
    {
        input = GetComponent<KillerInput>();
        state = GetComponent<KillerState>();
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        BindUI();
        HideInteractionPromptUI();
    }

    private void Update()
    {
        if (!isLocalPlayer)
            return;

        if (input == null || state == null)
            return;

        SearchTarget();
        currentDownedSurvivor = SearchDownedSurvivorForPrompt();

        UpdateInteractionPromptUI();

        if (state.CurrentCondition != KillerCondition.Idle)
            return;

        if (input.IsInteractPressed)
        {
            if (currentTarget != null)
            {
                HideInteractionPromptUI();

                GameObject targetObj = ((MonoBehaviour)currentTarget).gameObject;
                CmdInteract(targetObj);
            }
        }

        if (input.IsPickUpPressed)
        {
            SearchAndIncageSurvivor();
        }
    }

    private void OnDisable()
    {
        HideInteractionPromptUI();
    }

    private void BindUI()
    {
        if (InGameUIManager.Instance != null)
            interactionPromptUI = InGameUIManager.Instance.GetInteractionPromptUI();

        if (interactionPromptUI == null)
            interactionPromptUI = FindFirstObjectByType<InteractionPromptUI>(FindObjectsInactive.Include);
    }

    private void UpdateInteractionPromptUI()
    {
        if (!isLocalPlayer)
            return;

        if (interactionPromptUI == null)
            BindUI();

        if (interactionPromptUI == null)
            return;

        if (state == null || state.CurrentCondition != KillerCondition.Idle)
        {
            interactionPromptUI.Hide();
            return;
        }

        if (InGameUIManager.Instance == null)
        {
            interactionPromptUI.Hide();
            return;
        }

        if (currentDownedSurvivor != null)
        {
            interactionPromptUI.Show(
                InGameUIManager.Instance.GetKillerPickUpIcon(),
                "생존자 감옥에 넣기"
            );
            return;
        }

        if (currentTarget != null)
        {
            string action = GetKillerActionText(currentTarget);

            if (!string.IsNullOrWhiteSpace(action))
            {
                interactionPromptUI.Show(
                    InGameUIManager.Instance.GetPressInputIcon(),
                    action
                );
                return;
            }
        }

        interactionPromptUI.Hide();
    }

    private string GetKillerActionText(IInteractable interactable)
    {
        if (interactable is Window)
            return "창틀 넘기";

        if (interactable is Pallet)
            return "판자 부수기";

        return string.Empty;
    }

    private void HideInteractionPromptUI()
    {
        if (!isLocalPlayer)
            return;

        if (interactionPromptUI != null)
            interactionPromptUI.Hide();
    }

    private void SearchTarget()
    {
        currentTarget = null;

        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;

        Debug.DrawRay(rayOrigin, transform.forward * interactRange, Color.red);

        if (!Physics.Raycast(
                rayOrigin,
                transform.forward,
                out RaycastHit hit,
                interactRange,
                interactLayer,
                QueryTriggerInteraction.Collide))
        {
            return;
        }

        IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();

        if (interactable == null)
            return;

        if (!CanKillerUseTarget(interactable))
            return;

        currentTarget = interactable;
    }

    private bool CanKillerUseTarget(IInteractable interactable)
    {
        if (interactable == null)
            return false;

        if (interactable is Window)
            return true;

        if (interactable is Pallet pallet)
            return pallet.IsDropped;

        return false;
    }

    private SurvivorState SearchDownedSurvivorForPrompt()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            interactRange,
            survivorLayer
        );

        SurvivorState best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];

            if (hit == null)
                continue;

            SurvivorState survivor = hit.GetComponentInParent<SurvivorState>();
            SurvivorActionState actionState = hit.GetComponentInParent<SurvivorActionState>();

            bool isBusy = actionState != null && actionState.IsBusy;

            if (survivor == null)
                continue;

            if (!survivor.IsDowned)
                continue;

            if (isBusy)
                continue;

            float sqrDistance = (survivor.transform.position - transform.position).sqrMagnitude;

            if (best == null || sqrDistance < bestDistance)
            {
                best = survivor;
                bestDistance = sqrDistance;
            }
        }

        return best;
    }

    private void SearchAndIncageSurvivor()
    {
        SurvivorState survivor = SearchDownedSurvivorForPrompt();

        if (survivor == null)
            return;

        HideInteractionPromptUI();

        state.PlayTrigger(KillerCondition.Incage);
        CmdIncageSurvivor(survivor.gameObject);
    }

    [Command]
    private void CmdIncageSurvivor(GameObject survivorObj)
    {
        if (state.CurrentCondition != KillerCondition.Idle)
            return;

        if (survivorObj == null)
            return;

        SurvivorState survivor = survivorObj.GetComponent<SurvivorState>();

        if (survivor == null || !survivor.IsDowned)
            return;

        Prison emptyPrison = PrisonManager.Instance.GetEmpty();

        if (emptyPrison == null)
            return;

        state.ChangeState(KillerCondition.Incage);
        StartCoroutine(IncageRoutineServer(survivor, emptyPrison));
    }

    [Server]
    private IEnumerator IncageRoutineServer(SurvivorState survivor, Prison prison)
    {
        yield return new WaitForSeconds(2.1f);

        if (state == null)
            yield break;

        if (survivor == null || prison == null)
        {
            state.ChangeState(KillerCondition.Idle);
            yield break;
        }

        if (!survivor.IsDowned)
        {
            state.ChangeState(KillerCondition.Idle);
            yield break;
        }

        SurvivorPrisonEffect prisonEffect = survivor.GetComponent<SurvivorPrisonEffect>();

        if (prisonEffect == null)
        {
            Debug.LogWarning("[KillerInteractor] SurvivorPrisonEffect가 없어 감옥 연출을 실행할 수 없습니다.");
            state.ChangeState(KillerCondition.Idle);
            yield break;
        }

        prisonEffect.BeginPrisonSequenceServer(
            prison,
            incageSoundKey,
            incageSoundOffset
        );

        state.ChangeState(KillerCondition.Idle);
    }

    [Command]
    private void CmdInteract(GameObject target)
    {
        if (state.CurrentCondition != KillerCondition.Idle)
            return;

        if (target == null)
            return;

        IInteractable interactable = target.GetComponent<IInteractable>();

        if (interactable == null)
            interactable = target.GetComponentInParent<IInteractable>();

        if (interactable == null)
            return;

        interactable.BeginInteract(gameObject);
    }

    public void ApplyHitStun(float duration)
    {
        if (!isServer)
            return;

        if (state.CurrentCondition == KillerCondition.Hit)
            return;

        Debug.Log($"<color=red>[KillerHit]</color> 판자에 맞음! 스턴 시간: {duration}");

        state.ChangeState(KillerCondition.Hit);
        StartCoroutine(ResetHitStunRoutine(duration));
    }

    private IEnumerator ResetHitStunRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (state.CurrentCondition == KillerCondition.Hit)
            state.ChangeState(KillerCondition.Idle);
    }
}