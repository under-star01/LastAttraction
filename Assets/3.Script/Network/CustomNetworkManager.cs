using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using kcp2k;
using UnityEngine.SceneManagement;

// 어떤 역할로 입장할지
public enum JoinRole
{
    None,
    Killer,
    Survivor
}

// 클라 -> 서버 : 서버 입장 최종 요청 메세지
// DB 로그인 후 GameSession에 저장된 유저 정보를 같이 보낸다.
public struct JoinRequestMessage : NetworkMessage
{
    public int role;

    public int accountId;
    public string loginId;
    public string nickname;
    public int exp;
    public int level;
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

// 클라 -> 서버 : 서버 상태 요청 메세지
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

    public bool hasKiller;

    public bool ready1;
    public bool ready2;
    public bool ready3;
    public bool ready4;
}

// 서버 -> 클라 : 씬 전환 UI 표시 상태
public struct ChangeSceneUIMessage : NetworkMessage
{
    public bool isShow;
}

// 서버 -> 클라 : 로비에서 생존자 생성 사운드 재생 요청
public struct LobbySurvivorSpawnSoundMessage : NetworkMessage { }

// 서버 -> 클라 : 게임 시작 사운드 재생 요청
public struct GameStartSoundMessage : NetworkMessage { }

// 서버 -> 클라 : 로비에서 살인마 생성 사운드 재생 요청
public struct LobbyKillerSpawnSoundMessage : NetworkMessage { }

public class CustomNetworkManager : NetworkManager
{
    public static CustomNetworkManager Instance { get; private set; }

    [Header("Port Settings")]
    [SerializeField] private List<ushort> serverPorts = new() { 7777, 7778, 7779 };

    [Header("Role Prefabs")]
    [SerializeField] private GameObject killerPrefab;
    [SerializeField] private List<GameObject> survivorPrefabs = new();

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

    private int currentPortIndex = -1;
    private bool isSearchingServer;
    private bool isLeavingManually;
    private bool isJoiningFinalRoom;
    private bool joinApproved;
    private bool isGameInProgress;
    private bool isReturningLobby;
    private ushort selectedPort;

    private Coroutine connectRoutine;

    public bool HasKiller
    {
        get
        {
            foreach (var role in joinedRoles.Values)
            {
                if (role == JoinRole.Killer)
                    return true;
            }

            return false;
        }
    }

    public bool IsRoomFull => numPlayers >= maxRoomPlayers;
    public bool IsRoomClosed => isGameInProgress || isReturningLobby;

    public bool CanJoinAsKiller => !IsRoomClosed && !HasKiller && !IsRoomFull;
    public bool CanJoinAsSurvivor => !IsRoomClosed && HasKiller && !IsRoomFull;

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
        if (!Application.isBatchMode)
            return;

