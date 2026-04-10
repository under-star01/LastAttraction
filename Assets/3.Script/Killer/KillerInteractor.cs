using UnityEngine;
using Mirror;
using System.Collections;

public class KillerInteractor : NetworkBehaviour
{
    public float interactRange = 2.0f;
    public LayerMask interactLayer;
    public LayerMask survivorLayer;      // 쓰러진 생존자 감지용 레이어

    private KillerInput input;
    private KillerState state;
    private IInteractable currentTarget;

    void Awake()
    {
        input = GetComponent<KillerInput>();
        state = GetComponent<KillerState>();
    }

    void Update()
    {
        if (!isLocalPlayer) return;
        SearchTarget();

        if (state.CurrentCondition == KillerCondition.Idle && input.IsInteractPressed)
        {
            if (currentTarget != null)
            {
                GameObject targetObj = ((MonoBehaviour)currentTarget).gameObject;

                // [로컬 반응 추가] 상태에 따른 트리거를 미리 당겨 동기화 속도를 맞춥니다.
                if (targetObj.CompareTag("Pallet")) state.PlayTrigger(KillerCondition.Breaking);
                else if (targetObj.CompareTag("Window")) state.PlayTrigger(KillerCondition.Vaulting);

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
        RaycastHit hit;
        if (Physics.Raycast(transform.position, transform.forward, out hit, interactRange, interactLayer))
        {
            currentTarget = hit.collider.GetComponent<IInteractable>();
        }
        else currentTarget = null;
    }

    // 주변의 쓰러진 생존자를 찾아 감옥으로 보내는 로컬 함수
    private void SearchAndIncageSurvivor()
    {
        // 살인마 주변 interactRange 내의 생존자 콜라이더 탐색
        Collider[] hits = Physics.OverlapSphere(transform.position, interactRange, survivorLayer);

        foreach (var hit in hits)
        {
            SurvivorState survivor = hit.GetComponentInParent<SurvivorState>();
            // 생존자가 존재하고, 현재 '쓰러짐(Downed)' 상태이며, 다른 연출 중이 아닐 때
            if (survivor != null && survivor.IsDowned && !survivor.IsBusy)
            {
                // 로컬에서 즉시 Incage 애니메이션 재생
                state.PlayTrigger(KillerCondition.Incage);
                // 서버에 감옥 보내기 요청
                CmdIncageSurvivor(survivor.gameObject);
                break;
            }
        }
    }

    [Command]
    private void CmdIncageSurvivor(GameObject survivorObj)
    {
        // 서버에서 다시 한번 상태 및 유효성 검사
        if (state.CurrentCondition != KillerCondition.Idle) return;

        SurvivorState survivor = survivorObj.GetComponent<SurvivorState>();
        if (survivor == null || !survivor.IsDowned) return;

        // 비어있는 감옥 찾기
        Prison emptyPrison = PrisonManager.Instance.GetEmpty();
        if (emptyPrison == null) return;

        // 살인마 상태를 Incage로 변경 (이동/시야 잠금)
        state.ChangeState(KillerCondition.Incage);

        // 연출 시간 후 생존자 이동 및 상태 복구
        StartCoroutine(IncageRoutineServer(survivor, emptyPrison));
    }

    [Server]
    private IEnumerator IncageRoutineServer(SurvivorState survivor, Prison prison)
    {
        // 살인마의 가두기 애니메이션 길이만큼 대기 (예: 2.1초)
        yield return new WaitForSeconds(2.1f);

        // Prison.cs의 SetPrisoner를 호출하여 생존자를 이동시키고 상태를 Imprisoned로 변경
        prison.SetPrisoner(survivor);

        // 살인마를 다시 평상시 상태로 복구
        state.ChangeState(KillerCondition.Idle);
    }

    [Command]
    private void CmdInteract(GameObject target)
    {
        if (state.CurrentCondition != KillerCondition.Idle) return;

        IInteractable interactable = target.GetComponent<IInteractable>();
        if (interactable == null) return;

        if (target.CompareTag("Pallet")) state.ChangeState(KillerCondition.Breaking);
        else if (target.CompareTag("Window")) state.ChangeState(KillerCondition.Vaulting);

        interactable.BeginInteract(this.gameObject);
    }

    // Pallet에서 호출하는 스턴 함수
    public void ApplyHitStun(float duration)
    {
        if (!isServer) return;
        if (state.CurrentCondition == KillerCondition.Hit) return;

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