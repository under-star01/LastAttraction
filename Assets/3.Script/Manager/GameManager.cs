using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    // 어디서든 GameManager에 접근하기 위한 싱글턴이다.
    public static GameManager Instance { get; private set; }

    [Header("생존자 입력 상태")]
    [SerializeField] private bool survivorInputEnabled;

    [Header("증거 목표")]
    [SerializeField] private int needEvidenceCount = 0;

    [Header("통합 목표 게이지")]
    [SerializeField] private float objectiveMaxValue = 1f;

    // 진짜 증거 하나를 찾을 때 목표 게이지에 더해지는 값이다. 0.15 = 15%
    [SerializeField] private float evidenceObjectiveAdd = 0.15f;

    // 살인마 촬영만으로 목표 게이지 100%를 채우는 데 필요한 기준 시간이다.
    [SerializeField] private float needKillerDetectTime = 120f;

    // 서버에서 실제로 관리하는 통합 목표 게이지다.
    [SerializeField] private float objectiveProgress;

    // 현재 살인마를 촬영 중인 생존자 수다.
    [SerializeField] private int currentKillerDetectUserCount;

    // 현재 적용 중인 촬영 배율이다.
    [SerializeField] private float currentKillerDetectMultiplier;

    // 클라이언트 UI 표시용 통합 목표 게이지다.
    [SyncVar] private float syncedObjectiveProgress;

    // 클라이언트 UI 표시용 촬영 인원 수다.
    [SyncVar] private int syncedKillerDetectUserCount;

    // 클라이언트 UI 표시용 촬영 배율이다.
    [SyncVar] private float syncedKillerDetectMultiplier;

    [Header("업로드")]
    [SerializeField] private float uploadTime = 60f;
    [SerializeField] private UploadComputer[] uploadComputers;

    [Header("업로드 알림음")]
    [SerializeField] private AudioKey uploadNoticeSoundKey = AudioKey.UploadComputerReady;

    [Header("탈출문")]
    [SerializeField] private EscapeGate[] escapeGates;

    [Header("게임 종료 위치")]
    [SerializeField] private Transform[] survivorResultPoints;
    [SerializeField] private Transform killerResultPoint;

    [Header("탈출문 개방 대기")]
    [SerializeField] private float gateOpenDelay = 60f;

    // 맵에 있는 EvidenceZone들을 서버에서 등록해둔다.
    private readonly HashSet<EvidenceZone> zones = new HashSet<EvidenceZone>();

    // 이미 진짜 증거를 찾은 EvidenceZone들을 서버에서 기록한다.
    private readonly HashSet<EvidenceZone> foundZones = new HashSet<EvidenceZone>();

    // 현재 찾은 진짜 증거 개수다.
    private int foundEvidenceCount;

    // 업로드 컴퓨터 사용 가능 여부다.
    private bool canUpload;

    // 서버에서만 관리하는 공유 업로드 진행도다.
    private float uploadProgress;

    // 업로드 완료 후 문이 열리기 전 대기 중인지 여부다.
    private bool isWaitingGateOpen;

    // 탈출문이 열리기까지 남은 시간이다.
    private float gateRemainTime;

    // 탈출문이 열렸는지 여부다.
    private bool gateOpened;

    public bool SurvivorInputEnabled => survivorInputEnabled;
    public int FoundEvidenceCount => foundEvidenceCount;
    public int NeedEvidenceCount => GetNeedCount();
    public bool CanUpload => canUpload;
    public bool GateOpened => gateOpened;
    public bool IsWaitingGateOpen => isWaitingGateOpen;
    public float GateRemainTime => gateRemainTime;
    public float GateOpenDelay => gateOpenDelay;
    public float NeedKillerDetectTime => needKillerDetectTime;
    public float EvidenceObjectiveAdd => evidenceObjectiveAdd;
    public Transform KillerResultPoint => killerResultPoint;

    public bool IsEvidenceComplete
    {
        get
        {
            int needCount = GetNeedCount();

            if (needCount <= 0)
                return false;

            return foundEvidenceCount >= needCount;
        }
    }

    public float ObjectiveProgress
    {
        get
        {
            // 서버에서는 실제 목표 게이지를 사용한다.
            if (NetworkServer.active)
                return objectiveProgress;

            // 클라이언트에서는 SyncVar로 받은 목표 게이지를 사용한다.
            return syncedObjectiveProgress;
        }
    }

    public float ObjectiveProgress01
    {
        get
        {
            if (objectiveMaxValue <= 0f)
                return 1f;

            return Mathf.Clamp01(ObjectiveProgress / objectiveMaxValue);
        }
    }

    public bool IsObjectiveComplete
    {
        get
        {
            return ObjectiveProgress01 >= 1f;
        }
    }

    public int CurrentKillerDetectUserCount
    {
        get
        {
            // 서버에서는 실제 촬영 인원 수를 사용한다.
            if (NetworkServer.active)
                return currentKillerDetectUserCount;

            // 클라이언트에서는 SyncVar로 받은 촬영 인원 수를 사용한다.
            return syncedKillerDetectUserCount;
        }
    }

    public float CurrentKillerDetectMultiplier
    {
        get
        {
            // 서버에서는 실제 촬영 배율을 사용한다.
            if (NetworkServer.active)
                return currentKillerDetectMultiplier;

            // 클라이언트에서는 SyncVar로 받은 촬영 배율을 사용한다.
            return syncedKillerDetectMultiplier;
        }
    }

    public float UploadProgress01
    {
        get
        {
            // 업로드 시간이 0 이하이면 완료로 처리한다.
            if (uploadTime <= 0f)
                return 1f;

            return Mathf.Clamp01(uploadProgress / uploadTime);
        }
    }

    public float GateRemain01
    {
        get
        {
            // 대기 시간이 0 이하이면 0으로 처리한다.
            if (gateOpenDelay <= 0f)
                return 0f;

            return Mathf.Clamp01(gateRemainTime / gateOpenDelay);
        }
    }

    // 기존 UI / 기존 코드 호환용 프로퍼티다.
    public float KillerDetectProgress
    {
        get
        {
            return ObjectiveProgress;
        }
    }

    // 기존 CameraProgressUI나 다른 코드가 이 값을 읽어도 통합 목표 게이지를 반환한다.
    public float KillerDetectProgress01
    {
        get
        {
            return ObjectiveProgress01;
        }
    }

    // 이제 촬영 단독 완료가 아니라 통합 목표 완료 여부를 반환한다.
    public bool IsKillerDetectComplete
    {
        get
        {
            return IsObjectiveComplete;
        }
    }

    private void Awake()
    {
        // 중복 GameManager가 생기면 제거한다.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 현재 GameManager를 싱글턴으로 저장한다.
        Instance = this;
    }

    private void Start()
    {
        // 서버에서만 시작 입력 상태를 정한다.
        if (NetworkServer.active)
            SetAllSurvivorInput(false);
    }

    private void Update()
    {
        // 서버에서만 목표 진행과 문 개방 타이머를 계산한다.
        if (!NetworkServer.active)
            return;

        UpdateObjectiveByCamera();
        TickGateOpenTimer();
    }

    // 모든 생존자의 입력 가능 여부를 서버에서 바꾼다.
    public void SetAllSurvivorInput(bool value)
    {
        if (!NetworkServer.active)
            return;

        survivorInputEnabled = value;

        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn == null || conn.identity == null)
                continue;

            SurvivorInput input = conn.identity.GetComponent<SurvivorInput>();

            if (input == null)
                continue;

            input.SetInputEnabledServer(value);
        }

        Debug.Log($"[GameManager] 생존자 입력 상태 변경: {value}");
    }

    // 게임 시작 시 생존자 입력을 허용한다.
    public void StartGame()
    {
        SetAllSurvivorInput(true);
    }

    // 로비 진입 시 생존자 입력을 막는다.
    public void EnterLobby()
    {
        SetAllSurvivorInput(false);
    }

    // EvidenceZone이 시작될 때 자기 자신을 등록한다.
    public void AddZone(EvidenceZone zone)
    {
        if (!NetworkServer.active)
            return;

        if (zone == null)
            return;

        zones.Add(zone);

        Debug.Log($"[GameManager] EvidenceZone 등록: {zone.name} / 총 {zones.Count}개");
    }

    // 진짜 증거 발견 시 EvidenceZone에서 호출한다.
    public void AddEvidence(EvidenceZone zone)
    {
        if (!NetworkServer.active)
            return;

        if (zone == null)
            return;

        // 같은 구역이 여러 번 카운트되는 것을 막는다.
        if (foundZones.Contains(zone))
            return;

        foundZones.Add(zone);
        foundEvidenceCount = foundZones.Count;

        // 증거 하나를 찾으면 통합 목표 게이지를 15% 증가시킨다.
        AddObjective(evidenceObjectiveAdd);

        Debug.Log(
            $"[GameManager] 진짜 증거 발견: {foundEvidenceCount}/{GetNeedCount()} / " +
            $"목표 게이지: {ObjectiveProgress01 * 100f:F0}%"
        );

        CheckUpload();
    }

    // 서버에서 통합 목표 게이지를 증가시킨다.
    [Server]
    private void AddObjective(float amount)
    {
        if (canUpload || isWaitingGateOpen || gateOpened)
            return;

        if (amount <= 0f)
            return;

        objectiveProgress += amount;
        objectiveProgress = Mathf.Clamp(objectiveProgress, 0f, objectiveMaxValue);

        SyncObjectiveState();
    }

    // 서버에서 살인마 촬영으로 통합 목표 게이지를 갱신한다.
    private void UpdateObjectiveByCamera()
    {
        // 업로드 가능 / 문 대기 / 문 열림 상태에서는 목표 진행을 멈춘다.
        if (canUpload || isWaitingGateOpen || gateOpened)
        {
            SyncObjectiveState();
            return;
        }

        // 목표 게이지가 이미 다 찼으면 업로드 조건을 확인한다.
        if (IsObjectiveComplete)
        {
            objectiveProgress = objectiveMaxValue;
            currentKillerDetectUserCount = 0;
            currentKillerDetectMultiplier = 0f;

            SyncObjectiveState();
            CheckUpload();
            return;
        }

        // 기준 시간이 0 이하이면 카메라 촬영으로는 증가하지 않게 막는다.
        if (needKillerDetectTime <= 0f)
        {
            SyncObjectiveState();
            return;
        }

        int detectingCount = GetRecordingKillerSurvivorCount();

        currentKillerDetectUserCount = detectingCount;
        currentKillerDetectMultiplier = GetKillerDetectMultiplier(detectingCount);

        // 아무도 살인마를 촬영 중이 아니면 진행도 증가 없음.
        if (currentKillerDetectMultiplier <= 0f)
        {
            SyncObjectiveState();
            return;
        }

        // 기존 촬영 방식처럼 시간과 배율에 따라 통합 목표 게이지를 증가시킨다.
        float addValue = Time.deltaTime * currentKillerDetectMultiplier / needKillerDetectTime;
        AddObjective(addValue);

        // 목표를 막 달성했다면 업로드 가능 조건을 다시 검사한다.
        if (IsObjectiveComplete)
        {
            Debug.Log("[GameManager] 통합 목표 게이지 완료.");
            CheckUpload();
        }
    }

    // 현재 카메라로 살인마를 촬영 중인 생존자 수를 계산한다.
    private int GetRecordingKillerSurvivorCount()
    {
        int count = 0;

        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn == null || conn.identity == null)
                continue;

            SurvivorCameraSkill cameraSkill = conn.identity.GetComponent<SurvivorCameraSkill>();

            if (cameraSkill == null)
                continue;

            SurvivorState survivorState = conn.identity.GetComponent<SurvivorState>();

            // 다운 / 사망 / 감옥 상태는 안전하게 제외한다.
            if (survivorState != null)
            {
                if (survivorState.IsDead || survivorState.IsDowned || survivorState.IsImprisoned)
                    continue;
            }

            if (cameraSkill.IsRecordingKiller)
                count++;
        }

        return count;
    }

    // 촬영 중인 생존자 수에 따른 진행 속도 배율을 반환한다.
    private float GetKillerDetectMultiplier(int userCount)
    {
        switch (userCount)
        {
            case 1:
                return 1f;

            case 2:
                return 1.3f;

            case 3:
                return 1.6f;

            case 4:
                return 2f;

            default:
                return 0f;
        }
    }

    // 서버 계산값을 클라이언트 UI 표시용 SyncVar에 복사한다.
    private void SyncObjectiveState()
    {
        syncedObjectiveProgress = objectiveProgress;
        syncedKillerDetectUserCount = currentKillerDetectUserCount;
        syncedKillerDetectMultiplier = currentKillerDetectMultiplier;
    }

    // 통합 목표 게이지가 완료됐는지 검사하고 컴퓨터를 활성화한다.
    private void CheckUpload()
    {
        if (!NetworkServer.active)
            return;

        if (canUpload || isWaitingGateOpen || gateOpened)
            return;

        // 이제 업로드 조건은 통합 목표 게이지 100% 하나다.
        if (!IsObjectiveComplete)
            return;

        canUpload = true;

        // 목표 완료로 업로드 컴퓨터가 활성화되는 순간 모두에게 2D 알림음을 재생한다.
        ServerPlayUploadNoticeSound();

        Debug.Log("[GameManager] 통합 목표 완료. 업로드 컴퓨터 활성화.");

        for (int i = 0; i < uploadComputers.Length; i++)
        {
            if (uploadComputers[i] != null)
                uploadComputers[i].SetOpen(true);
        }
    }

    // 업로드 관련 중요 알림음을 모두에게 2D로 재생한다.
    // - 목표 완료 후 업로드 컴퓨터 활성화
    // - 업로드 완료 후 탈출문 대기 시작
    [Server]
    private void ServerPlayUploadNoticeSound()
    {
        if (NetworkAudioManager.Instance == null)
            return;

        if (uploadNoticeSoundKey == AudioKey.None)
            return;

        NetworkAudioManager.PlayAudioForEveryone(
            uploadNoticeSoundKey,
            AudioDimension.Sound2D,
            Vector3.zero
        );
    }

    // 업로드 중인 생존자 수만큼 공유 진행도를 증가시킨다.
    public void AddUpload(int userCount)
    {
        if (!NetworkServer.active)
            return;

        if (!canUpload)
            return;

        if (isWaitingGateOpen || gateOpened)
            return;

        if (userCount <= 0)
            return;

        uploadProgress += Time.deltaTime * userCount;

        if (uploadProgress >= uploadTime)
        {
            uploadProgress = uploadTime;
            SyncUploadProgress();
            FinishUpload();
            return;
        }

        SyncUploadProgress();
    }

    // 서버 progress를 모든 컴퓨터의 SyncVar UI progress로 전달한다.
    private void SyncUploadProgress()
    {
        float value = UploadProgress01;

        for (int i = 0; i < uploadComputers.Length; i++)
        {
            if (uploadComputers[i] != null)
                uploadComputers[i].SetProgress(value);
        }
    }

    // 업로드 완료 시 컴퓨터를 멈추고 탈출문 개방 대기를 시작한다.
    private void FinishUpload()
    {
        if (!NetworkServer.active)
            return;

        if (isWaitingGateOpen || gateOpened)
            return;

        canUpload = false;
        isWaitingGateOpen = true;
        gateRemainTime = gateOpenDelay;

        // 업로드 완료 순간에도 업로드 컴퓨터 활성화 때와 같은 2D 알림음을 재생한다.
        ServerPlayUploadNoticeSound();

        Debug.Log("[GameManager] 업로드 완료. 탈출문 개방 대기 시작.");

        for (int i = 0; i < uploadComputers.Length; i++)
        {
            if (uploadComputers[i] != null)
                uploadComputers[i].StopAllUsers();
        }

        SyncGateTimer();
    }

    // 업로드 완료 후 탈출문이 열리기까지 남은 시간을 감소시킨다.
    private void TickGateOpenTimer()
    {
        if (!isWaitingGateOpen)
            return;

        if (gateOpened)
            return;

        gateRemainTime -= Time.deltaTime;

        if (gateRemainTime <= 0f)
        {
            gateRemainTime = 0f;
            SyncGateTimer();
            OpenGates();
            return;
        }

        SyncGateTimer();
    }

    // 문 개방 대기 시간을 모든 업로드 컴퓨터의 SyncVar로 전달한다.
    private void SyncGateTimer()
    {
        float remain01 = GateRemain01;

        for (int i = 0; i < uploadComputers.Length; i++)
        {
            if (uploadComputers[i] != null)
                uploadComputers[i].SetGateTimer(isWaitingGateOpen, gateRemainTime, gateOpenDelay, remain01);
        }
    }

    // 대기 시간이 끝나면 모든 탈출문을 연다.
    private void OpenGates()
    {
        if (!NetworkServer.active)
            return;

        if (gateOpened)
            return;

        gateOpened = true;
        isWaitingGateOpen = false;

        Debug.Log("[GameManager] 대기 시간 종료. 탈출문 자동 개방.");

        for (int i = 0; i < uploadComputers.Length; i++)
        {
            if (uploadComputers[i] != null)
                uploadComputers[i].SetGateOpened();
        }

        for (int i = 0; i < escapeGates.Length; i++)
        {
            if (escapeGates[i] != null)
                escapeGates[i].Open();
        }
    }

    // needEvidenceCount가 0이면 등록된 EvidenceZone 개수를 목표로 사용한다.
    private int GetNeedCount()
    {
        if (needEvidenceCount > 0)
            return needEvidenceCount;

        return zones.Count;
    }

    // 생존자 결과창 위치를 생존자 인덱스에 맞게 반환한다.
    [Server]
    public Transform GetSurvivorResultPoint(SurvivorMove targetSurvivor)
    {
        if (targetSurvivor == null)
            return null;

        if (targetSurvivor.connectionToClient == null)
            return null;

        if (survivorResultPoints == null || survivorResultPoints.Length == 0)
            return null;

        if (CustomNetworkManager.Instance == null)
            return null;

        if (!CustomNetworkManager.Instance.TryGetSurvivorIndex(
                targetSurvivor.connectionToClient,
                out int survivorIndex))
        {
            return null;
        }

        if (survivorIndex < 0 || survivorIndex >= survivorResultPoints.Length)
            return null;

        return survivorResultPoints[survivorIndex];
    }

    // 살인마 결과창 위치를 반환한다.
    [Server]
    public Transform GetKillerResultPoint()
    {
        return killerResultPoint;
    }
}