using UnityEngine;
using Mirror;
using UnityEngine.SceneManagement;

public class LobbyPlayerMode : NetworkBehaviour
{
    [SerializeField] private JoinRole role = JoinRole.None;
    [SerializeField] private string lobbySceneName = "Lobby";

    public override void OnStartClient()
    {
        base.OnStartClient();
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyLobbyCamera(SceneManager.GetActiveScene().name);
    }

    public override void OnStopClient()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        base.OnStopClient();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyLobbyCamera(scene.name);
    }

    private void ApplyLobbyCamera(string sceneName)
    {
        if (!isLocalPlayer)
            return;

        if (sceneName != lobbySceneName)
            return;

        LobbySceneBinder.Instance?.ApplyCameraForRole(role);
    }
}