using UnityEngine;
using Mirror;
using System.Collections;

public class KillerInteractor : NetworkBehaviour
{
    [Header("상호작용 검사")]
    public float interactRange = 2.0f;
    public LayerMask interactLayer;
    public LayerMask survivorLayer;

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
        RaycastHit hit;

        Debug.DrawRay(rayOrigin, transform.forward * interactRange, Color.red);

        if (Physics.Raycast(rayOrigin, transform.forward, out hit, interactRange, interactLayer, QueryTriggerInteraction.Collide))
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
        RpcPlaySurvivorSmoke(survivor.gameObject);

        yield return new WaitForSeconds(2.1f);

        prison.SetPrisoner(survivor);
        state.ChangeState(KillerCondition.Idle);
    }

    [ClientRpc]
    private void RpcPlaySurvivorSmoke(GameObject survivorObj)
    {
        if (survivorObj == null) return;

        // 생존자 자식 중에서 "Smoke_air"라는 이름을 가진 파티클 시스템을 찾음
        // transform.Find는 직계 자식만 찾으므로, 깊은 곳에 있다면 GetComponentsInChildren 사용
        Transform smokeTransform = survivorObj.transform.Find("Smoke_air");

        if (smokeTransform != null)
        {
            ParticleSystem ps = smokeTransform.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Play();
            }
        }
        else
        {
            // 만약 경로가 복잡할 경우를 대비한 백업 (성능을 위해 Find를 우선 권장)
            ParticleSystem[] allPS = survivorObj.GetComponentsInChildren<ParticleSystem>();
            foreach (var ps in allPS)
            {
                if (ps.gameObject.name == "Smoke_air")
                {
                    ps.Play();
                    break;
                }
            }
        }
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

        interactable.BeginInteract(this.gameObject);
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