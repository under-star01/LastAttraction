using Mirror;
using UnityEngine;

public class SurvivorHeal : NetworkBehaviour, IInteractable
{
    // 힐은 누르고 있는 동안 진행되는 Hold 타입 상호작용
    public InteractType InteractType => InteractType.Hold;

    [Header("힐 설정")]
    [SerializeField] private float healTime = 16f; // 힐 완료까지 걸리는 시간

    [Header("참조")]
    [SerializeField] private SurvivorState targetState;           // 힐 받을 대상 상태
    [SerializeField] private SurvivorMove targetMove;             // 힐 받을 대상 이동 제어
    [SerializeField] private SurvivorInteractor targetInteractor; // 힐 받을 대상 상호작용 상태 확인용

    // 현재 힐 중인 플레이어 netId
    [SyncVar] private uint healer;

    // 현재 힐 진행 중인지
    [SyncVar] private bool isHealing;

    // 현재 힐 진행도
    [SyncVar] private float progress;

    // 힐하는 쪽(로컬)
    private SurvivorInteractor localHealerInteractor;
    private SurvivorState localHealerState;
    private SurvivorMove localHealerMove;

    // 힐받는 쪽(로컬)
    private SurvivorInteractor localTargetInteractor;

    private void Awake()
    {
        // 같은 대상 플레이어 오브젝트 기준으로 자동 연결
        if (targetState == null)
            targetState = GetComponentInParent<SurvivorState>();

        if (targetMove == null)
            targetMove = GetComponentInParent<SurvivorMove>();

        if (targetInteractor == null)
            targetInteractor = GetComponentInParent<SurvivorInteractor>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 이 힐 오브젝트가 내 플레이어에 붙어 있다면
        // 힐받는 쪽 UI 참조를 미리 캐싱
        CacheTarget();
    }

    private void Update()
    {
        // 실제 힐 진행은 서버에서만 처리
        if (isServer)
            HealUpdate();

        // 각 클라이언트 로컬 UI 갱신
        UpdateUI();
    }

    // 로컬 플레이어가 힐 시작
    public void BeginInteract()
    {
        // 로컬 기준 힐 가능 여부 확인
        if (!CanHeal())
            return;

        // 이미 다른 플레이어가 힐 중이면 시작 불가
        if (IsBusy())
            return;

        // 로컬 체감용 처리
        FaceTarget();
        SetHealerLock(true);
        SetHealAnim(true);

        // 실제 시작은 서버에 요청
        CmdBeginHeal();
    }

    // 로컬 플레이어가 힐 중단
    public void EndInteract()
    {
        if (localHealerInteractor == null)
            return;

        // 힐러 쪽 로컬 효과 정리
        SetHealerLock(false);
        SetHealAnim(false);

        // UI는 직접 만지지 않고 Interactor를 통해 정리
        localHealerInteractor.HideProgress(this, true);

        // 실제 취소는 서버에 요청
        CmdEndHeal();
    }

    // 클라이언트 -> 서버 : 힐 시작 요청
    [Command(requiresAuthority = false)]
    private void CmdBeginHeal(NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null)
            return;

        SurvivorInteractor healerInteractor = sender.identity.GetComponent<SurvivorInteractor>();
        SurvivorState healerState = sender.identity.GetComponent<SurvivorState>();
        SurvivorMove healerMove = sender.identity.GetComponent<SurvivorMove>();

        // 힐러 필수 컴포넌트 확인
        if (healerInteractor == null || healerState == null || healerMove == null)
            return;

        // 대상이 정상 상태면 힐 불가
        if (targetState.IsHealthy)
            return;

        // 힐러가 다운 상태면 힐 불가
        if (healerState.IsDowned)
            return;

        // 자기 자신 힐 방지
        if (healerState == targetState)
            return;

        // 이미 다른 플레이어가 힐 중이면 시작 불가
        if (isHealing && healer != sender.identity.netId)
            return;

        // 범위 밖이면 시작 불가
        if (!CanUse(healerInteractor.transform))
            return;

        // 서버 기준으로 대상이 현재 상호작용 중이면 힐 시작 불가
        if (targetState.IsDoingInteraction)
            return;

        isHealing = true;
        healer = sender.identity.netId;

        // 힐받는 대상은 다른 상호작용 불가
        targetState.SetBeingHealedServer(true);

