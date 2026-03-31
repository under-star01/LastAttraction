using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using kcp2k;

public enum JoinRole
{
    None,
    Killer,
    Survivor
}

public struct JoinRequestMessage : NetworkMessage
{
    public int role;
}

public struct JoinDeniedMessage : NetworkMessage
{
    public string reason;
}

public struct JoinAcceptedMessage : NetworkMessage
{
    public int role;
    public ushort port;
}

public class CustomNetworkManager : NetworkManager
{
    public static CustomNetworkManager Instance { get; private set; }

    [Header("Port Settings")]
    [SerializeField] private List<ushort> serverPorts = new List<ushort> { 7777, 7778, 7779 };

    [Header("Role Prefabs")]
    [SerializeField] private GameObject killerPrefab;
    [SerializeField] private GameObject survivorPrefab;

    [Header("Spawn Points")]
    [SerializeField] private Transform killerSpawnPoint;
    [SerializeField] private List<Transform> survivorSpawnPoints = new List<Transform>();

    [Header("Match Settings")]
    [SerializeField] private int maxRoomPlayers = 5;

    private KcpTransport kcpTransport;

    // 클라이언트가 현재 입장 시도 중인 역할
    private JoinRole localJoinRole = JoinRole.None;

    // 서버가 connection 별 실제 입장 역할 저장
    private readonly Dictionary<int, JoinRole> joinedRoles = new Dictionary<int, JoinRole>();

    // 클라이언트 포트 순차 탐색 상태
    private int currentPortIndex = -1;
    private bool isSearchingServer = false;
    private bool joinApproved = false;
    private bool retryScheduled = false;

    // 수동 취소/돌아가기 여부
    private bool isLeavingManually = false;

    public bool HasKiller
    {
        get
        {
            foreach (var pair in joinedRoles)
            {
                if (pair.Value == JoinRole.Killer)
                    return true;
            }
            return false;
        }
    }

    public bool IsRoomFull => numPlayers >= maxRoomPlayers;
    public bool CanJoinAsKiller => !HasKiller && !IsRoomFull;
    public bool CanJoinAsSurvivor => HasKiller && !IsRoomFull;

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

        ushort targetPort = GetPortFromArgs();
        kcpTransport.Port = targetPort;

        maxConnections = maxRoomPlayers;

