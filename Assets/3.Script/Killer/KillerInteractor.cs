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
    [SerializeField] private AudioKey incageSoundKey = AudioKey.KillerIncage; // 생존자를 감옥에 넣을 때 소리
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

    // 정면 상호작용 대상 찾기
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

    // 주변 다운 생존자 찾아 감옥 보내기
    private void SearchAndIncageSurvivor()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, interactRange, survivorLayer);

        foreach (var hit in hits)
        {
            SurvivorState survivor = hit.GetComponentInParent<SurvivorState>();
            SurvivorActionState actionState = hit.GetComponentInParent<SurvivorActionState>();

            // 다운 상태이고, 다운 연출/스턴 같은 강한 행동 제한 중이 아닐 때만 가능
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

        state.ChangeState(KillerCondition.Incage);
        StartCoroutine(IncageRoutineServer(survivor, emptyPrison));
    }

    [Server]
    private IEnumerator IncageRoutineServer(SurvivorState survivor, Prison prison)
    {
        yield return new WaitForSeconds(2.1f);

        if (state == null)
            yield break;

        // 대기 시간 중 대상이나 감옥이 사라졌을 수 있으므로 방어 처리
        if (survivor == null || prison == null)
        {
            state.ChangeState(KillerCondition.Idle);
            yield break;
        }

        // 감옥에 넣는 처리가 실제로 확정되는 순간 3D 사운드 재생
        ServerPlayIncageSound(prison.transform.position);

        prison.SetPrisoner(survivor);
        state.ChangeState(KillerCondition.Idle);
    }

    // 서버에서 감옥 넣기 성공 사운드를 모든 클라이언트에게 3D로 재생한다.
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

    // 판자 스턴 적용
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