        // 힐받는 대상 본인 로컬에 이동 잠금 전달
        if (targetState.connectionToClient != null)
            TargetLockTarget(targetState.connectionToClient, true);
    }

    // 클라이언트 -> 서버 : 힐 취소 요청
    [Command(requiresAuthority = false)]
    private void CmdEndHeal(NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null)
            return;

        if (!isHealing)
            return;

        // 현재 힐 중인 본인만 취소 가능
        if (healer != sender.identity.netId)
            return;

        StopHeal();
    }

    // 서버에서 힐 진행 처리
    [Server]
    private void HealUpdate()
    {
        if (!isHealing)
            return;

        // 현재 힐 중인 플레이어 찾기
        if (!NetworkServer.spawned.TryGetValue(healer, out NetworkIdentity identity))
        {
            StopHeal();
            return;
        }

        SurvivorInteractor healerInteractor = identity.GetComponent<SurvivorInteractor>();
        SurvivorState healerState = identity.GetComponent<SurvivorState>();

        if (healerInteractor == null || healerState == null)
        {
            StopHeal();
            return;
        }

        // 대상이 정상 상태가 됐거나 힐러가 다운되면 종료
        if (targetState.IsHealthy || healerState.IsDowned)
        {
            StopHeal();
            return;
        }

        // 범위를 벗어나면 종료
        if (!CanUse(healerInteractor.transform))
        {
            StopHeal();
            return;
        }

        progress += Time.deltaTime;

        if (progress >= healTime)
            CompleteHeal();
    }

    // 서버에서 힐 중단 처리
    [Server]
    private void StopHeal()
    {
        isHealing = false;
        healer = 0;

        // 힐받는 상태 해제
        targetState.SetBeingHealedServer(false);

        // 힐받는 대상 본인 로컬에서 이동 잠금 해제
        if (targetState.connectionToClient != null)
            TargetLockTarget(targetState.connectionToClient, false);

        // 모든 클라이언트 로컬 효과 정리
        RpcStopHeal();
    }

    // 서버에서 힐 완료 처리
    [Server]
    private void CompleteHeal()
    {
        isHealing = false;
        healer = 0;

        // 힐받는 상태 해제
        targetState.SetBeingHealedServer(false);

        // 힐받는 대상 본인 로컬에서 이동 잠금 해제
        if (targetState.connectionToClient != null)
            TargetLockTarget(targetState.connectionToClient, false);

        // 다운 상태면 부상까지 회복
        if (targetState.IsDowned)
            targetState.RecoverToInjured();
        else if (targetState.IsInjured)
            targetState.HealToHealthy();

        progress = 0f;

        RpcStopHeal();
    }

    // 모든 클라이언트에서 로컬 효과 정리
    [ClientRpc]
    private void RpcStopHeal()
    {
        // 힐러 쪽 로컬
        if (localHealerMove != null)
        {
            localHealerMove.SetMoveLock(false);
            localHealerMove.SetSearching(false);
        }

        // 힐받는 대상 쪽 애니메이션 멈춤
        if (targetMove != null)
            targetMove.StopAnimation();

        // UI도 각자 자기 Interactor를 통해서만 정리
        if (localHealerInteractor != null)
            localHealerInteractor.HideProgress(this, true);

        if (localTargetInteractor != null)
            localTargetInteractor.HideProgress(this, true);
    }

    // 로컬 UI 갱신
    private void UpdateUI()
    {
        // 힐 대상 UI 참조 보정
        CacheTarget();

        // 힐하는 쪽 UI
        if (localHealerInteractor != null)
        {
            bool isMyHeal = false;

            if (isHealing && healer == localHealerInteractor.netId && !targetState.IsHealthy)
                isMyHeal = true;

            if (isMyHeal)
            {
                localHealerInteractor.ShowProgress(this, progress / healTime);
            }
            else
            {
                // UI숨김
                localHealerInteractor.HideProgress(this, false);
            }
        }

        // 힐받는 쪽 UI
        if (localTargetInteractor != null && targetState.isLocalPlayer)
        {
            bool isMyTargetHeal = false;

            if (isHealing && !targetState.IsHealthy)
                isMyTargetHeal = true;

            if (isMyTargetHeal)
            {
                localTargetInteractor.ShowProgress(this, progress / healTime);
            }
            else
            {
                localTargetInteractor.HideProgress(this, false);
            }
        }
    }

    // 로컬 기준 힐 가능한지 확인
    private bool CanHeal()
    {
        // 정상 상태는 힐 대상이 아님
        if (targetState.IsHealthy)
            return false;

        if (localHealerInteractor == null || localHealerState == null)
            return false;

        // 다운된 생존자는 다른 생존자를 힐할 수 없음
        if (localHealerState.IsDowned)
            return false;

        // 자기 자신 힐 방지
        if (localHealerState == targetState)
            return false;

        // 대상이 현재 다른 상호작용 중이면 힐 시작 불가
        if (targetState.IsDoingInteraction)
            return false;

        return true;
    }

    // 이미 다른 플레이어가 힐 중인지 확인
    private bool IsBusy()
    {
        if (!isHealing)
            return false;

        if (localHealerInteractor == null)
            return true;

        return healer != localHealerInteractor.netId;
    }

    // 서버 기준 범위 체크
    private bool CanUse(Transform healerTransform)
    {
        Collider col = GetComponent<Collider>();

        if (col == null)
            col = GetComponentInChildren<Collider>();

        if (col == null)
            return false;

        Vector3 closest = col.ClosestPoint(healerTransform.position);
        float sqrDist = (closest - healerTransform.position).sqrMagnitude;

        return sqrDist <= 4f;
    }

    // 로컬 힐 애니메이션 on/off
    private void SetHealAnim(bool value)
    {
        if (localHealerMove != null)
            localHealerMove.SetSearching(value);
    }

    // 로컬에서 힐 대상을 바라보게 함
    private void FaceTarget()
    {
        if (localHealerMove == null)
            return;

        Vector3 lookDir = targetState.transform.position - localHealerMove.transform.position;
        lookDir.y = 0f;

        if (lookDir.sqrMagnitude <= 0.001f)
            return;

        localHealerMove.FaceDirection(lookDir.normalized);
    }

    // 힐하는 쪽 이동 잠금
    private void SetHealerLock(bool value)
    {
        if (localHealerMove != null)
            localHealerMove.SetMoveLock(value);
    }

    // 이 힐 오브젝트가 붙어있는 대상이 내 로컬 플레이어인지 확인하고
    // 힐받는 대상 UI 참조를 캐싱
    private void CacheTarget()
    {
        if (!targetState.isLocalPlayer)
            return;

        if (localTargetInteractor == null)
            localTargetInteractor = targetInteractor;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Survivor"))
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();

        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        if (interactor == null || !interactor.isLocalPlayer)
            return;

        // 힐러 쪽 로컬 참조 캐싱
        localHealerInteractor = interactor;
        localHealerState = interactor.GetComponent<SurvivorState>();
        localHealerMove = interactor.GetComponent<SurvivorMove>();

        // 자기 자신 힐 방지
        if (localHealerState == targetState)
            return;

        // 이미 다른 플레이어가 힐 중이면 등록 안 함
        if (IsBusy())
            return;

        if (CanHeal())
            interactor.SetInteractable(this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Survivor"))
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();

        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        if (interactor == null || !interactor.isLocalPlayer)
            return;

        // 현재 상호작용 대상 해제
        interactor.ClearInteractable(this);

        if (localHealerInteractor != interactor)
            return;

        // 힐러 쪽 로컬 효과 정리
        SetHealerLock(false);
        SetHealAnim(false);

        // UI 정리
        localHealerInteractor.HideProgress(this, true);

        // 실제 힐 종료는 서버에 요청
        CmdEndHeal();

        localHealerInteractor = null;
        localHealerState = null;
        localHealerMove = null;
    }

    // 힐받는 대상 본인 클라이언트에게만 이동 잠금/해제를 보냄
    // 힐러 로컬에서 targetMove.SetMoveLock을 호출해도
    // 대상 본인 입력은 막히지 않기 때문에 이 TargetRpc가 필요함
    [TargetRpc]
    private void TargetLockTarget(NetworkConnection target, bool value)
    {
        if (targetMove == null)
            return;

        targetMove.SetMoveLock(value);

        // 잠글 때 이동 애니메이션도 같이 멈춤
        if (value)
            targetMove.StopAnimation();
    }
}