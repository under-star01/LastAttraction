using Mirror;
using UnityEngine;

public class SurvivorHeal : NetworkBehaviour, IInteractable
{
    // 이 오브젝트는 Hold 타입 상호작용
    public InteractType InteractType => InteractType.Hold;

    [Header("힐 설정")]
    [SerializeField] private float healTime = 30f;      // 힐 완료까지 걸리는 시간
    [SerializeField] private ProgressUI progressUI;     // 진행도 UI

    [Header("참조")]
    [SerializeField] private SurvivorState targetState; // 힐 받을 대상 상태
    [SerializeField] private SurvivorMove targetMove;   // 힐 받을 대상 이동 제어

    // 현재 힐 중인 플레이어
    [SyncVar]
    private uint healer;

    // 현재 힐 진행 중인지
    [SyncVar]
    private bool isHealing;

    // 현재 힐 진행도
    [SyncVar]
    private float progress;

    // 로컬 플레이어 캐시
    private SurvivorInteractor localHealerInteractor;
    private SurvivorState localHealerState;
    private SurvivorMove localHealerMove;

    private void Awake()
    {
        if (targetState == null)
            targetState = GetComponentInParent<SurvivorState>();

        if (targetMove == null && targetState != null)
            targetMove = targetState.GetComponent<SurvivorMove>();

        if (progressUI == null)
            progressUI = FindFirstObjectByType<ProgressUI>();
    }

    private void Update()
    {
        // 실제 힐 진행은 서버에서만 증가
        if (isServer)
        {
            ServerUpdateHeal();
        }

        // UI는 각 클라이언트 로컬에서 표시
        UpdateLocalUI();
    }

    // 힐 시작
    public void BeginInteract()
    {
        // 로컬 기준으로 먼저 가능한지 체크
        if (!CanHealLocal())
            return;

        // 이미 다른 사람이 힐 중이면 시작 막기
        if (IsBusyByOtherLocal())
            return;

        // 힐 시작할 때 힐하는 사람이 대상을 바라보게 함
        FaceToTargetLocal();

        // 로컬에서 즉시 이동 잠금
        LockMovementLocal(true);

        // 로컬에서 즉시 애니메이션 켜기
        SetHealAnimationLocal(true);

        // 서버에 힐 시작 요청
        CmdBeginHeal();
    }

    // 힐 중단
    public void EndInteract()
    {
        if (localHealerInteractor == null)
            return;

        // 로컬 효과 먼저 해제
        LockMovementLocal(false);
        SetHealAnimationLocal(false);

        // 진행도 UI 숨김
        if (progressUI != null)
            progressUI.Hide();

        // 서버에 힐 중단 요청
        CmdEndHeal();
    }

    // 서버에 힐 시작 요청
    [Command(requiresAuthority = false)]
    private void CmdBeginHeal(NetworkConnectionToClient sender = null)
    {
        if (targetState == null)
            return;

        if (sender == null || sender.identity == null)
            return;

        SurvivorInteractor healerInteractor = sender.identity.GetComponent<SurvivorInteractor>();
        if (healerInteractor == null)
            return;

        SurvivorState healerState = sender.identity.GetComponent<SurvivorState>();
        if (healerState == null)
            return;

        SurvivorMove healerMove = sender.identity.GetComponent<SurvivorMove>();
        if (healerMove == null)
            return;

        // 대상이 이미 정상 상태면 힐 불가
        if (targetState.IsHealthy)
            return;

        // 힐하는 쪽이 다운 상태면 힐 불가
        if (healerState.IsDowned)
            return;

        // 자기 자신 힐 방지
        if (healerState == targetState)
            return;

        // 이미 다른 사람이 힐 중이면 막기
        if (isHealing && healer != sender.identity.netId)
            return;

        // 범위 벗어났으면 시작 불가
        if (!CanHealerUseThis(healerInteractor.transform))
            return;

        isHealing = true;
        healer = sender.identity.netId;
        progress = 0f;
    }

    // 서버에 힐 중단 요청
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

