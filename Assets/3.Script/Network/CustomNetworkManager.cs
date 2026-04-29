using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using kcp2k;

// 어떤 역할로 입장할지
public enum JoinRole
{
    None,
    Killer,
    Survivor
}

// 클라 -> 서버 : 서버 입장 최종 요청 메세지
public struct JoinRequestMessage : NetworkMessage
{
    public int role;
}

// 서버 -> 클라 : 서버 입장 거절 메세지 
public struct JoinDeniedMessage : NetworkMessage
{
    public string reason;
}

// 서버 -> 클라 : 서버 입장 승인 메세지
public struct JoinAcceptedMessage : NetworkMessage
{
    public int role;
    public ushort port;
}

// 클라 -> 서버 : 서버 상태 요청 메세지 (요청만)
public struct RoomProbeRequestMessage : NetworkMessage { }

// 서버 -> 클라 : 현재 서버 상태 반환 메세지
public struct RoomProbeResponseMessage : NetworkMessage
{
    public ushort port;
    public int survivorCount;
    public bool hasKiller;
    public bool isFull;
}

// 생존자 -> 서버 : Ready 상태 변경 요청 메세지
public struct SurvivorReadyRequestMessage : NetworkMessage
{
    public bool isReady;
}

// 살인마 -> 서버 : 게임 시작 요청 메세지
public struct StartGameRequestMessage : NetworkMessage { }

// 서버 -> 클라 : 로비 상태 동기화 메세지
public struct LobbyStateMessage : NetworkMessage
{
    public int survivorCount;
    public int readySurvivorCount;
    public bool canStart;
}

public class CustomNetworkManager : NetworkManager
{
    public static CustomNetworkManager Instance { get; private set; }

    [Header("Port Settings")]
    [SerializeField] private List<ushort> serverPorts = new() { 7777, 7778, 7779 };

    [Header("Role Prefabs")]
    [SerializeField] private GameObject killerPrefab;
    [SerializeField] private List<GameObject> survivorPrefabs = new();

    [Header("Spawn Points")]
    [SerializeField] private Transform killerSpawnPoint;
    [SerializeField] private List<Transform> survivorSpawnPoints = new();

    [Header("Match Settings")]
    [SerializeField] private int maxRoomPlayers = 5;

    [Header("Scene Settings")]
    [SerializeField] private string inGameSceneName = "InGame";

    private KcpTransport kcpTransport;

    private JoinRole localJoinRole = JoinRole.None;

    // 서버 기준 접속 역할 관리
    private readonly Dictionary<int, JoinRole> joinedRoles = new();

    // 서버 기준 생존자 프리팹 인덱스 관리
    private readonly Dictionary<int, int> survivorPrefabIndexByConnection = new();

    // 서버 기준 생존자 Ready 상태 관리
    private readonly Dictionary<int, bool> survivorReadyByConnection = new();

    // 클라이언트가 탐색한 방 정보
    private readonly List<RoomProbeResponseMessage> probedRooms = new();

    private int currentPortIndex = -1;       // 현재 탐색중인 포트 Index
    private bool isSearchingServer;          // 현재 서버 탐색 여부
    private bool isLeavingManually;          // 수동으로 나가는 중인지 여부
    private bool isJoiningFinalRoom;         // 최종 입장 시도 중인지 여부
    private bool joinApproved;               // 최종 입장 승인 여부
    private ushort selectedPort;             // 최종 선택된 포트

    private Coroutine connectRoutine;

    public bool HasKiller
    {
        get
        {
            foreach (var role in joinedRoles)
            {
                if (role.Value == JoinRole.Killer)
                    return true;
            }

            return false;
        }
    }

    public bool IsRoomFull => numPlayers >= maxRoomPlayers;

    // 살인마 입장 가능 상태 여부
    public bool CanJoinAsKiller => !HasKiller && !IsRoomFull;

