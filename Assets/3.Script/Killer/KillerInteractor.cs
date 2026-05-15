using UnityEngine;
using Mirror;
using System.Collections;

public class KillerInteractor : NetworkBehaviour
{
    [Header("상호작용 검사")]
    public float interactRange = 2.0f;
    public LayerMask interactLayer;
    public LayerMask survivorLayer;

    [Header("오디오")]
    [SerializeField] private AudioKey incageSoundKey = AudioKey.KillerIncage;
    [SerializeField] private Vector3 incageSoundOffset = new Vector3(0f, 1.0f, 0f);

    private KillerInput input;
    private KillerState state;
    private IInteractable currentTarget;

    private void Awake()
    {
        input = GetComponent<KillerInput>();
        state = GetComponent<KillerState>();
    }

    private void Update()
    {
        if (!isLocalPlayer)
            return;

        SearchTarget();

        if (state.CurrentCondition == KillerCondition.Idle && input.IsInteractPressed)
        {
            if (currentTarget != null)
            {
                GameObject targetObj = ((MonoBehaviour)currentTarget).gameObject;
                CmdInteract(targetObj);
            }
        }

        if (state.CurrentCondition == KillerCondition.Idle && input.IsPickUpPressed)
        {
            SearchAndIncageSurvivor();
        }
    }

    private void SearchTarget()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;

        Debug.DrawRay(rayOrigin, transform.forward * interactRange, Color.red);

        if (Physics.Raycast(rayOrigin, transform.forward, out RaycastHit hit, interactRange, interactLayer, QueryTriggerInteraction.Collide))
        {
            currentTarget = hit.collider.GetComponentInParent<IInteractable>();
        }
        else
        {
            currentTarget = null;
        }
    }

    private void SearchAndIncageSurvivor()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, interactRange, survivorLayer);

        foreach (var hit in hits)
        {
            SurvivorState survivor = hit.GetComponentInParent<SurvivorState>();
            SurvivorActionState actionState = hit.GetComponentInParent<SurvivorActionState>();

            bool isBusy = actionState != null && actionState.IsBusy;

            if (survivor != null && survivor.IsDowned && !isBusy)
            {
                state.PlayTrigger(KillerCondition.Incage);
                CmdIncageSurvivor(survivor.gameObject);
                break;
            }
        }
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

        SurvivorIncageEffect incageEffect = survivorObj.GetComponent<SurvivorIncageEffect>();
        NetworkIdentity survivorIdentity = survivorObj.GetComponent<NetworkIdentity>();

        if (incageEffect != null && survivorIdentity != null)
        {
            // 대상 생존자의 클라이언트에만 TargetRpc를 날려 연출을 재생시킵니다.
            incageEffect.TargetPlayIncageEffect(survivorIdentity.connectionToClient, gameObject, emptyPrison.transform.position);
        }
        // ==========================================================

        state.ChangeState(KillerCondition.Incage);
        StartCoroutine(IncageRoutineServer(survivor, emptyPrison));
    }

    [Server]
    private IEnumerator IncageRoutineServer(SurvivorState survivor, Prison prison)
    {
        // 살인마의 인케이지 애니메이션(약 2.1초) 대기
        yield return new WaitForSeconds(2.1f);

        if (state == null)
            yield break;

        if (survivor == null || prison == null)
        {
            state.ChangeState(KillerCondition.Idle);
            yield break;
        }

        ServerPlayIncageSound(prison.transform.position);

        // 여기서 생존자의 실제 서버 위치가 감옥으로 강제 이동됩니다.
        prison.SetPrisoner(survivor);
        state.ChangeState(KillerCondition.Idle);
    }

    [Server]
    private void ServerPlayIncageSound(Vector3 prisonPosition)
    {
        if (NetworkAudioManager.Instance == null)
            return;

        if (incageSoundKey == AudioKey.None)
            return;

        NetworkAudioManager.PlayAudioForEveryone(
            incageSoundKey,
            AudioDimension.Sound3D,
            prisonPosition + incageSoundOffset
        );
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