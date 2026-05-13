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

    // 증거 목표 완료 또는 통합 목표 완료 후 남은 증거 상호작용을 한 번만 막기 위한 플래그다.
    private bool evidenceInteractionDisabled;

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
            if (NetworkServer.active)
                return objectiveProgress;

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
            if (NetworkServer.active)
                return currentKillerDetectUserCount;

            return syncedKillerDetectUserCount;
        }
    }

    public float CurrentKillerDetectMultiplier
    {
        get
        {
            if (NetworkServer.active)
                return currentKillerDetectMultiplier;

            return syncedKillerDetectMultiplier;
        }
    }

    public float UploadProgress01
    {
        get
        {
            if (uploadTime <= 0f)
                return 1f;

            return Mathf.Clamp01(uploadProgress / uploadTime);
        }
    }

    public float GateRemain01
    {
        get
        {
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

    public float KillerDetectProgress01
    {
        get
        {
            return ObjectiveProgress01;
        }
    }

    public bool IsKillerDetectComplete
    {
        get
        {
            return IsObjectiveComplete;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (NetworkServer.active)
            SetAllSurvivorInput(false);
    }

    private void Update()
    {
        if (!NetworkServer.active)
            return;

        UpdateObjectiveByCamera();
        TickGateOpenTimer();
    }

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

    public void StartGame()
    {
        SetAllSurvivorInput(true);
    }

    public void EnterLobby()
    {
        SetAllSurvivorInput(false);
    }

    public void AddZone(EvidenceZone zone)
    {
        if (!NetworkServer.active)
            return;

        if (zone == null)
            return;

        zones.Add(zone);

        Debug.Log($"[GameManager] EvidenceZone 등록: {zone.name} / 총 {zones.Count}개");
    }

    public void AddEvidence(EvidenceZone zone)
    {
        if (!NetworkServer.active)
            return;

        if (zone == null)
            return;

        if (foundZones.Contains(zone))
            return;

        foundZones.Add(zone);
        foundEvidenceCount = foundZones.Count;

        AddObjective(evidenceObjectiveAdd);

        Debug.Log(
            $"[GameManager] 진짜 증거 발견: {foundEvidenceCount}/{GetNeedCount()} / " +
            $"목표 게이지: {ObjectiveProgress01 * 100f:F0}%"
        );

        // 필요한 증거 개수를 모두 찾으면 남은 증거들은 보이지만 상호작용만 막는다.
        if (IsEvidenceComplete)
            DisableAllEvidenceInteractions();

        CheckUpload();
    }

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

    private void UpdateObjectiveByCamera()
    {
        if (canUpload || isWaitingGateOpen || gateOpened)
        {
            SyncObjectiveState();
            return;
        }

        if (IsObjectiveComplete)
        {
            objectiveProgress = objectiveMaxValue;
            currentKillerDetectUserCount = 0;
            currentKillerDetectMultiplier = 0f;

            SyncObjectiveState();
            CheckUpload();
            return;
        }

        if (needKillerDetectTime <= 0f)
        {
            SyncObjectiveState();
            return;
        }

        int detectingCount = GetRecordingKillerSurvivorCount();

        currentKillerDetectUserCount = detectingCount;
        currentKillerDetectMultiplier = GetKillerDetectMultiplier(detectingCount);

        if (currentKillerDetectMultiplier <= 0f)
        {
            SyncObjectiveState();
            return;
        }

        float addValue = Time.deltaTime * currentKillerDetectMultiplier / needKillerDetectTime;
        AddObjective(addValue);

        if (IsObjectiveComplete)
        {
            Debug.Log("[GameManager] 통합 목표 게이지 완료.");
            CheckUpload();
        }
    }

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

    private void SyncObjectiveState()
    {
        syncedObjectiveProgress = objectiveProgress;
        syncedKillerDetectUserCount = currentKillerDetectUserCount;
        syncedKillerDetectMultiplier = currentKillerDetectMultiplier;
    }

    private void CheckUpload()
    {
        if (!NetworkServer.active)
            return;

        if (canUpload || isWaitingGateOpen || gateOpened)
            return;

        if (!IsObjectiveComplete)
            return;

        canUpload = true;

        // 카메라 촬영으로 목표가 먼저 완료된 경우에도 남은 증거 상호작용을 막는다.
        DisableAllEvidenceInteractions();

        ServerPlayUploadNoticeSound();

        Debug.Log("[GameManager] 통합 목표 완료. 업로드 컴퓨터 활성화.");

        for (int i = 0; i < uploadComputers.Length; i++)
        {
            if (uploadComputers[i] != null)
                uploadComputers[i].SetOpen(true);
        }
    }

    [Server]
    private void DisableAllEvidenceInteractions()
    {
        if (evidenceInteractionDisabled)
            return;

        evidenceInteractionDisabled = true;

        EvidencePoint[] points = FindObjectsByType<EvidencePoint>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] != null)
                points[i].ServerDisableInteractionOnly();
        }

        Debug.Log($"[GameManager] 남은 증거 상호작용 비활성화 완료: {points.Length}개");
    }

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

    private void SyncUploadProgress()
    {
        float value = UploadProgress01;

        for (int i = 0; i < uploadComputers.Length; i++)
        {
            if (uploadComputers[i] != null)
                uploadComputers[i].SetProgress(value);
        }
    }

    private void FinishUpload()
    {
        if (!NetworkServer.active)
            return;

        if (isWaitingGateOpen || gateOpened)
            return;

        canUpload = false;
        isWaitingGateOpen = true;
        gateRemainTime = gateOpenDelay;

        ServerPlayUploadNoticeSound();

        Debug.Log("[GameManager] 업로드 완료. 탈출문 개방 대기 시작.");

        for (int i = 0; i < uploadComputers.Length; i++)
        {
            if (uploadComputers[i] != null)
                uploadComputers[i].StopAllUsers();
        }

        SyncGateTimer();
    }

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

    private void SyncGateTimer()
    {
        float remain01 = GateRemain01;

        for (int i = 0; i < uploadComputers.Length; i++)
        {
            if (uploadComputers[i] != null)
                uploadComputers[i].SetGateTimer(isWaitingGateOpen, gateRemainTime, gateOpenDelay, remain01);
        }
    }

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

    private int GetNeedCount()
    {
        if (needEvidenceCount > 0)
            return needEvidenceCount;

        return zones.Count;
    }

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

    [Server]
    public Transform GetKillerResultPoint()
    {
        return killerResultPoint;
    }
}