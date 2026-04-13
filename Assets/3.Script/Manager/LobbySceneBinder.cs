using UnityEngine;
using Unity.Cinemachine;

public class LobbySceneBinder : MonoBehaviour
{
    public static LobbySceneBinder Instance { get; private set; }

    [Header("Lobby Cameras")]
    [SerializeField] private CinemachineCamera killerLobbyCamera;
    [SerializeField] private CinemachineCamera survivorLobbyCamera;

    [Header("Lobby UI")]
    [SerializeField] private ProgressUI progressUI;
    [SerializeField] private QTEUI qteUI;


    [Header("Priority")]
    [SerializeField] private int livePriority = 20;
    [SerializeField] private int idlePriority = 10;

    private void Awake()
    {
        Instance = this;
    }

    public void ApplyCameraForRole(JoinRole role)
    {
        if (killerLobbyCamera == null || survivorLobbyCamera == null)
        {
            Debug.LogWarning("[LobbySceneBinder] 로비용 Cinemachine Camera가 할당되지 않았습니다.");
            return;
        }

        switch (role)
        {
            case JoinRole.Killer:
                killerLobbyCamera.Priority = livePriority;
                survivorLobbyCamera.Priority = idlePriority;
                Debug.Log("[LobbySceneBinder] Killer 로비 카메라 활성화");
                break;

            case JoinRole.Survivor:
            case JoinRole.None:
                survivorLobbyCamera.Priority = livePriority;
                killerLobbyCamera.Priority = idlePriority;
                Debug.Log("[LobbySceneBinder] Survivor 로비 카메라 활성화");
                break;
        }
    }

    public ProgressUI GetProgressUI()
    {
        return progressUI;
    }

    public QTEUI GetQTEUI()
    {
        return qteUI;
    }
}