using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class UploadComputer : NetworkBehaviour, IInteractable
{
    // 업로드 컴퓨터는 누르고 있는 동안 진행되는 Hold 상호작용이다.
    public InteractType InteractType => InteractType.Hold;

    // 모든 증거를 모은 뒤 컴퓨터가 사용 가능한지 여부다.
    [SyncVar]
    private bool isOpen;

    // ProgressUI에 보여줄 공유 업로드 진행도다.
    [SyncVar]
    private float uploadProgress01;

    // 이 컴퓨터를 현재 사용 중인 생존자 netId 목록이다.
    private readonly HashSet<uint> users = new HashSet<uint>();

    // 로컬 플레이어의 상호작용 컴포넌트다.
    private SurvivorInteractor localInteractor;

    // 로컬 플레이어의 이동 컴포넌트다.
    private SurvivorMove localMove;

    // 로컬 플레이어의 상태 컴포넌트다.
    private SurvivorState localState;

    // 로컬 플레이어가 컴퓨터 범위 안에 있는지 여부다.
    private bool isLocalInside;

    // 내 로컬 플레이어가 업로드 중인지 여부다.
    private bool isUploading;

    public bool IsOpen => isOpen;

    private void Update()
    {
        // 서버에서만 실제 업로드 진행도를 증가시킨다.
        if (isServer)
            TickUpload();

        // 로컬 플레이어에게 기존 ProgressUI로 공유 진행도를 보여준다.
        UpdateUI();

        // 로컬 플레이어가 범위 안에 있으면 상호작용 후보 상태를 갱신한다.
        RefreshUse();
    }

    // 이 컴퓨터를 사용 중인 사람 수만큼 GameManager 공유 progress를 올린다.
    [Server]
    private void TickUpload()
    {
        if (!isOpen)
            return;

        if (GameManager.Instance == null)
            return;

        GameManager.Instance.AddUpload(users.Count);
    }

    // GameManager가 모든 증거 완료 후 컴퓨터를 활성화할 때 호출한다.
    [Server]
    public void SetOpen(bool value)
    {
        isOpen = value;
    }

    // GameManager가 서버 progress를 UI용 SyncVar로 전달할 때 호출한다.
    [Server]
    public void SetProgress(float value)
    {
        uploadProgress01 = Mathf.Clamp01(value);
    }

    // 업로드 완료 시 모든 사용자와 로컬 UI 상태를 정리한다.
    [Server]
    public void StopAllUsers()
    {
        isOpen = false;
        users.Clear();

        StopUploadLoopSound();

        RpcStopAll();
    }

    // 로컬에서 업로드 상호작용을 시작한다.
    public void BeginInteract(GameObject actor)
    {
        if (!CanStart())
            return;

        StartFx();

        NetworkIdentity identity = actor.GetComponent<NetworkIdentity>();

        if (identity == null)
            identity = actor.GetComponentInParent<NetworkIdentity>();

        if (identity == null)
        {
            StopFx(true);
            return;
        }

        CmdStart(identity.netId);
    }

    // 로컬에서 손을 떼면 업로드 상호작용을 종료한다.
    public void EndInteract()
    {
        StopFx(false);
        CmdStop();
    }

    // 서버에서 업로드 시작 가능 여부를 최종 검사한다.
    [Command(requiresAuthority = false)]
    private void CmdStart(uint actorNetId, NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null)
            return;

        if (sender.identity.netId != actorNetId)
            return;

        if (!isOpen)
        {
            TargetStop(sender);
            return;
        }

        if (GameManager.Instance == null || GameManager.Instance.GateOpened)
        {
            TargetStop(sender);
            return;
        }

        SurvivorState state = sender.identity.GetComponent<SurvivorState>();
        SurvivorActionState actionState = sender.identity.GetComponent<SurvivorActionState>();

        if (state == null)
        {
            TargetStop(sender);
            return;
        }

        if (state.IsDowned || state.IsDead || state.IsImprisoned)
        {
            TargetStop(sender);
            return;
        }

        if (actionState != null && actionState.IsBusy)
        {
            TargetStop(sender);
            return;
        }

        bool wasEmpty = users.Count == 0;

        users.Add(sender.identity.netId);

        // 첫 사용자가 업로드를 시작했을 때만 루프 사운드 시작
        if (wasEmpty && users.Count > 0)
            StartUploadLoopSound();

        if (actionState != null)
        {
            actionState.SetInteract(true);
            actionState.SetCam(false);
        }
    }

    // 서버에서 업로드 종료를 처리한다.
    [Command(requiresAuthority = false)]
    private void CmdStop(NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null)
            return;

        users.Remove(sender.identity.netId);

        if (users.Count <= 0)
            StopUploadLoopSound();

        SurvivorActionState actionState = sender.identity.GetComponent<SurvivorActionState>();

        if (actionState != null)
            actionState.SetInteract(false);
    }

    // 서버에서 업로드 시작이 거절되면 해당 클라이언트만 정리한다.
    [TargetRpc]
    private void TargetStop(NetworkConnection target)
    {
        StopFx(true);

        if (localInteractor != null)
            localInteractor.ClearInteractable(this);
    }

    // 업로드 완료 시 모든 클라이언트의 업로드 UI와 이동 잠금을 정리한다.
    [ClientRpc]
    private void RpcStopAll()
    {
        StopFx(true);

        if (localInteractor != null)
            localInteractor.ClearInteractable(this);
    }

    // 로컬 기준으로 업로드를 시작할 수 있는지 빠르게 검사한다.
    private bool CanStart()
    {
        if (!isOpen)
            return false;

        if (localInteractor == null || localState == null)
            return false;

        if (localState.IsDowned || localState.IsDead || localState.IsImprisoned)
            return false;

        return true;
    }

    // 업로드 시작 시 로컬 이동 잠금과 Searching 애니메이션을 켠다.
    private void StartFx()
    {
        isUploading = true;

        if (localMove == null)
            return;

        localMove.SetMoveLock(true);
        localMove.SetCamAnim(false);
        localMove.SetSearching(true);

        Vector3 lookDir = transform.position - localMove.transform.position;
        lookDir.y = 0f;

        if (lookDir.sqrMagnitude > 0.001f)
            localMove.FaceDirection(lookDir.normalized);
    }

    // 업로드 종료 시 로컬 이동 잠금과 ProgressUI를 정리한다.
    private void StopFx(bool resetProgress)
    {
        isUploading = false;

        if (localMove != null)
        {
            localMove.SetMoveLock(false);
            localMove.SetSearching(false);
            localMove.SetCamAnim(false);
        }

        if (localInteractor != null)
            localInteractor.HideProgress(this, resetProgress);
    }

    // 업로드 중인 로컬 플레이어에게 동기화된 공유 progress를 보여준다.
    private void UpdateUI()
    {
        if (!isUploading)
            return;

        if (localInteractor == null)
            return;

        localInteractor.ShowProgress(this, uploadProgress01);
    }

    // 범위 안에 있는 로컬 플레이어에게 상호작용 가능 여부를 갱신한다.
    private void RefreshUse()
    {
        if (!isLocalInside)
            return;

        if (localInteractor == null)
            return;

        if (CanStart())
            localInteractor.SetInteractable(this);
        else
            localInteractor.ClearInteractable(this);
    }

    // 사운드
    [Server]
    private void StartUploadLoopSound()
    {
        NetworkAudioManager.StartLoopAudioForEveryone(
            netId,
            AudioKey.SurvivorUploadLoop,
            AudioDimension.Sound3D,
            transform.position
        );
    }

    [Server]
    private void StopUploadLoopSound()
    {
        NetworkAudioManager.StopLoopAudioForEveryone(
            netId,
            AudioKey.SurvivorUploadLoop
        );
    }

    // 로컬 생존자가 Trigger 범위에 들어오면 상호작용 후보로 등록한다.
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Survivor"))
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();

        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        if (interactor == null || !interactor.isLocalPlayer)
            return;

        localInteractor = interactor;
        localMove = interactor.GetComponent<SurvivorMove>();
        localState = interactor.GetComponent<SurvivorState>();

        isLocalInside = true;

        RefreshUse();
    }

    // 로컬 생존자가 Trigger 범위 밖으로 나가면 업로드를 끊고 후보에서 제거한다.
    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Survivor"))
            return;

        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();

        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        if (interactor == null || !interactor.isLocalPlayer)
            return;

        interactor.ClearInteractable(this);

        isLocalInside = false;

        if (localInteractor == interactor)
        {
            StopFx(true);
            CmdStop();

            localInteractor = null;
            localMove = null;
            localState = null;
        }
    }
}