        StopServerHeal();
    }

    // 서버에서 매 프레임 힐 진행
    [Server]
    private void ServerUpdateHeal()
    {
        if (!isHealing)
            return;

        if (targetState == null)
        {
            StopServerHeal();
            return;
        }

        // 힐러 찾기
        if (!NetworkServer.spawned.TryGetValue(healer, out NetworkIdentity identity))
        {
            StopServerHeal();
            return;
        }

        SurvivorInteractor healerInteractor = identity.GetComponent<SurvivorInteractor>();
        if (healerInteractor == null)
        {
            StopServerHeal();
            return;
        }

        SurvivorState healerState = identity.GetComponent<SurvivorState>();
        if (healerState == null)
        {
            StopServerHeal();
            return;
        }

        // 대상이 정상 상태가 되면 힐 종료
        if (targetState.IsHealthy)
        {
            StopServerHeal();
            return;
        }

        // 힐러가 다운되면 힐 종료
        if (healerState.IsDowned)
        {
            StopServerHeal();
            return;
        }

        // 범위 벗어나면 힐 종료
        if (!CanHealerUseThis(healerInteractor.transform))
        {
            StopServerHeal();
            return;
        }

        progress += Time.deltaTime;

        if (progress >= healTime)
        {
            CompleteHealServer();
        }
    }

    // 서버에서 힐 강제 종료
    [Server]
    private void StopServerHeal()
    {
        isHealing = false;
        healer = 0;
        progress = 0f;

        // 로컬 효과 강제 종료
        RpcForceStopLocalEffects();
    }

    // 서버에서 힐 완료
    [Server]
    private void CompleteHealServer()
    {
        isHealing = false;
        healer = 0;
        progress = healTime;

        if (targetState == null)
        {
            RpcForceStopLocalEffects();
            return;
        }

        // 다운 상태면 부상 상태까지 회복
        if (targetState.IsDowned)
        {
            targetState.RecoverToInjured();
            Debug.Log($"{name} : 다운 상태 힐 완료 -> 부상 상태로 회복");
        }
        // 부상 상태면 정상 상태까지 회복
        else if (targetState.IsInjured)
        {
            targetState.HealToHealthy();
            Debug.Log($"{name} : 부상 상태 힐 완료 -> 정상 상태로 회복");
        }

        progress = 0f;

        // 로컬 효과 강제 종료
        RpcForceStopLocalEffects();
    }

    // 모든 클라이언트에서 로컬 효과 정리
    [ClientRpc]
    private void RpcForceStopLocalEffects()
    {
        if (localHealerMove != null)
        {
            localHealerMove.SetMoveLock(false);
            localHealerMove.SetSearching(false);
        }

        if (targetMove != null)
        {
            targetMove.SetMoveLock(false);
        }

        if (progressUI != null)
        {
            progressUI.SetProgress(0f);
            progressUI.Hide();
        }
    }

    // 로컬 플레이어 UI 갱신
    private void UpdateLocalUI()
    {
        if (progressUI == null)
            return;

        if (localHealerInteractor == null)
            return;

        // 내가 현재 힐 중인 플레이어인지 확인
        bool isMyHeal =
            isHealing &&
            healer == localHealerInteractor.netId &&
            targetState != null &&
            !targetState.IsHealthy;

        if (isMyHeal)
        {
            progressUI.Show();
            progressUI.SetProgress(progress / healTime);
        }
        else
        {
            progressUI.Hide();
        }
    }

    // 로컬 기준 힐 가능한지 확인
    private bool CanHealLocal()
    {
        if (targetState == null)
            return false;

        // 대상이 정상 상태면 힐 불가
        if (targetState.IsHealthy)
            return false;

        // 힐러 정보 없으면 불가
        if (localHealerInteractor == null)
            return false;

        if (localHealerState == null)
            return false;

        // 다운된 생존자는 다른 사람 힐 불가
        if (localHealerState.IsDowned)
            return false;

        // 자기 자신 힐 방지
        if (localHealerState == targetState)
            return false;

        return true;
    }

    // 로컬 기준으로 "다른 사람이 이미 힐 중인지" 확인
    private bool IsBusyByOtherLocal()
    {
        if (!isHealing)
            return false;

        if (localHealerInteractor == null)
            return true;

        return healer != localHealerInteractor.netId;
    }

    // 힐 범위 체크
    private bool CanHealerUseThis(Transform healerTransform)
    {
        if (healerTransform == null)
            return false;

        Collider myCol = GetComponent<Collider>();
        if (myCol == null)
            myCol = GetComponentInChildren<Collider>();

        if (myCol == null)
            return false;

        Vector3 closest = myCol.ClosestPoint(healerTransform.position);
        float sqrDist = (closest - healerTransform.position).sqrMagnitude;

        return sqrDist <= 4f;
    }

    // 힐 애니메이션 on/off
    private void SetHealAnimationLocal(bool value)
    {
        if (localHealerMove != null)
            localHealerMove.SetSearching(value);
    }

    // 힐하는 사람이 힐 대상을 바라보게 함
    private void FaceToTargetLocal()
    {
        if (localHealerMove == null || targetState == null)
            return;

        Vector3 lookDir = targetState.transform.position - localHealerMove.transform.position;
        lookDir.y = 0f;

        if (lookDir.sqrMagnitude <= 0.001f)
            return;

        localHealerMove.FaceDirection(lookDir.normalized);
    }

    // 힐하는 사람 / 힐받는 사람 둘 다 이동 잠금/해제
    private void LockMovementLocal(bool value)
    {
        if (localHealerMove != null)
            localHealerMove.SetMoveLock(value);

        if (targetMove != null)
            targetMove.SetMoveLock(value);
    }

    // 범위 안에 들어오면 로컬 플레이어만 상호작용 가능 대상으로 등록
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Survivor"))
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        if (interactor == null)
            return;

        if (!interactor.isLocalPlayer)
            return;

        SurvivorState state = interactor.GetComponent<SurvivorState>();
        if (state == null)
            state = interactor.GetComponentInParent<SurvivorState>();

        SurvivorMove move = interactor.GetComponent<SurvivorMove>();
        if (move == null)
            move = interactor.GetComponentInParent<SurvivorMove>();

        localHealerInteractor = interactor;
        localHealerState = state;
        localHealerMove = move;

        // 자기 자신 힐 방지
        if (targetState != null && state == targetState)
            return;

        // 이미 다른 사람이 힐 중이면 상호작용 등록 안 함
        if (IsBusyByOtherLocal())
        {
            Debug.Log($"{name} : 다른 플레이어가 힐 중이라 상호작용 불가");
            return;
        }

        if (CanHealLocal())
        {
            interactor.SetInteractable(this);
            Debug.Log($"{name} 힐 범위 진입");
        }
    }

    // 범위를 벗어나면 상호작용 해제
    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Survivor"))
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        if (interactor == null)
            return;

        if (!interactor.isLocalPlayer)
            return;

        interactor.ClearInteractable(this);

        if (localHealerInteractor == interactor)
        {
            // 로컬 효과 즉시 해제
            LockMovementLocal(false);
            SetHealAnimationLocal(false);

            if (progressUI != null)
            {
                progressUI.SetProgress(0f);
                progressUI.Hide();
            }

            // 서버에도 힐 중단 요청
            CmdEndHeal();

            localHealerInteractor = null;
            localHealerState = null;
            localHealerMove = null;

            Debug.Log($"{name} 힐 범위 이탈");
        }
    }
}