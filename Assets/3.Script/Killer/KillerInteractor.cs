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
        // 1. 살인마의 인케이지 애니메이션(약 2.1초) 대기
        yield return new WaitForSeconds(2.1f);

        if (state != null)
        {
            // 2. 살인마는 2.1초 뒤에 다시 자유롭게 움직일 수 있도록 상태 해제 (먼저 풀어줌)
            state.ChangeState(KillerCondition.Idle);
        }

        if (survivor == null || prison == null)
            yield break;

        // 3. 생존자는 시네마틱 연출을 다 볼 때까지 기다렸다가 감옥에 집어넣도록 별도 코루틴 실행
        // 연출 시간(4초) + 암전 대기 시간(약 1.5초) = 약 5.5초 뒤에 감옥으로 물리적 이동.
        // 이미 2.1초가 지났으므로 3.4초만 더 대기하게 합니다.
        StartCoroutine(DelayedSetPrisonerRoutine(survivor, prison, 3.4f));
    }

    [Server]
    private IEnumerator DelayedSetPrisonerRoutine(SurvivorState survivor, Prison prison, float delay)
    {
        // 연출이 끝날 때까지 서버에서 대기
        yield return new WaitForSeconds(delay);

        if (survivor == null || prison == null)
            yield break;

        // 암전이 진행 중이거나 막 끝날 즈음에 사운드 재생 및 감옥 가두기 완료
        ServerPlayIncageSound(prison.transform.position);

        // 여기서 생존자의 실제 서버 위치가 감옥으로 강제 이동되며 상태가 바뀜
        prison.SetPrisoner(survivor);

        //Debug.Log($"<color=cyan>[KillerInteractor]</color> 생존자 연출 종료 타이밍에 맞춰 감옥 세팅 완료");
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