using Mirror;
using UnityEngine;

public class SurvivorHeal : NetworkBehaviour, IInteractable
{
    // 이 오브젝트는 Hold 타입 상호작용
    public InteractType InteractType => InteractType.Hold;

    [Header("힐 설정")]
    [SerializeField] private float healTime = 30f;
    [SerializeField] private ProgressUI progressUI; // 현재 상호작용 중인 로컬 플레이어의 UI를 연결받음

    [Header("참조")]
    [SerializeField] private SurvivorState targetState; // 힐 받을 대상 상태
    [SerializeField] private SurvivorMove targetMove;   // 힐 받을 대상 이동 제어

    [SyncVar]
    private uint healerNetId; // 현재 힐 중인 플레이어 netId

    [SyncVar]
    private bool isHealing; // 현재 힐 진행 중인지

    [SyncVar]
    private float progress; // 현재 힐 진행도

    private SurvivorInteractor localHealerInteractor;
    private SurvivorState localHealerState;
    private SurvivorMove localHealerMove;

    private void Awake()
    {
        if (targetState == null)
            targetState = GetComponentInParent<SurvivorState>();

        if (targetMove == null && targetState != null)
            targetMove = targetState.GetComponent<SurvivorMove>();
    }

    private void Update()
    {
        // 실제 힐 진행은 서버에서만 처리
        if (isServer)
        {
            ServerUpdateHeal();
        }

        // UI는 각 클라이언트 로컬에서만 표시
        UpdateLocalUI();
    }

    // 로컬 플레이어가 힐 시작
    public void BeginInteract()
    {
        if (!CanHealLocal())
            return;

        // 이미 다른 사람이 힐 중이면 시작 불가
        if (IsBusyByOtherLocal())
            return;

        // 로컬 체감용 처리
        FaceToTargetLocal();
        LockMovementLocal(true);
        SetHealAnimationLocal(true);

        // 실제 시작은 서버에 요청
        CmdBeginHeal();
    }

    // 로컬 플레이어가 힐 중단
    public void EndInteract()
    {
        if (localHealerInteractor == null)
            return;

        LockMovementLocal(false);
        SetHealAnimationLocal(false);

        if (progressUI != null)
            progressUI.Hide();

        CmdEndHeal();
    }

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

        // SurvivorMove가 없으면 힐 불가
        if (sender.identity.GetComponent<SurvivorMove>() == null)
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

        // 이미 다른 플레이어가 힐 중이면 막기
        if (isHealing && healerNetId != sender.identity.netId)
            return;

        // 서버 기준 범위 체크
        if (!CanHealerUseThis(healerInteractor.transform))
            return;

        isHealing = true;
        healerNetId = sender.identity.netId;
        progress = 0f;
    }

    [Command(requiresAuthority = false)]
    private void CmdEndHeal(NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null)
            return;

        if (!isHealing)
            return;

        // 현재 힐 중인 본인만 취소 가능
        if (healerNetId != sender.identity.netId)
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

        if (!NetworkServer.spawned.TryGetValue(healerNetId, out NetworkIdentity identity))
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

        // 대상이 이미 정상 상태가 되면 힐 종료
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

    // 서버에서 힐 중단 처리
    [Server]
    private void StopServerHeal()
    {
        isHealing = false;
        healerNetId = 0;
        progress = 0f;

        RpcForceStopLocalEffects();
    }

    // 서버에서 힐 완료 처리
    [Server]
    private void CompleteHealServer()
    {
        isHealing = false;
        healerNetId = 0;
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

    // 현재 로컬 플레이어가 힐 중일 때만 UI 표시
    private void UpdateLocalUI()
    {
        if (progressUI == null)
            return;

        if (localHealerInteractor == null)
            return;

        bool isMyHeal =
            isHealing &&
            healerNetId == localHealerInteractor.netId &&
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

        if (targetState.IsHealthy)
            return false;

        if (localHealerInteractor == null)
            return false;

        if (localHealerState == null)
            return false;

        // 다운된 생존자는 다른 생존자를 힐할 수 없음
        if (localHealerState.IsDowned)
            return false;

        // 자기 자신 힐 방지
        if (localHealerState == targetState)
            return false;

        return true;
    }

    // 이미 다른 플레이어가 힐 중인지 확인
    private bool IsBusyByOtherLocal()
    {
        if (!isHealing)
            return false;

        if (localHealerInteractor == null)
            return true;

        return healerNetId != localHealerInteractor.netId;
    }

    // 서버 기준 범위 체크
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

    // 로컬 힐 애니메이션 on/off
    private void SetHealAnimationLocal(bool value)
    {
        if (localHealerMove != null)
            localHealerMove.SetSearching(value);
    }

    // 로컬에서 힐 대상을 바라보게 함
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

    // 로컬에서 힐러/대상 둘 다 이동 잠금
    private void LockMovementLocal(bool value)
    {
        if (localHealerMove != null)
            localHealerMove.SetMoveLock(value);

        if (targetMove != null)
            targetMove.SetMoveLock(value);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Survivor"))
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        if (interactor == null)
            return;

        // 로컬 플레이어만 자기 UI를 연결
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

        // 이 로컬 플레이어가 들고 있는 ProgressUI를 연결
        progressUI = interactor.ProgressUI;

        // 자기 자신 힐 방지
        if (targetState != null && state == targetState)
            return;

        // 이미 다른 플레이어가 힐 중이면 상호작용 등록 안 함
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
            LockMovementLocal(false);
            SetHealAnimationLocal(false);

            if (progressUI != null)
            {
                progressUI.SetProgress(0f);
                progressUI.Hide();
            }

            CmdEndHeal();

            localHealerInteractor = null;
            localHealerState = null;
            localHealerMove = null;

            Debug.Log($"{name} 힐 범위 이탈");
        }
    }
}