    // 생존자 입장 가능 상태 여부
    public bool CanJoinAsSurvivor => HasKiller && !IsRoomFull;

    // 상태 확인용 프로퍼티
    public bool IsSearchingServer => isSearchingServer;
    public bool IsConnectedToServer => NetworkClient.isConnected;
    public JoinRole CurrentLocalJoinRole => localJoinRole;

    public override void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        base.Awake();

        kcpTransport = transport as KcpTransport;
        if (kcpTransport == null)
        {
            Debug.LogError("[CustomNetworkManager] KcpTransport를 찾지 못했습니다.");
            return;
        }

        kcpTransport.Port = GetPortFromArgs();
        maxConnections = maxRoomPlayers;
    }

    private void Start()
    {
        // 서버 컴퓨터에서만 실행
        if (!Application.isBatchMode) return;

        StartServer();
    }

    #region Client Connect

    // 살인마로 접속 메소드
    public void ConnectAsKiller()
    {
        BeginRoleSearch(JoinRole.Killer);
    }

    // 생존자로 접속 메소드
    public void ConnectAsSurvivor()
    {
        BeginRoleSearch(JoinRole.Survivor);
    }

    // 역할 선택 초기화 메소드
    public void BackToRoleSelect()
    {
        isLeavingManually = true;
        isSearchingServer = false;
        joinApproved = false;
        isJoiningFinalRoom = false;
        selectedPort = 0;

        UIManager.Instance?.ShowLoading(false);

        if (connectRoutine != null)
        {
            StopCoroutine(connectRoutine);
            connectRoutine = null;
        }

        if (NetworkClient.active || NetworkClient.isConnected)
        {
            StopClient();
            return;
        }

        ResetClientSearchState();
    }

    private void BeginRoleSearch(JoinRole role)
    {
        // 탐색 가능 여부 확인
        if (role != JoinRole.Killer && role != JoinRole.Survivor)
        {
            Debug.LogWarning("[CustomNetworkManager] 유효하지 않은 역할입니다.");
            return;
        }

        if (NetworkClient.active || isSearchingServer)
        {
            Debug.LogWarning("[CustomNetworkManager] 이미 접속 중이거나 서버 탐색 중입니다.");
            return;
        }

        if (serverPorts == null || serverPorts.Count == 0)
        {
            Debug.LogError("[CustomNetworkManager] serverPorts가 비어 있습니다.");
            return;
        }

        // 탐색 상태 초기화
        localJoinRole = role;
        currentPortIndex = -1;
        isSearchingServer = true;
        isLeavingManually = false;
        isJoiningFinalRoom = false;
        joinApproved = false;
        selectedPort = 0;

        UIManager.Instance?.ShowLoading(true);
        probedRooms.Clear();

        // 포트 탐색 시작
        ProbeNextPort();
    }

    private void ProbeNextPort()
    {
        currentPortIndex++;

        if (currentPortIndex >= serverPorts.Count)
        {
            // 최종 접속 방 선택
            SelectBestRoomAndJoin();
            return;
        }

        // 목표 포트에 클라이언트 접속
        StartClientDelayed(serverPorts[currentPortIndex]);
    }

    // 최종 접속 포트 선택 메소드
    private void SelectBestRoomAndJoin()
    {
        selectedPort = FindBestPort();

        if (selectedPort == 0)
        {
            Debug.LogWarning($"[CustomNetworkManager] {localJoinRole} 입장 가능한 방이 없습니다.");
            UIManager.Instance?.ShowLoading(false);
            ResetClientSearchState();
            return;
        }

        // 최종 입장 단계로 전환
        isJoiningFinalRoom = true;
        StartClientDelayed(selectedPort);
    }

    // 목표 포트 접속 메소드
    private void StartClientDelayed(ushort targetPort)
    {
        if (connectRoutine != null)
        {
            StopCoroutine(connectRoutine);
            connectRoutine = null;
        }

        connectRoutine = StartCoroutine(StartClientNextFrame(targetPort));
    }

    private IEnumerator StartClientNextFrame(ushort targetPort)
    {
        yield return new WaitForSeconds(0.1f);

        if (isLeavingManually)
        {
            connectRoutine = null;
            yield break;
        }

        if (kcpTransport == null)
        {
            Debug.LogError("[CustomNetworkManager] KcpTransport를 찾지 못했습니다.");
            connectRoutine = null;
            yield break;
        }

        if (NetworkClient.active || NetworkClient.isConnected)
        {
            connectRoutine = null;
            yield break;
        }

        kcpTransport.Port = targetPort;
        StartClient();

        connectRoutine = null;
    }

    // 최적 포트 반환 메소드
    private ushort FindBestPort()
    {
        // 살인마 접속의 경우
        if (localJoinRole == JoinRole.Killer)
        {
            // 우선순위 1 : 살인마x + 생존자o
            foreach (var room in probedRooms)
            {
                if (!room.isFull && !room.hasKiller && room.survivorCount > 0)
                    return room.port;
            }

            // 우선순위 2 : 살인마x + 생존자x
            foreach (var room in probedRooms)
            {
                if (!room.isFull && !room.hasKiller && room.survivorCount == 0)
                    return room.port;
            }

            return 0;
        }

        // 생존자 접속의 경우
        if (localJoinRole == JoinRole.Survivor)
        {
            // 살인마o + 최대 인원수x
            foreach (var room in probedRooms)
            {
                if (room.hasKiller && !room.isFull)
                    return room.port;
            }

            return 0;
        }

        return 0;
    }

    // 탐색 상태 초기화 메소드
    private void ResetClientSearchState()
    {
        localJoinRole = JoinRole.None;
        currentPortIndex = -1;
        isSearchingServer = false;
        joinApproved = false;
        isLeavingManually = false;
        isJoiningFinalRoom = false;
        selectedPort = 0;
        probedRooms.Clear();

        if (connectRoutine != null)
        {
            StopCoroutine(connectRoutine);
            connectRoutine = null;
        }
    }

    #endregion

    #region Lobby Button Request

    // Ready UI 버튼에서 호출
    public void RequestSurvivorReady(bool isReady)
    {
        if (!NetworkClient.isConnected)
        {
            Debug.LogWarning("[CustomNetworkManager] 서버에 연결되어 있지 않아 Ready 요청을 보낼 수 없습니다.");
            return;
        }

        if (localJoinRole != JoinRole.Survivor)
        {
            Debug.LogWarning("[CustomNetworkManager] Survivor가 아니므로 Ready 요청을 보낼 수 없습니다.");
            return;
        }

        NetworkClient.Send(new SurvivorReadyRequestMessage
        {
            isReady = isReady
        });
    }

    // Ready 버튼 OnClick에 바로 연결하기 편한 메소드
    public void OnClickReadyButton()
    {
        RequestSurvivorReady(true);
    }

    // Start UI 버튼에서 호출
    public void RequestStartGame()
    {
        Debug.Log(
            $"[CustomNetworkManager] Start 요청 시도 / " +
            $"isConnected: {NetworkClient.isConnected}, " +
            $"ready: {NetworkClient.ready}, " +
            $"localJoinRole: {localJoinRole}"
        );

        if (!NetworkClient.isConnected)
        {
            Debug.LogWarning("[CustomNetworkManager] 서버에 연결되어 있지 않아 Start 요청을 보낼 수 없습니다.");
            return;
        }

        if (localJoinRole != JoinRole.Killer)
        {
            Debug.LogWarning("[CustomNetworkManager] Killer가 아니므로 Start 요청을 보낼 수 없습니다.");
            return;
        }

        NetworkClient.Send(new StartGameRequestMessage());
    }

    #endregion

    #region Server Lifecycle

    public override void OnStartServer()
    {
        base.OnStartServer();

        // 각 메세지 처리할 메소드 등록
        NetworkServer.RegisterHandler<JoinRequestMessage>(OnReceiveJoinRequest, false);
        NetworkServer.RegisterHandler<RoomProbeRequestMessage>(OnReceiveRoomProbeRequest, false);

        // 로비 Ready / Start 메세지 등록
        NetworkServer.RegisterHandler<SurvivorReadyRequestMessage>(OnReceiveSurvivorReadyRequest, false);
        NetworkServer.RegisterHandler<StartGameRequestMessage>(OnReceiveStartGameRequest, false);
    }

    public override void OnStopServer()
    {
        joinedRoles.Clear();
        survivorPrefabIndexByConnection.Clear();
        survivorReadyByConnection.Clear();

        base.OnStopServer();
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        // 미리 인원 초과 상태시 차단
        if (IsRoomFull)
        {
            conn.Disconnect();
            return;
        }

        base.OnServerConnect(conn);
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        joinedRoles.Remove(conn.connectionId);
        survivorPrefabIndexByConnection.Remove(conn.connectionId);
        survivorReadyByConnection.Remove(conn.connectionId);

        base.OnServerDisconnect(conn);

        if (NetworkServer.active)
            BroadcastLobbyState();
    }

    #endregion

    #region Client Lifecycle

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 각 메세지 처리할 메소드 등록
        NetworkClient.RegisterHandler<JoinDeniedMessage>(OnJoinDenied, false);
        NetworkClient.RegisterHandler<JoinAcceptedMessage>(OnJoinAccepted, false);
        NetworkClient.RegisterHandler<RoomProbeResponseMessage>(OnRoomProbeResponse, false);

        // 로비 상태 수신 등록
        NetworkClient.RegisterHandler<LobbyStateMessage>(OnLobbyStateMessage, false);
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();

        if (localJoinRole == JoinRole.None)
        {
            StopClient();
            return;
        }

        // 최종 입장 단계 -> 최종 입장 요청
        if (isJoiningFinalRoom)
        {
            NetworkClient.Send(new JoinRequestMessage
            {
                role = (int)localJoinRole
            });
        }
        // 탐색 단계 -> 방 상태 확인 요청
        else
        {
            NetworkClient.Send(new RoomProbeRequestMessage());
        }
    }

    public override void OnClientDisconnect()
    {
        bool wasProbing = isSearchingServer && !joinApproved && !isLeavingManually && !isJoiningFinalRoom;
        bool finalJoinFailed = isSearchingServer && !joinApproved && !isLeavingManually && isJoiningFinalRoom;

        base.OnClientDisconnect();

        if (wasProbing)
        {
            ProbeNextPort();
            return;
        }

        if (finalJoinFailed)
        {
            Debug.LogWarning("[CustomNetworkManager] 최종 방 입장에 실패했습니다.");
        }

        if (isLeavingManually)
        {
            ResetClientSearchState();
            return;
        }

        if (!joinApproved)
        {
            UIManager.Instance?.ShowLoading(false);
            ResetClientSearchState();
        }
    }

    private void OnJoinDenied(JoinDeniedMessage msg)
    {
        Debug.LogWarning($"[CustomNetworkManager] 입장 거부: {msg.reason}");

        if (NetworkClient.active || NetworkClient.isConnected)
        {
            StopClient();
        }
        else
        {
            UIManager.Instance?.ShowLoading(false);
            ResetClientSearchState();
        }
    }

    private void OnJoinAccepted(JoinAcceptedMessage msg)
    {
        joinApproved = true;
        isSearchingServer = false;
        isJoiningFinalRoom = false;
        localJoinRole = (JoinRole)msg.role;

        UIManager.Instance?.ShowLoading(false);

        // 역할별 로비 UI 표시
        if (localJoinRole == JoinRole.Killer)
        {
            UIManager.Instance?.ShowKillerLobbyUI();
            UIManager.Instance?.SetStartButtonInteractable(false);
        }
        else if (localJoinRole == JoinRole.Survivor)
        {
            UIManager.Instance?.ShowSurvivorLobbyUI();
        }

        Debug.Log($"[CustomNetworkManager] 입장 완료 - Role: {localJoinRole}, Port: {msg.port}");
    }

    private void OnRoomProbeResponse(RoomProbeResponseMessage msg)
    {
        probedRooms.Add(msg);

        if (NetworkClient.active || NetworkClient.isConnected)
            StopClient();
    }

    private void OnLobbyStateMessage(LobbyStateMessage msg)
    {
        UIManager.Instance?.SetLobbyReadyCount(msg.readySurvivorCount, msg.survivorCount);

        if (localJoinRole == JoinRole.Killer)
        {
            UIManager.Instance?.SetStartButtonInteractable(msg.canStart);
        }
    }

    #endregion

    #region Server Request Handlers

    // 탐색 요청 수신 시 적용 메소드
    private void OnReceiveRoomProbeRequest(NetworkConnectionToClient conn, RoomProbeRequestMessage msg)
    {
        conn.Send(new RoomProbeResponseMessage
        {
            port = kcpTransport.Port,
            survivorCount = GetCurrentSurvivorCount(),
            hasKiller = HasKiller,
            isFull = IsRoomFull
        });

        StartCoroutine(DisconnectNextFrame(conn));
    }

    // 최종 접속 요청 수신 시 적용 메소드
    private void OnReceiveJoinRequest(NetworkConnectionToClient conn, JoinRequestMessage msg)
    {
        JoinRole requestedRole = (JoinRole)msg.role;

        if (conn.identity != null)
        {
            conn.Send(new JoinDeniedMessage { reason = "이미 플레이어가 생성된 연결입니다." });
            StartCoroutine(DisconnectNextFrame(conn));
            return;
        }

        if (!CanAcceptRole(requestedRole, out string denyReason))
        {
            conn.Send(new JoinDeniedMessage { reason = denyReason });
            StartCoroutine(DisconnectNextFrame(conn));
            return;
        }

        if (!TryCreatePlayer(conn, requestedRole, out string createFailReason))
        {
            conn.Send(new JoinDeniedMessage { reason = createFailReason });
            StartCoroutine(DisconnectNextFrame(conn));
            return;
        }

        // 역할 등록
        joinedRoles[conn.connectionId] = requestedRole;

        // 생존자는 입장 직후 Ready false로 초기화
        if (requestedRole == JoinRole.Survivor)
            survivorReadyByConnection[conn.connectionId] = false;

        conn.Send(new JoinAcceptedMessage
        {
            role = (int)requestedRole,
            port = kcpTransport.Port
        });

        BroadcastLobbyState();
    }

    // 생존자 Ready 요청 수신
    private void OnReceiveSurvivorReadyRequest(NetworkConnectionToClient conn, SurvivorReadyRequestMessage msg)
    {
        if (!joinedRoles.TryGetValue(conn.connectionId, out JoinRole role))
        {
            Debug.LogWarning("[CustomNetworkManager] 역할이 등록되지 않은 연결에서 Ready 요청이 들어왔습니다.");
            return;
        }

        if (role != JoinRole.Survivor)
        {
            Debug.LogWarning("[CustomNetworkManager] Survivor가 아닌 연결에서 Ready 요청이 들어왔습니다.");
            return;
        }

        survivorReadyByConnection[conn.connectionId] = msg.isReady;

        Debug.Log($"[CustomNetworkManager] Survivor Ready 변경 - Conn: {conn.connectionId}, Ready: {msg.isReady}");

        BroadcastLobbyState();
    }

    // 살인마 Start 요청 수신
    private void OnReceiveStartGameRequest(NetworkConnectionToClient conn, StartGameRequestMessage msg)
    {
        Debug.Log($"[CustomNetworkManager] 서버 Start 요청 수신 / Conn: {conn.connectionId}");

        if (!joinedRoles.TryGetValue(conn.connectionId, out JoinRole role))
        {
            Debug.LogWarning("[CustomNetworkManager] 역할이 등록되지 않은 연결에서 Start 요청이 들어왔습니다.");
            return;
        }

        if (role != JoinRole.Killer)
        {
            Debug.LogWarning("[CustomNetworkManager] Killer가 아닌 연결에서 Start 요청이 들어왔습니다.");
            return;
        }

        bool canStart = CanStartGame();

        Debug.Log(
            $"[CustomNetworkManager] 시작 조건 확인 / " +
            $"CanStart: {canStart}, " +
            $"HasKiller: {HasKiller}, " +
            $"Survivor: {GetReadySurvivorCount()}/{GetCurrentSurvivorCount()}"
        );

        if (!canStart)
        {
            Debug.LogWarning("[CustomNetworkManager] 게임 시작 조건을 만족하지 못했습니다.");
            BroadcastLobbyState();
            return;
        }


        Debug.Log("[CustomNetworkManager] 게임 시작 조건 만족. 씬 이동 실행");
        MoveToGameScene();
    }

    #endregion

    #region Lobby State

    private void BroadcastLobbyState()
    {
        if (!NetworkServer.active)
            return;

        LobbyStateMessage msg = new LobbyStateMessage
        {
            survivorCount = GetCurrentSurvivorCount(),
            readySurvivorCount = GetReadySurvivorCount(),
            canStart = CanStartGame()
        };

        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn != null && conn.isReady)
                conn.Send(msg);
        }
    }

    private bool CanStartGame()
    {
        return HasKiller && AreAllSurvivorsReady();
    }

    private bool AreAllSurvivorsReady()
    {
        int survivorCount = 0;
        int readyCount = 0;

        foreach (var pair in joinedRoles)
        {
            if (pair.Value != JoinRole.Survivor)
                continue;

            survivorCount++;

            if (survivorReadyByConnection.TryGetValue(pair.Key, out bool isReady) && isReady)
                readyCount++;
        }

        // 현재 접속한 생존자가 1명 이상이고, 접속한 생존자 전원이 Ready면 true
        return survivorCount > 0 && survivorCount == readyCount;
    }

    private int GetReadySurvivorCount()
    {
        int count = 0;

        foreach (var pair in joinedRoles)
        {
            if (pair.Value != JoinRole.Survivor)
                continue;

            if (survivorReadyByConnection.TryGetValue(pair.Key, out bool isReady) && isReady)
                count++;
        }

        return count;
    }

    #endregion

    #region Role / Spawn

    // 입장 가능 여부 반환 메소드
    private bool CanAcceptRole(JoinRole role, out string reason)
    {
        reason = string.Empty;

        if (role != JoinRole.Killer && role != JoinRole.Survivor)
        {
            reason = "유효하지 않은 역할 요청입니다.";
            return false;
        }

        if (IsRoomFull)
        {
            reason = "방이 가득 찼습니다.";
            return false;
        }

        if (role == JoinRole.Killer && !CanJoinAsKiller)
        {
            reason = "이미 Killer가 존재하는 방입니다.";
            return false;
        }

        if (role == JoinRole.Survivor && !CanJoinAsSurvivor)
        {
            reason = "아직 Killer가 없는 방에는 Survivor가 입장할 수 없습니다.";
            return false;
        }

        return true;
    }

    // 실제 플레이어 생성 시도 메소드
    private bool TryCreatePlayer(NetworkConnectionToClient conn, JoinRole role, out string reason)
    {
        reason = string.Empty;

        GameObject prefabToSpawn = null;
        Transform spawnPoint = null;

        int survivorIndex = -1;

        switch (role)
        {
            case JoinRole.Killer:
                prefabToSpawn = killerPrefab;
                spawnPoint = killerSpawnPoint;
                break;

            case JoinRole.Survivor:
                survivorIndex = GetAvailableSurvivorPrefabIndex();

                if (survivorIndex < 0)
                {
                    reason = "사용 가능한 Survivor 프리팹이 없습니다.";
                    return false;
                }

                prefabToSpawn = GetSurvivorPrefab(survivorIndex);
                spawnPoint = GetSurvivorSpawnPoint(survivorIndex);
                break;
        }

        if (prefabToSpawn == null)
        {
            reason = $"{role} 프리팹이 설정되지 않았습니다.";
            return false;
        }

        if (spawnPoint == null)
        {
            reason = $"{role} 스폰 포인트가 설정되지 않았습니다.";
            return false;
        }

        GameObject playerObj = Instantiate(prefabToSpawn, spawnPoint.position, spawnPoint.rotation);
        NetworkServer.AddPlayerForConnection(conn, playerObj);

        if (role == JoinRole.Survivor)
            survivorPrefabIndexByConnection[conn.connectionId] = survivorIndex;

        return true;
    }

    private GameObject GetSurvivorPrefab(int survivorIndex)
    {
        if (survivorPrefabs == null || survivorPrefabs.Count == 0)
            return null;

        if (survivorIndex < 0 || survivorIndex >= survivorPrefabs.Count)
            return null;

        return survivorPrefabs[survivorIndex];
    }

    private int GetAvailableSurvivorPrefabIndex()
    {
        if (survivorPrefabs == null || survivorPrefabs.Count == 0)
            return -1;

        for (int i = 0; i < survivorPrefabs.Count; i++)
        {
            if (!IsSurvivorPrefabIndexUsed(i))
                return i;
        }

        return -1;
    }

    private bool IsSurvivorPrefabIndexUsed(int index)
    {
        foreach (var pair in survivorPrefabIndexByConnection)
        {
            if (pair.Value == index)
                return true;
        }

        return false;
    }

    private Transform GetSurvivorSpawnPoint(int survivorIndex)
    {
        if (survivorSpawnPoints == null || survivorSpawnPoints.Count == 0)
            return null;

        if (survivorIndex < 0 || survivorIndex >= survivorSpawnPoints.Count)
            return null;

        return survivorSpawnPoints[survivorIndex];
    }

    #endregion

    #region Utils

    private IEnumerator DisconnectNextFrame(NetworkConnectionToClient conn)
    {
        yield return null;

        if (conn != null)
            conn.Disconnect();
    }

    private ushort GetPortFromArgs()
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "-port")
                continue;

            if (ushort.TryParse(args[i + 1], out ushort parsedPort))
                return parsedPort;
        }

        if (serverPorts == null || serverPorts.Count == 0)
            return 7777;

        return serverPorts[0];
    }

    private int GetCurrentSurvivorCount()
    {
        int count = 0;

        foreach (var pair in joinedRoles)
        {
            if (pair.Value == JoinRole.Survivor)
                count++;
        }

        return count;
    }

    public void MoveToGameScene()
    {
        Debug.Log(
            $"[CustomNetworkManager] MoveToGameScene 호출 / " +
            $"NetworkServer.active: {NetworkServer.active}, " +
            $"inGameSceneName: {inGameSceneName}"
        );

        if (!NetworkServer.active)
        {
            Debug.LogWarning("[CustomNetworkManager] 서버가 활성화되어 있지 않아 InGame 씬으로 이동할 수 없습니다.");
            return;
        }

        if (string.IsNullOrWhiteSpace(inGameSceneName))
        {
            Debug.LogError("[CustomNetworkManager] InGame 씬 이름이 설정되지 않았습니다.");
            return;
        }

        Debug.Log($"[CustomNetworkManager] {inGameSceneName} 씬으로 이동");
        ServerChangeScene(inGameSceneName);
    }

    #endregion
}