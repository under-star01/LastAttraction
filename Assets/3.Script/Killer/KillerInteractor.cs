using UnityEngine;
using System.Collections;
using Mirror;

public class KillerInteractor : NetworkBehaviour
{
    [Header("Interaction Settings")]
    public float interactRange = 2.0f;
    public LayerMask interactLayer;

    private KillerInput input;
    private KillerState state;
    private Animator animator;
    private IInteractable currentTarget;

    private NetworkAnimator networkAnimator;

    void Awake()
    {
        input = GetComponent<KillerInput>();
        state = GetComponent<KillerState>();
        animator = GetComponentInChildren<Animator>();
        networkAnimator = GetComponent<NetworkAnimator>(); // 추가
    }

    void Update()
    {
        if (!isLocalPlayer) return;
        // 1. 앞에 상호작용 대상이 있는지 탐색
        SearchTarget();

        // 2. Hit 상태가 아닐 때만 상호작용 시도
        if (state.CurrentCondition == KillerCondition.Idle && input.IsInteractPressed)
        {
            if (currentTarget != null)
            {
                // currentTarget.gameObject 대신, 인터페이스를 구현하고 있는 실제 컴포넌트의 gameObject를 찾습니다.
                GameObject targetObj = ((MonoBehaviour)currentTarget).gameObject;
                CmdInteract(targetObj);
            }
        }
    }

    private void SearchTarget()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position, transform.forward, out hit, interactRange, interactLayer))
        {
            currentTarget = hit.collider.GetComponent<IInteractable>();
        }
        else
        {
            currentTarget = null;
        }
    }

    // [중요] 판자가 살인마를 때릴 때 호출하는 함수
    public void ApplyHitStun(float duration)
    {
        if (!isServer) return;
        if (state.CurrentCondition == KillerCondition.Hit) return;

        state.ChangeState(KillerCondition.Hit);
        //if (networkAnimator != null) networkAnimator.SetTrigger("Hit");
        StartCoroutine(ResetStateRoutine(duration));
    }

    private IEnumerator ResetStateRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        state.ChangeState(KillerCondition.Idle);
    }

    [Command]
    private void CmdInteract(GameObject target)
    {
        IInteractable interactable = target.GetComponent<IInteractable>();
        if (interactable != null)
        {
            // 1. 살인마 상태 변경 (예: Breaking)
            state.ChangeState(KillerCondition.Breaking);

            // 2. 네트워크 애니메이터로 트리거 실행 (모든 클라 동기화) [cite: 2026-04-06]
            //networkAnimator.SetTrigger("Break"); // 애니메이터의 판자부수기 트리거 이름 확인 필요

            interactable.BeginInteract(this.gameObject);
        }
    }
}