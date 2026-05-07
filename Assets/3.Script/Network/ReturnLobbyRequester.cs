using Mirror;
using UnityEngine;

public class ReturnLobbyRequester : NetworkBehaviour
{
    public void RequestReturnLobby()
    {
        if (!isLocalPlayer)
            return;

        CmdRequestReturnLobby();
    }

    [Command]
    private void CmdRequestReturnLobby()
    {
        if (CustomNetworkManager.Instance == null)
            return;

        CustomNetworkManager.Instance.ServerReturnToLobby();
    }
}