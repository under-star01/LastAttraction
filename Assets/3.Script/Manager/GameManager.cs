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
    [SyncVar]
    private int foundEvidenceCount;

    // 업로드 컴퓨터 사용 가능 여부다.
    [SyncVar]
    private bool canUpload;

    // 컴퓨터 2개가 함께 사용하는 공유 업로드 진행도다.
    [SyncVar]
    private float uploadProgress;

    // 탈출문이 열렸는지 여부다.
    [SyncVar]
    private bool gateOpened;

    public bool SurvivorInputEnabled => survivorInputEnabled;
    public int FoundEvidenceCount => foundEvidenceCount;
    public int NeedEvidenceCount => GetNeedCount();
    public bool CanUpload => canUpload;
    public bool GateOpened => gateOpened;

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

        // 씬 이동 후에도 유지하려면 사용한다.
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // 서버에서만 시작 입력 상태를 정한다.
        if (NetworkServer.active)
            SetAllSurvivorInput(false);
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
    [Server]
    public void AddZone(EvidenceZone zone)
    {
        if (zone == null)
            return;

        zones.Add(zone);

        Debug.Log($"[GameManager] EvidenceZone 등록: {zone.name} / 총 {zones.Count}개");
    }

    // 진짜 증거 발견 시 EvidenceZone에서 호출한다.
    [Server]
    public void AddEvidence(EvidenceZone zone)
    {
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

    // 모든 진짜 증거를 모았는지 검사하고 컴퓨터를 활성화한다.
    [Server]
    private void CheckUpload()
    {
        if (canUpload || gateOpened)
            return;

        int needCount = GetNeedCount();

        if (needCount <= 0)
            return;

        if (foundEvidenceCount < needCount)
            return;

        canUpload = true;

        Debug.Log("[GameManager] 모든 진짜 증거 발견 완료. 업로드 컴퓨터 활성화.");

        for (int i = 0; i < uploadComputers.Length; i++)
        {
            if (uploadComputers[i] != null)
                uploadComputers[i].SetOpen(true);
        }
    }

    // 업로드 중인 생존자 수만큼 공유 진행도를 증가시킨다.
    [Server]
    public void AddUpload(int userCount)
    {
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
            FinishUpload();
        }
    }

    // 업로드 완료 시 모든 컴퓨터를 멈추고 탈출문을 연다.
    [Server]
    private void FinishUpload()
    {
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