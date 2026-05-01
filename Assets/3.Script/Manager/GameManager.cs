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

    [Header("살인마 탐지 목표")]
    [SerializeField] private bool requireKillerDetectionForUpload = true;

    // 살인마를 총 몇 초 탐지해야 업로드 컴퓨터가 열리는지
    // 120초 = 2분
    [SerializeField] private float needKillerDetectTime = 120f;

    // 서버에서 실제로 계산하는 누적 탐지 시간
    [SerializeField] private float killerDetectProgress;

    // 현재 살인마를 탐지 중인 생존자 수
    [SerializeField] private int currentKillerDetectUserCount;

    // 현재 적용 중인 탐지 배율
    [SerializeField] private float currentKillerDetectMultiplier;

    // 클라이언트 UI 표시용 동기화 값
    [SyncVar] private float syncedKillerDetectProgress;
    [SyncVar] private int syncedKillerDetectUserCount;
    [SyncVar] private float syncedKillerDetectMultiplier;

    [Header("업로드")]
    [SerializeField] private float uploadTime = 60f;
    [SerializeField] private UploadComputer[] uploadComputers;

    [Header("탈출문")]
    [SerializeField] private EscapeGate[] escapeGates;

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

    // 탈출문이 열렸는지 여부다.
    private bool gateOpened;

    public bool SurvivorInputEnabled => survivorInputEnabled;
    public int FoundEvidenceCount => foundEvidenceCount;
    public int NeedEvidenceCount => GetNeedCount();
    public bool CanUpload => canUpload;
    public bool GateOpened => gateOpened;

    public float NeedKillerDetectTime => needKillerDetectTime;

    public float KillerDetectProgress
    {
        get
        {
            // 서버에서는 실제 계산값 사용
            if (NetworkServer.active)
                return killerDetectProgress;

            // 클라이언트에서는 SyncVar로 받은 값 사용
            return syncedKillerDetectProgress;
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

    public bool IsKillerDetectComplete
    {
        get
        {
            if (!requireKillerDetectionForUpload)
                return true;

            if (needKillerDetectTime <= 0f)
                return true;

            return KillerDetectProgress >= needKillerDetectTime;
        }
    }

    public float KillerDetectProgress01
    {
        get
        {
            if (!requireKillerDetectionForUpload)
                return 1f;

            if (needKillerDetectTime <= 0f)
                return 1f;

            return Mathf.Clamp01(KillerDetectProgress / needKillerDetectTime);
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

        // NetworkIdentity가 붙는 구조이므로,
        // 씬 오브젝트로 두고 호스트 / 클라이언트가 같은 씬을 사용하게 한다.
    }

    private void Start()
    {
        // 서버에서만 시작 입력 상태를 정한다.
        if (NetworkServer.active)
            SetAllSurvivorInput(false);
    }

    private void Update()
    {
        // 살인마 탐지 목표 계산은 서버에서만 한다.
        if (!NetworkServer.active)
            return;

        UpdateKillerDetectGoal();
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

        Debug.Log($"[GameManager] 진짜 증거 발견: {foundEvidenceCount}/{GetNeedCount()}");

        CheckUpload();
    }

    // 서버에서 살인마 탐지 목표를 갱신한다.
    private void UpdateKillerDetectGoal()
    {
        // 탐지 목표를 사용하지 않으면 계산하지 않는다.
        if (!requireKillerDetectionForUpload)
        {
            SyncKillerDetectState();
            return;
        }

        // 이미 업로드가 열렸거나 문이 열렸으면 더 이상 탐지 목표를 올리지 않는다.
        if (canUpload || gateOpened)
        {
            SyncKillerDetectState();
            return;
        }

        // 이미 목표를 달성했으면 업로드 조건만 다시 확인한다.
        if (killerDetectProgress >= needKillerDetectTime)
        {
            killerDetectProgress = needKillerDetectTime;
            currentKillerDetectUserCount = 0;
            currentKillerDetectMultiplier = 0f;

            SyncKillerDetectState();
            CheckUpload();
            return;
        }

        int detectingCount = GetRecordingKillerSurvivorCount();

        currentKillerDetectUserCount = detectingCount;
        currentKillerDetectMultiplier = GetKillerDetectMultiplier(detectingCount);

        // 아무도 살인마를 탐지 중이 아니면 진행도 증가 없음
        if (currentKillerDetectMultiplier <= 0f)
        {
            SyncKillerDetectState();
            return;
        }

        // 탐지 중인 생존자 수에 따른 배율로 공용 게이지 증가
        killerDetectProgress += Time.deltaTime * currentKillerDetectMultiplier;
        killerDetectProgress = Mathf.Clamp(killerDetectProgress, 0f, needKillerDetectTime);

        SyncKillerDetectState();

        // 탐지 목표를 막 달성했다면 업로드 가능 조건을 다시 검사한다.
        if (killerDetectProgress >= needKillerDetectTime)
        {
            Debug.Log("[GameManager] 살인마 탐지 목표 완료.");
            CheckUpload();
        }
    }

    // 현재 카메라로 살인마를 탐지 중인 생존자 수를 계산한다.
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

            // 다운 / 사망 / 감옥 상태는 카메라 스킬 자체가 꺼지는 구조지만,
            // 안전하게 한 번 더 제외한다.
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

    // 탐지 중인 생존자 수에 따른 진행 속도 배율
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
    private void SyncKillerDetectState()
    {
        syncedKillerDetectProgress = killerDetectProgress;
        syncedKillerDetectUserCount = currentKillerDetectUserCount;
        syncedKillerDetectMultiplier = currentKillerDetectMultiplier;
    }

    // 모든 진짜 증거 + 살인마 탐지 목표를 만족했는지 검사하고 컴퓨터를 활성화한다.
    private void CheckUpload()
    {
        if (!NetworkServer.active)
            return;

        if (canUpload || gateOpened)
            return;

        int needCount = GetNeedCount();

        if (needCount <= 0)
            return;

        // 조건 1. 진짜 증거를 모두 찾아야 한다.
        if (foundEvidenceCount < needCount)
            return;

        // 조건 2. 살인마를 총 2분 탐지해야 한다.
        if (!IsKillerDetectComplete)
        {
            Debug.Log(
                $"[GameManager] 증거는 완료됐지만 살인마 탐지 목표 미완료: " +
                $"{KillerDetectProgress:F1}/{needKillerDetectTime:F1}"
            );

            return;
        }

        canUpload = true;

        Debug.Log("[GameManager] 증거 + 살인마 탐지 목표 완료. 업로드 컴퓨터 활성화.");

        for (int i = 0; i < uploadComputers.Length; i++)
        {
            if (uploadComputers[i] != null)
                uploadComputers[i].SetOpen(true);
        }
    }

    // 업로드 중인 생존자 수만큼 공유 진행도를 증가시킨다.
    public void AddUpload(int userCount)
    {
        if (!NetworkServer.active)
            return;

        if (!canUpload)
            return;

        if (gateOpened)
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

    // 업로드 완료 시 모든 컴퓨터를 멈추고 탈출문을 연다.
    private void FinishUpload()
    {
        if (!NetworkServer.active)
            return;

        if (gateOpened)
            return;

        gateOpened = true;
        canUpload = false;

        Debug.Log("[GameManager] 업로드 완료. 탈출문 자동 개방.");

        for (int i = 0; i < uploadComputers.Length; i++)
        {
            if (uploadComputers[i] != null)
                uploadComputers[i].StopAllUsers();
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
}