        Debug.Log($"[CustomNetworkManager] 적용된 포트: {kcpTransport.Port}");
        Debug.Log($"[CustomNetworkManager] 최대 인원 수: {maxRoomPlayers}");
    }

    private void Start()
    {
        if (Application.isBatchMode)
        {
            Debug.Log("[CustomNetworkManager] BatchMode 감지 - 서버를 시작합니다.");
            StartServer();
        }
    }

    #region Client Connect API

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
        Debug.Log("[CustomNetworkManager] 돌아가기 요청");

        isLeavingManually = true;

        // 자동 재시도 방지
        isSearchingServer = false;
        retryScheduled = false;
        joinApproved = false;

        // 클라이언트가 연결 중이거나 활성 상태면 종료
        if (NetworkClient.active || NetworkClient.isConnected)
        {
            StopClient();
        }
        else
        {
            ResetClientSearchState();
            isLeavingManually = false;
        }
    }

    private void BeginRoleSearch(JoinRole role)
    {
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

        isLeavingManually = false;
        localJoinRole = role;
        currentPortIndex = -1;
        isSearchingServer = true;
        joinApproved = false;
        retryScheduled = false;

        Debug.Log($"[CustomNetworkManager] {role} 서버 탐색 시작 - Address: {networkAddress}");
        TryNextPort();
    }

    private void TryNextPort()
    {
        currentPortIndex++;

        if (currentPortIndex >= serverPorts.Count)
        {
            Debug.LogWarning($"[CustomNetworkManager] {localJoinRole} 입장 실패 - 시도 가능한 모든 포트를 확인했지만 접속할 서버를 찾지 못했습니다.");
            ResetClientSearchState();
            return;
        }

        ushort targetPort = serverPorts[currentPortIndex];
        kcpTransport.Port = targetPort;

        Debug.Log($"[CustomNetworkManager] {localJoinRole} 접속 시도 -> {networkAddress}:{targetPort}");
        StartClient();
    }

    private IEnumerator RetryNextPortCoroutine()
    {
        retryScheduled = true;

        yield return null;

        retryScheduled = false;

        if (!isSearchingServer || isLeavingManually)
            yield break;

        TryNextPort();
    }

    private void ResetClientSearchState()
    {
        localJoinRole = JoinRole.None;
        currentPortIndex = -1;
        isSearchingServer = false;
        joinApproved = false;
        retryScheduled = false;
    }

    #endregion

    #region Server Lifecycle

    public override void OnStartServer()
    {
        base.OnStartServer();

        NetworkServer.RegisterHandler<JoinRequestMessage>(OnReceiveJoinRequest, false);

        Debug.Log($"[CustomNetworkManager] 서버 시작 완료 - Listen Port: {kcpTransport.Port}");
    }

    public override void OnStopServer()
    {
        joinedRoles.Clear();
        base.OnStopServer();
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        if (IsRoomFull)
        {
            Debug.LogWarning($"[CustomNetworkManager] 접속 거부 - 방이 가득 참 (connId: {conn.connectionId})");
            conn.Disconnect();
            return;
        }

        base.OnServerConnect(conn);
        Debug.Log($"[CustomNetworkManager] 클라이언트 접속 - connId: {conn.connectionId}");
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        if (joinedRoles.ContainsKey(conn.connectionId))
            joinedRoles.Remove(conn.connectionId);

        Debug.Log($"[CustomNetworkManager] 클라이언트 종료 - connId: {conn.connectionId}, disconnect 전 인원: {numPlayers}");
        base.OnServerDisconnect(conn);
        Debug.Log($"[CustomNetworkManager] disconnect 후 인원: {numPlayers}, HasKiller: {HasKiller}");
    }

    #endregion

    #region Client Lifecycle

    public override void OnStartClient()
    {
        base.OnStartClient();

        NetworkClient.RegisterHandler<JoinDeniedMessage>(OnJoinDenied, false);
        NetworkClient.RegisterHandler<JoinAcceptedMessage>(OnJoinAccepted, false);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        ResetClientSearchState();
        isLeavingManually = false;
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();

        Debug.Log($"[CustomNetworkManager] 서버 접속 성공 - requestedRole: {localJoinRole}, targetPort: {kcpTransport.Port}");

        if (localJoinRole == JoinRole.None)
        {
            Debug.LogWarning("[CustomNetworkManager] 선택된 접속 역할이 없습니다. 접속 종료.");
            StopClient();
            return;
        }

        JoinRequestMessage msg = new JoinRequestMessage
        {
            role = (int)localJoinRole
        };

        NetworkClient.Send(msg);
    }

    public override void OnClientDisconnect()
    {
        bool shouldRetry = isSearchingServer && !joinApproved && !isLeavingManually;

        Debug.Log($"[CustomNetworkManager] 서버 연결 종료 - role: {localJoinRole}, lastPort: {(currentPortIndex >= 0 && currentPortIndex < serverPorts.Count ? serverPorts[currentPortIndex].ToString() : "Unknown")}, manualLeave: {isLeavingManually}");

        base.OnClientDisconnect();

        if (shouldRetry && !retryScheduled)
        {
            StartCoroutine(RetryNextPortCoroutine());
            return;
        }

        ResetClientSearchState();
        isLeavingManually = false;
    }

    private void OnJoinDenied(JoinDeniedMessage msg)
    {
        Debug.LogWarning($"[CustomNetworkManager] 입장 거부: {msg.reason}");

        if (NetworkClient.isConnected || NetworkClient.active)
            StopClient();
    }

    private void OnJoinAccepted(JoinAcceptedMessage msg)
    {
        joinApproved = true;
        isSearchingServer = false;

        JoinRole approvedRole = (JoinRole)msg.role;
        Debug.Log($"[CustomNetworkManager] 입장 승인 - role: {approvedRole}, port: {msg.port}");
    }

    #endregion

    #region Join Request Handling

    private void OnReceiveJoinRequest(NetworkConnectionToClient conn, JoinRequestMessage msg)
    {
        JoinRole requestedRole = (JoinRole)msg.role;

        Debug.Log($"[CustomNetworkManager] JoinRequest 수신 - connId: {conn.connectionId}, requestedRole: {requestedRole}");

        if (conn.identity != null)
        {
            Debug.LogWarning($"[CustomNetworkManager] 이미 플레이어가 생성된 connection 입니다. connId: {conn.connectionId}");
            return;
        }

        if (!CanAcceptRole(requestedRole, out string denyReason))
        {
            conn.Send(new JoinDeniedMessage { reason = denyReason });
            conn.Disconnect();
            return;
        }

        CreatePlayerForConnection(conn, requestedRole);

        joinedRoles[conn.connectionId] = requestedRole;

        conn.Send(new JoinAcceptedMessage
        {
            role = (int)requestedRole,
            port = kcpTransport.Port
        });
    }

    private bool CanAcceptRole(JoinRole role, out string reason)
    {
        reason = string.Empty;

        if (IsRoomFull)
        {
            reason = "방이 가득 찼습니다.";
            return false;
        }

        switch (role)
        {
            case JoinRole.Killer:
                if (!CanJoinAsKiller)
                {
                    reason = "이미 Killer가 존재하는 방입니다.";
                    return false;
                }
                return true;

            case JoinRole.Survivor:
                if (!CanJoinAsSurvivor)
                {
                    reason = "아직 Killer가 없는 방에는 Survivor가 입장할 수 없습니다.";
                    return false;
                }
                return true;

            default:
                reason = "유효하지 않은 역할 요청입니다.";
                return false;
        }
    }

    private void CreatePlayerForConnection(NetworkConnectionToClient conn, JoinRole role)
    {
        GameObject prefabToSpawn = null;
        Transform spawnPoint = null;

        switch (role)
        {
            case JoinRole.Killer:
                prefabToSpawn = killerPrefab;
                spawnPoint = killerSpawnPoint;
                break;

            case JoinRole.Survivor:
                prefabToSpawn = survivorPrefab;
                int survivorIndex = GetCurrentSurvivorCount();
                spawnPoint = GetSurvivorSpawnPoint(survivorIndex);
                break;
        }

        if (prefabToSpawn == null)
        {
            Debug.LogError($"[CustomNetworkManager] role {role} 에 해당하는 프리팹이 없습니다.");
            conn.Disconnect();
            return;
        }

        Vector3 spawnPosition = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion spawnRotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        GameObject playerObj = Instantiate(prefabToSpawn, spawnPosition, spawnRotation);
        NetworkServer.AddPlayerForConnection(conn, playerObj);

        Debug.Log($"[CustomNetworkManager] 플레이어 생성 완료 - connId: {conn.connectionId}, role: {role}, totalPlayers: {numPlayers}");
    }

    #endregion

    #region Utils

    private ushort GetPortFromArgs()
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-port")
            {
                if (ushort.TryParse(args[i + 1], out ushort parsedPort))
                {
                    Debug.Log($"[CustomNetworkManager] 명령줄 포트 인자 감지: {parsedPort}");
                    return parsedPort;
                }
                else
                {
                    Debug.LogWarning($"[CustomNetworkManager] 포트 파싱 실패: {args[i + 1]}");
                }
            }
        }

        if (serverPorts == null || serverPorts.Count == 0)
        {
            Debug.LogError("[CustomNetworkManager] serverPorts가 비어 있습니다. 기본 포트를 결정할 수 없습니다.");
            return 7777;
        }

        Debug.Log($"[CustomNetworkManager] 명령줄 포트가 없어 serverPorts[0] 사용: {serverPorts[0]}");
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

    private Transform GetSurvivorSpawnPoint(int survivorIndex)
    {
        if (survivorSpawnPoints == null || survivorSpawnPoints.Count == 0)
            return null;

        int index = Mathf.Clamp(survivorIndex, 0, survivorSpawnPoints.Count - 1);
        return survivorSpawnPoints[index];
    }

    #endregion
}