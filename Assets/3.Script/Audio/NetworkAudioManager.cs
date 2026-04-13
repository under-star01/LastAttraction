using Mirror;
using UnityEngine;

// 멀티플레이에서 소리 이벤트를 모든 클라이언트로 보내는 매니저
// 실제 재생은 각 클라이언트의 AudioManager가 담당한다
public class NetworkAudioManager : NetworkBehaviour
{
    public static NetworkAudioManager Instance;

    private void Awake()
    {
        Instance = this;
    }

    // 클라이언트가 서버에 소리 재생 요청
    [Command(requiresAuthority = false)]
    public void CmdPlayAudio(AudioKey key, AudioListenerTarget listenerTarget, AudioDimension dimension, Vector3 worldPosition)
    {
        RpcPlayAudio(key, listenerTarget, dimension, worldPosition);
    }

    // 서버가 모든 클라이언트에게 소리 재생 알림
    [ClientRpc]
    private void RpcPlayAudio(AudioKey key, AudioListenerTarget listenerTarget, AudioDimension dimension, Vector3 worldPosition)
    {
        if (AudioManager.Instance == null)
            return;

        AudioManager.Instance.PlayAudio(key, listenerTarget, dimension, worldPosition);
    }

    // 모두가 듣는 소리 재생
    public static void PlayAudioForEveryone(AudioKey key, AudioDimension dimension, Vector3 worldPosition)
    {
        if (Instance == null)
            return;

        if (Instance.isServer)
            Instance.RpcPlayAudio(key, AudioListenerTarget.Everyone, dimension, worldPosition);
        else
            Instance.CmdPlayAudio(key, AudioListenerTarget.Everyone, dimension, worldPosition);
    }

    // 킬러만 듣는 소리 재생
    public static void PlayAudioForKiller(AudioKey key, AudioDimension dimension, Vector3 worldPosition)
    {
        if (Instance == null)
            return;

        if (Instance.isServer)
            Instance.RpcPlayAudio(key, AudioListenerTarget.KillerOnly, dimension, worldPosition);
        else
            Instance.CmdPlayAudio(key, AudioListenerTarget.KillerOnly, dimension, worldPosition);
    }

    // 생존자만 듣는 소리 재생
    public static void PlayAudioForSurvivor(AudioKey key, AudioDimension dimensionType, Vector3 worldPosition)
    {
        if (Instance == null)
            return;

        if (Instance.isServer)
            Instance.RpcPlayAudio(key, AudioListenerTarget.SurvivorOnly, dimensionType, worldPosition);
        else
            Instance.CmdPlayAudio(key, AudioListenerTarget.SurvivorOnly, dimensionType, worldPosition);
    }
}