        StartServer();
    }

    #region Client Connect

    public void ConnectAsKiller()
    {
        BeginRoleSearch(JoinRole.Killer);
    }

    public void ConnectAsSurvivor()
    {
        BeginRoleSearch(JoinRole.Survivor);
    }

    public void BackToRoleSelect()
    {
        isLeavingManually = true;
        isSearchingServer = false;
        joinApproved = false;
        isJoiningFinalRoom = false;
        selectedPort = 0;

        LobbyUIManager.Instance?.ShowLoading(false);

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

    public bool TryGetSurvivorIndex(NetworkConnectionToClient conn, out int survivorIndex)
    {
        survivorIndex = -1;

        if (conn == null)
            return false;

        return survivorPrefabIndexByConnection.TryGetValue(conn.connectionId, out survivorIndex);
    }

    private void BeginRoleSearch(JoinRole role)
    {
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

        localJoinRole = role;
        currentPortIndex = -1;
        isSearchingServer = true;
        isLeavingManually = false;
        isJoiningFinalRoom = false;
        joinApproved = false;
        selectedPort = 0;

        LobbyUIManager.Instance?.ShowLoading(true);
        probedRooms.Clear();

        ProbeNextPort();
    }

    private void ProbeNextPort()
    {
        currentPortIndex++;

        if (currentPortIndex >= serverPorts.Count)
        {
            SelectBestRoomAndJoin();
            return;
        }

        StartClientDelayed(serverPorts[currentPortIndex]);
    }

    private void SelectBestRoomAndJoin()
    {
        selectedPort = FindBestPort();

        if (selectedPort == 0)
        {
            Debug.LogWarning($"[CustomNetworkManager] {localJoinRole} 입장 가능한 방이 없습니다.");
            LobbyUIManager.Instance?.ShowLoading(false);
            ResetClientSearchState();
            return;
        }

        isJoiningFinalRoom = true;
        StartClientDelayed(selectedPort);
    }

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

    private ushort FindBestPort()
    {
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

    public void RequestSurvivorReady(bool isReady)
    {
        if (!NetworkClient.isConnected || localJoinRole != JoinRole.Survivor)
            return;

        NetworkClient.Send(new SurvivorReadyRequestMessage
        {
            isReady = isReady
        });
    }

    public void RequestStartGame()
    {
        if (!NetworkClient.isConnected || localJoinRole != JoinRole.Killer)
            return;

        NetworkClient.Send(new StartGameRequestMessage());
    }

    #endregion

    #region Server Lifecycle

    public override void OnStartServer()
    {
        base.OnStartServer();

        NetworkServer.RegisterHandler<JoinRequestMessage>(OnReceiveJoinRequest, false);
        NetworkServer.RegisterHandler<RoomProbeRequestMessage>(OnReceiveRoomProbeRequest, false);
        NetworkServer.RegisterHandler<SurvivorReadyRequestMessage>(OnReceiveSurvivorReadyRequest, false);
        NetworkServer.RegisterHandler<StartGameRequestMessage>(OnReceiveStartGameRequest, false);
        NetworkClient.RegisterHandler<LobbyKillerSpawnSoundMessage>(OnLobbyKillerSpawnSoundMessage, false);
    }

    public override void OnStopServer()
    {
        joinedRoles.Clear();
        survivorPrefabIndexByConnection.Clear();
        survivorReadyByConnection.Clear();

        isGameInProgress = false;
        isReturningLobby = false;

        base.OnStopServer();
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        if (IsRoomFull || IsRoomClosed)
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

        if (!NetworkServer.active)
            return;

        if (isGameInProgress && joinedRoles.Count == 0)
        {
            StartCoroutine(ReturnServerToLobbyWhenEmpty());
            return;
        }

        if (!isGameInProgress)
            BroadcastLobbyState();
    }

    private IEnumerator ReturnServerToLobbyWhenEmpty()
    {
        if (isReturningLobby)
            yield break;

        isReturningLobby = true;

        yield return null;

        joinedRoles.Clear();
        survivorPrefabIndexByConnection.Clear();
        survivorReadyByConnection.Clear();

        ServerChangeScene("Lobby");

        Debug.Log("[CustomNetworkManager] 모든 플레이어가 나가서 서버를 Lobby로 초기화");
    }

    #endregion

    #region Client Lifecycle

    public override void OnStartClient()
    {
        base.OnStartClient();

        NetworkClient.RegisterHandler<JoinDeniedMessage>(OnJoinDenied, false);
        NetworkClient.RegisterHandler<JoinAcceptedMessage>(OnJoinAccepted, false);
        NetworkClient.RegisterHandler<RoomProbeResponseMessage>(OnRoomProbeResponse, false);
        NetworkClient.RegisterHandler<LobbyStateMessage>(OnLobbyStateMessage, false);
        NetworkClient.RegisterHandler<ChangeSceneUIMessage>(OnChangeSceneUIMessage, false);
        NetworkClient.RegisterHandler<GameStartSoundMessage>(OnGameStartSoundMessage, false);
        NetworkClient.RegisterHandler<LobbySurvivorSpawnSoundMessage>(OnLobbySurvivorSpawnSoundMessage, false);
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();

        if (localJoinRole == JoinRole.None)
        {
            StopClient();
            return;
        }

        if (isJoiningFinalRoom)
        {
            GameSession session = GameSession.Instance;

            NetworkClient.Send(new JoinRequestMessage
            {
                role = (int)localJoinRole,

                accountId = session != null ? session.AccountId : 0,
                loginId = session != null ? session.LoginId : string.Empty,
                nickname = session != null ? session.Nickname : string.Empty,
                exp = session != null ? session.Exp : 0,
                level = session != null ? session.Level : 0
            });
        }
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
            LobbyUIManager.Instance?.ShowLoading(false);
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
            LobbyUIManager.Instance?.ShowLoading(false);
            ResetClientSearchState();
        }
    }

    private void OnJoinAccepted(JoinAcceptedMessage msg)
    {
        joinApproved = true;
        isSearchingServer = false;
        isJoiningFinalRoom = false;
        localJoinRole = (JoinRole)msg.role;

        LobbyUIManager.Instance?.ShowLoading(false);

        if (localJoinRole == JoinRole.Killer)
        {
            LobbyUIManager.Instance?.ShowKillerLobbyUI();
            LobbyUIManager.Instance?.SetStartButtonInteractable(false);
        }
        else if (localJoinRole == JoinRole.Survivor)
        {
            LobbyUIManager.Instance?.ShowSurvivorLobbyUI();
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
        LobbyUIManager.Instance?.SetLobbyReadyCount(msg.readySurvivorCount, msg.survivorCount);

        LobbyUIManager.Instance?.SetReadySlotUI(
            msg.ready1,
            msg.ready2,
            msg.ready3,
            msg.ready4
        );

        LobbyUIManager.Instance?.SetKillerLight(msg.hasKiller);

        if (localJoinRole == JoinRole.Killer)
            LobbyUIManager.Instance?.SetStartButtonInteractable(msg.canStart);
    }

    private void OnChangeSceneUIMessage(ChangeSceneUIMessage msg)
    {
        ChangeSceneUI.Instance?.Show(msg.isShow);
    }

    private void OnGameStartSoundMessage(GameStartSoundMessage msg)
    {
        AudioManager.PlayLocalAudio(AudioKey.GameStart, AudioDimension.Sound2D);
    }

    private void OnLobbySurvivorSpawnSoundMessage(LobbySurvivorSpawnSoundMessage msg)
    {
        AudioManager.PlayLocalAudio(AudioKey.LobbySurvivorSpawn, AudioDimension.Sound2D);
    }

    #endregion

    #region Server Request Handlers

    private void OnReceiveRoomProbeRequest(NetworkConnectionToClient conn, RoomProbeRequestMessage msg)
    {
        conn.Send(new RoomProbeResponseMessage
        {
            port = kcpTransport.Port,
            survivorCount = GetCurrentSurvivorCount(),
            hasKiller = HasKiller,
            isFull = IsRoomFull || IsRoomClosed
        });

        StartCoroutine(DisconnectNextFrame(conn));
    }

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

        if (!TryCreatePlayer(conn, msg, requestedRole, out string createFailReason))
        {
            conn.Send(new JoinDeniedMessage { reason = createFailReason });
            StartCoroutine(DisconnectNextFrame(conn));
            return;
        }

        joinedRoles[conn.connectionId] = requestedRole;

        if (requestedRole == JoinRole.Survivor)
            survivorReadyByConnection[conn.connectionId] = false;

        conn.Send(new JoinAcceptedMessage
        {
            role = (int)requestedRole,
            port = kcpTransport.Port
        });

        BroadcastLobbyState();
    }

    private void OnReceiveSurvivorReadyRequest(NetworkConnectionToClient conn, SurvivorReadyRequestMessage msg)
    {
        if (!joinedRoles.TryGetValue(conn.connectionId, out JoinRole role) || role != JoinRole.Survivor)
            return;

        survivorReadyByConnection[conn.connectionId] = msg.isReady;
        BroadcastLobbyState();
    }

    private void OnReceiveStartGameRequest(NetworkConnectionToClient conn, StartGameRequestMessage msg)
    {
        if (!joinedRoles.TryGetValue(conn.connectionId, out JoinRole role) || role != JoinRole.Killer)
            return;

        LobbyStateMessage lobbyState = GetLobbyState();

        if (!lobbyState.canStart)
        {
            Debug.LogWarning("[CustomNetworkManager] 아직 게임을 시작할 수 있는 상태가 아닙니다.");
            return;
        }

        MoveToGameScene();
    }

    private void OnLobbyKillerSpawnSoundMessage(LobbyKillerSpawnSoundMessage msg)
    {
        AudioManager.PlayLocalAudio(AudioKey.LobbyKillerSpawn, AudioDimension.Sound2D);
    }

    #endregion

    #region Lobby State

    private void BroadcastLobbyState()
    {
        if (!NetworkServer.active)
            return;

        LobbyStateMessage msg = GetLobbyState();

        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn != null && conn.isReady)
                conn.Send(msg);
        }
    }

    private LobbyStateMessage GetLobbyState()
    {
        int survivorCount = 0;
        int readyCount = 0;

        bool ready1 = false;
        bool ready2 = false;
        bool ready3 = false;
        bool ready4 = false;

        foreach (var pair in joinedRoles)
        {
            int connectionId = pair.Key;

            if (pair.Value != JoinRole.Survivor)
                continue;

            survivorCount++;

            bool isReady = survivorReadyByConnection.TryGetValue(connectionId, out bool ready) && ready;

            if (isReady)
                readyCount++;

            if (!survivorPrefabIndexByConnection.TryGetValue(connectionId, out int survivorIndex))
                continue;

            if (survivorIndex == 0)
                ready1 = isReady;
            else if (survivorIndex == 1)
                ready2 = isReady;
            else if (survivorIndex == 2)
                ready3 = isReady;
            else if (survivorIndex == 3)
                ready4 = isReady;
        }

        return new LobbyStateMessage
        {
            survivorCount = survivorCount,
            readySurvivorCount = readyCount,
            canStart = HasKiller && survivorCount > 0 && survivorCount == readyCount,

            hasKiller = HasKiller,

            ready1 = ready1,
            ready2 = ready2,
            ready3 = ready3,
            ready4 = ready4
        };
    }

    #endregion

    #region Role / Spawn

    private bool CanAcceptRole(JoinRole role, out string reason)
    {
        reason = string.Empty;

        if (role != JoinRole.Killer && role != JoinRole.Survivor)
        {
            reason = "유효하지 않은 역할 요청입니다.";
            return false;
        }

        if (IsRoomClosed)
        {
            reason = "이미 게임이 진행 중인 방입니다.";
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

    private bool TryCreatePlayer(NetworkConnectionToClient conn, JoinRequestMessage msg, JoinRole role, out string reason)
    {
        reason = string.Empty;

        GameObject prefabToSpawn = null;
        Transform spawnPoint = null;
        int survivorIndex = -1;

        if (SpawnPointBinder.Instance == null)
        {
            reason = "현재 씬에서 SceneBinder를 찾지 못했습니다.";
            return false;
        }

        switch (role)
        {
            case JoinRole.Killer:
                prefabToSpawn = killerPrefab;
                spawnPoint = SpawnPointBinder.Instance.GetKillerSpawnPoint();
                break;

            case JoinRole.Survivor:
                survivorIndex = GetAvailableSurvivorPrefabIndex();

                if (survivorIndex < 0)
                {
                    reason = "사용 가능한 Survivor 프리팹이 없습니다.";
                    return false;
                }

                prefabToSpawn = GetSurvivorPrefab(survivorIndex);
                spawnPoint = SpawnPointBinder.Instance.GetSurvivorSpawnPoint(survivorIndex);
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

        // DB 로그인 정보를 플레이어 UI 프로필에 적용한다.
        // 이 값은 SyncVar라서 모든 클라이언트의 인게임 UI에서 읽을 수 있다.
        PlayerUIProfile profile = playerObj.GetComponent<PlayerUIProfile>();

        if (profile == null)
            profile = playerObj.GetComponentInChildren<PlayerUIProfile>();

        if (profile != null)
        {
            profile.SetUserData(
                msg.accountId,
                msg.loginId,
                msg.nickname,
                msg.exp,
                msg.level
            );
        }
        else
        {
            Debug.LogWarning($"[CustomNetworkManager] {playerObj.name}에 PlayerUIProfile이 없습니다.");
        }

        if (role == JoinRole.Killer)
        {
            BroadcastLobbyKillerSpawnSound();
        }

        if (role == JoinRole.Survivor)
        {
            survivorPrefabIndexByConnection[conn.connectionId] = survivorIndex;

            // 로비에서 생존자가 생성되었을 때 모든 로비 인원에게 2D 사운드를 재생시킨다.
            BroadcastLobbySurvivorSpawnSound();
        }

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

    #endregion

    #region Scene Change

    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);

        if (sceneName == "Lobby")
        {
            isGameInProgress = false;
            isReturningLobby = false;

            Debug.Log("[CustomNetworkManager] Lobby 씬 복귀 완료 / 포트 재오픈");
            return;
        }

        if (sceneName == inGameSceneName)
        {
            StartCoroutine(SetupInGameScene());
        }
    }

    private IEnumerator SetupInGameScene()
    {
        // InGame 씬 오브젝트들이 생성될 시간 확보
        yield return new WaitForSeconds(0.25f);

        float timeout = 3f;
        float elapsed = 0f;

        while (SpawnPointBinder.Instance == null && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (SpawnPointBinder.Instance == null)
        {
            Debug.LogWarning("[CustomNetworkManager] InGame 씬에서 SceneBinder를 찾지 못했습니다.");
            BroadcastChangeSceneUI(false);
            yield break;
        }

        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn == null)
            {
                Debug.LogWarning("[CustomNetworkManager] conn이 null입니다.");
                continue;
            }

            if (conn.identity == null)
            {
                Debug.LogWarning($"[CustomNetworkManager] Conn {conn.connectionId} identity가 null입니다.");
                continue;
            }

            if (!joinedRoles.TryGetValue(conn.connectionId, out JoinRole role))
            {
                Debug.LogWarning($"[CustomNetworkManager] Conn {conn.connectionId} role을 찾지 못했습니다.");
                continue;
            }

            Transform spawnPoint = GetSpawnPointForConnection(conn);

            if (spawnPoint == null)
            {
                Debug.LogWarning($"[CustomNetworkManager] Conn {conn.connectionId}, Role {role}의 SpawnPoint를 찾지 못했습니다.");
                continue;
            }

            Debug.Log($"[CustomNetworkManager] InGame 배치 / Conn:{conn.connectionId} / Role:{role} / Player:{conn.identity.name} / Spawn:{spawnPoint.name} / Pos:{spawnPoint.position}");

            KillerMove killerMove = conn.identity.GetComponent<KillerMove>();

            if (killerMove != null)
            {
                killerMove.ServerTeleportTo(spawnPoint.position, spawnPoint.rotation);
                continue;
            }

            SurvivorMove survivorMove = conn.identity.GetComponent<SurvivorMove>();

            if (survivorMove != null)
            {
                survivorMove.ServerTeleportTo(spawnPoint.position, spawnPoint.rotation);
                continue;
            }

            conn.identity.transform.SetPositionAndRotation(
                spawnPoint.position,
                spawnPoint.rotation
            );
        }

        // 인게임 상태 적용
        ApplyInGameStateToAllPlayers();

        // 암전 상태 2초 유지
        yield return new WaitForSeconds(2f);

        // Fade Out 시작
        BroadcastChangeSceneUI(false);
    }

    private void ApplyInGameStateToAllPlayers()
    {
        if (!NetworkServer.active)
            return;

        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn == null || conn.identity == null)
                continue;

            if (!joinedRoles.TryGetValue(conn.connectionId, out JoinRole role))
                continue;

            if (role == JoinRole.Killer)
            {
                KillerMove killerMove = conn.identity.GetComponent<KillerMove>();

                if (killerMove != null)
                    killerMove.SetInGameStateServer();

                continue;
            }

            if (role == JoinRole.Survivor)
            {
                SurvivorInput survivorInput = conn.identity.GetComponent<SurvivorInput>();

                if (survivorInput != null)
                    survivorInput.SetInputEnabledServer(true);

                continue;
            }
        }
    }

    public void MoveToGameScene()
    {
        if (!NetworkServer.active || string.IsNullOrWhiteSpace(inGameSceneName))
            return;

        if (isGameInProgress)
            return;

        isGameInProgress = true;

        StartCoroutine(MoveToGameSceneRoutine());
    }

    private IEnumerator MoveToGameSceneRoutine()
    {
        // 게임 시작이 서버에서 승인된 순간 모든 클라이언트에게 시작 사운드를 재생시킨다.
        BroadcastGameStartSound();

        // Fade In 시작
        BroadcastChangeSceneUI(true);

        // Fade In 1초 대기
        yield return new WaitForSeconds(1f);

        // 화면이 완전히 암전된 뒤 씬 이동
        ServerChangeScene(inGameSceneName);
    }

    private Transform GetSpawnPointForConnection(NetworkConnectionToClient conn)
    {
        if (conn == null)
            return null;

        if (SpawnPointBinder.Instance == null)
            return null;

        if (!joinedRoles.TryGetValue(conn.connectionId, out JoinRole role))
            return null;

        if (role == JoinRole.Killer)
            return SpawnPointBinder.Instance.GetKillerSpawnPoint();

        if (role == JoinRole.Survivor)
        {
            if (!survivorPrefabIndexByConnection.TryGetValue(conn.connectionId, out int survivorIndex))
                return null;

            return SpawnPointBinder.Instance.GetSurvivorSpawnPoint(survivorIndex);
        }

        return null;
    }

    private void BroadcastChangeSceneUI(bool value)
    {
        if (!NetworkServer.active)
            return;

        ChangeSceneUIMessage msg = new ChangeSceneUIMessage
        {
            isShow = value
        };

        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn == null)
                continue;

            conn.Send(msg);
        }
    }

    private void BroadcastGameStartSound()
    {
        if (!NetworkServer.active)
            return;

        GameStartSoundMessage msg = new GameStartSoundMessage();

        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn == null)
                continue;

            if (!conn.isReady)
                continue;

            conn.Send(msg);
        }
    }

    private void BroadcastLobbySurvivorSpawnSound()
    {
        if (!NetworkServer.active)
            return;

        LobbySurvivorSpawnSoundMessage msg = new LobbySurvivorSpawnSoundMessage();

        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn == null)
                continue;

            if (!conn.isReady)
                continue;

            conn.Send(msg);
        }
    }

    private void BroadcastLobbyKillerSpawnSound()
    {
        if (!NetworkServer.active)
            return;

        LobbyKillerSpawnSoundMessage msg = new LobbyKillerSpawnSoundMessage();

        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn == null)
                continue;

            if (!conn.isReady)
                continue;

            conn.Send(msg);
        }
    }

    public void LeaveCurrentGameToLobby()
    {
        if (isLeavingManually)
            return;

        if (!NetworkClient.active && !NetworkClient.isConnected)
        {
            ResetClientSearchState();
            SceneManager.LoadScene("Lobby");
            return;
        }

        isLeavingManually = true;
        isSearchingServer = false;
        joinApproved = false;
        isJoiningFinalRoom = false;
        selectedPort = 0;

        LobbyUIManager.Instance?.ShowLoading(false);

        if (connectRoutine != null)
        {
            StopCoroutine(connectRoutine);
            connectRoutine = null;
        }

        StartCoroutine(LeaveCurrentGameRoutine());
    }

    private IEnumerator LeaveCurrentGameRoutine()
    {
        // 1. 로컬 클라이언트 화면만 블랙아웃
        if (ChangeSceneUI.Instance != null)
            ChangeSceneUI.Instance.Show(true);

        // Fade In 시간 대기
        yield return new WaitForSecondsRealtime(1f);

        // 2. 서버 연결 종료
        StopClient();

        while (NetworkClient.active || NetworkClient.isConnected)
            yield return null;

        ResetClientSearchState();

        // 3. Lobby 씬 로드
        SceneManager.LoadScene("Lobby");

        // Lobby 씬이 로드될 시간 1프레임 대기
        yield return null;

        // 4. 블랙아웃 해제
        if (ChangeSceneUI.Instance != null)
            ChangeSceneUI.Instance.Show(false);
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

        foreach (var role in joinedRoles.Values)
        {
            if (role == JoinRole.Survivor)
                count++;
        }

        return count;
    }

    #endregion
}