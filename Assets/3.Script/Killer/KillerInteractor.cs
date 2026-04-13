using UnityEngine;
using Mirror;
using System.Collections;

public class KillerInteractor : NetworkBehaviour
{
    [Header("상호작용 검사")]
    public float interactRange = 2.0f;      // 정면으로 상호작용 가능한 거리
    public LayerMask interactLayer;         // 판자, 창틀 같은 상호작용 오브젝트 레이어
    public LayerMask survivorLayer;         // 쓰러진 생존자 찾기용 레이어

    private KillerInput input;
    private KillerState state;
    private IInteractable currentTarget;    // 현재 정면에서 보고 있는 상호작용 대상

    void Awake()
    {
        input = GetComponent<KillerInput>();
        state = GetComponent<KillerState>();
    }

    void Update()
    {
        // 내 로컬 킬러만 입력 처리
        if (!isLocalPlayer) return;

        // 정면 상호작용 대상 찾기
        SearchTarget();

        // 여기서 먼저 Breaking / Vaulting 상태를 넣지 않는다.
        // 실제 사용 가능 여부는 각 오브젝트(Pallet / Window)가 서버에서 판정한다.
        if (state.CurrentCondition == KillerCondition.Idle && input.IsInteractPressed)
        {
            if (currentTarget != null)
            {
                GameObject targetObj = ((MonoBehaviour)currentTarget).gameObject;
                CmdInteract(targetObj);
            }
        }

        // 다운된 생존자를 감옥에 보내는 입력
        if (state.CurrentCondition == KillerCondition.Idle && input.IsPickUpPressed)
        {
            SearchAndIncageSurvivor();
        }
    }

    // 정면 Raycast로 현재 상호작용 대상 찾기
    private void SearchTarget()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
        RaycastHit hit;

        Debug.DrawRay(rayOrigin, transform.forward * interactRange, Color.red);

        if (Physics.Raycast(rayOrigin, transform.forward, out hit, interactRange, interactLayer, QueryTriggerInteraction.Collide))
        {
            // 자식 콜라이더를 맞췄을 수도 있으니 부모에서 IInteractable 찾기
            currentTarget = hit.collider.GetComponentInParent<IInteractable>();
        }
        else
        {
            currentTarget = null;
        }
    }

    // 주변의 다운된 생존자를 찾아 감옥 보내기
    private void SearchAndIncageSurvivor()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, interactRange, survivorLayer);

        foreach (var hit in hits)
        {
            SurvivorState survivor = hit.GetComponentInParent<SurvivorState>();

            // 다운 상태이고, 다른 연출 중이 아닐 때만 감옥 보내기 가능
            if (survivor != null && survivor.IsDowned && !survivor.IsBusy)
            {
                // 감옥 보내기 연출은 여기서 미리 재생해도 됨
                state.PlayTrigger(KillerCondition.Incage);
                CmdIncageSurvivor(survivor.gameObject);
                break;
            }
        }
    }

    [Command]
    private void CmdIncageSurvivor(GameObject survivorObj)
    {
        // 서버에서도 다시 상태 검사
        if (state.CurrentCondition != KillerCondition.Idle) return;

        SurvivorState survivor = survivorObj.GetComponent<SurvivorState>();
        if (survivor == null || !survivor.IsDowned) return;

        // 비어있는 감옥 찾기
        Prison emptyPrison = PrisonManager.Instance.GetEmpty();
        if (emptyPrison == null) return;

        // 실제 감옥 보내기 상태 시작
        state.ChangeState(KillerCondition.Incage);
        StartCoroutine(IncageRoutineServer(survivor, emptyPrison));
    }

    [Server]
    private IEnumerator IncageRoutineServer(SurvivorState survivor, Prison prison)
    {
        // 가두기 애니메이션 시간만큼 대기
        yield return new WaitForSeconds(2.1f);

        // 생존자를 감옥에 넣음
        prison.SetPrisoner(survivor);

        // 다시 Idle 상태 복귀
        state.ChangeState(KillerCondition.Idle);
    }

    [Command]
    private void CmdInteract(GameObject target)
    {
        // 킬러가 Idle 상태일 때만 상호작용 시작 가능
        if (state.CurrentCondition != KillerCondition.Idle) return;
        if (target == null) return;

        // IInteractable 찾기
        IInteractable interactable = target.GetComponent<IInteractable>();
        if (interactable == null)
            interactable = target.GetComponentInParent<IInteractable>();

        if (interactable == null) return;

        // 여기서 state.ChangeState(Breaking / Vaulting)를 먼저 하면 안 된다.
        // 예를 들어 생존자가 내리는 중 / 넘는 중이면 실제로는 상호작용 실패인데
        // 킬러만 먼저 Breaking 상태가 되어 멈춘 것처럼 보일 수 있다.
        // 그래서 실제 판정은 Pallet / Window 안에서 성공했을 때만 상태를 바꾼다.
        interactable.BeginInteract(this.gameObject);
    }

    // 판자에 맞았을 때 스턴 적용
    public void ApplyHitStun(float duration)
    {
        if (!isServer) return;
        if (state.CurrentCondition == KillerCondition.Hit) return;

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