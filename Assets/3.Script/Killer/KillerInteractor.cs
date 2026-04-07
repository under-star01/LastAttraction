using UnityEngine;
using System.Collections;

public class KillerInteractor : MonoBehaviour
{
    [Header("Interaction Settings")]
    public float interactRange = 2.0f;
    public LayerMask interactLayer;

    private KillerInput input;
    private KillerState state;
    private Animator animator;
    private IInteractable currentTarget;

    void Awake()
    {
        input = GetComponent<KillerInput>();
        state = GetComponent<KillerState>();
        animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        // 1. 앞에 상호작용 대상이 있는지 탐색
        SearchTarget();

        // 2. Hit 상태가 아닐 때만 상호작용 시도
        if (state.CurrentCondition == KillerCondition.Idle && input.IsInteractPressed)
        {
            if (currentTarget != null)
            {
                // 오브젝트의 기능을 호출 (창틀 넘기, 판자 부수기 등 실행)
                currentTarget.BeginInteract(this.gameObject);
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
        if (state.CurrentCondition == KillerCondition.Hit) return;

        state.ChangeState(KillerCondition.Hit);
        if (animator != null) animator.SetTrigger("Hit");

        StartCoroutine(ResetStateRoutine(duration));
    }

    private IEnumerator ResetStateRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        state.ChangeState(KillerCondition.Idle);
    }
}