using Mirror;
using UnityEngine;

// 멀티플레이에서 소리 이벤트를 네트워크로 보내는 매니저
// 실제 재생은 각 클라이언트의 AudioManager가 담당한다
public class NetworkAudioManager : NetworkBehaviour
{
    public static NetworkAudioManager Instance;

    private void Awake()
    {
        // 싱글톤
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    [Command(requiresAuthority = false)]
    public void CmdPlayAudio(AudioKey key, AudioListenerTarget listenerTarget, AudioDimension dimension, Vector3 worldPosition)
    {
        RpcPlayAudio(key, listenerTarget, dimension, worldPosition);
    }

    [ClientRpc]
    private void RpcPlayAudio(AudioKey key, AudioListenerTarget listenerTarget, AudioDimension dimension, Vector3 worldPosition)
    {
        if (AudioManager.Instance == null)
            return;

        AudioManager.Instance.PlayAudio(key, listenerTarget, dimension, worldPosition);
    }

    public static void PlayAudioForEveryone(AudioKey key, AudioDimension dimension, Vector3 worldPosition)
    {
        if (Instance == null)
            return;

        if (Instance.isServer)
            Instance.RpcPlayAudio(key, AudioListenerTarget.Everyone, dimension, worldPosition);
        else
            Instance.CmdPlayAudio(key, AudioListenerTarget.Everyone, dimension, worldPosition);
    }

    public static void PlayAudioForKiller(AudioKey key, AudioDimension dimension, Vector3 worldPosition)
    {
        if (Instance == null)
            return;

        if (Instance.isServer)
            Instance.RpcPlayAudio(key, AudioListenerTarget.KillerOnly, dimension, worldPosition);
        else
            Instance.CmdPlayAudio(key, AudioListenerTarget.KillerOnly, dimension, worldPosition);
    }

    public static void PlayAudioForSurvivors(AudioKey key, AudioDimension dimension, Vector3 worldPosition)
    {
        if (Instance == null)
            return;

        if (Instance.isServer)
            Instance.RpcPlayAudio(key, AudioListenerTarget.SurvivorOnly, dimension, worldPosition);
        else
            Instance.CmdPlayAudio(key, AudioListenerTarget.SurvivorOnly, dimension, worldPosition);
    }

    // 루프 사운드 시작 요청
    [Command(requiresAuthority = false)]
    public void CmdStartLoopAudio(uint ownerNetId, AudioKey key, AudioListenerTarget listenerTarget, AudioDimension dimension, Vector3 worldPosition)
    {
        RpcStartLoopAudio(ownerNetId, key, listenerTarget, dimension, worldPosition);
    }

    // 모든 클라이언트에서 루프 사운드 시작
    [ClientRpc]
    private void RpcStartLoopAudio(uint ownerNetId, AudioKey key, AudioListenerTarget listenerTarget, AudioDimension dimension, Vector3 worldPosition)
    {
        if (AudioManager.Instance == null)
            return;

        AudioManager.Instance.StartLoopAudio(ownerNetId, key, listenerTarget, dimension, worldPosition);
    }

    // 루프 사운드 종료 요청
    [Command(requiresAuthority = false)]
    public void CmdStopLoopAudio(uint ownerNetId, AudioKey key)
    {
        RpcStopLoopAudio(ownerNetId, key);
    }

    // 모든 클라이언트에서 루프 사운드 종료
    [ClientRpc]
    private void RpcStopLoopAudio(uint ownerNetId, AudioKey key)
    {
        if (AudioManager.Instance == null)
            return;

        AudioManager.Instance.StopLoopAudio(ownerNetId, key);
    }

    // 모두가 듣는 루프 사운드 시작
    public static void StartLoopAudioForEveryone(uint ownerNetId, AudioKey key, AudioDimension dimension, Vector3 worldPosition)
    {
        if (Instance == null)
            return;

        if (Instance.isServer)
            Instance.RpcStartLoopAudio(ownerNetId, key, AudioListenerTarget.Everyone, dimension, worldPosition);
        else
            Instance.CmdStartLoopAudio(ownerNetId, key, AudioListenerTarget.Everyone, dimension, worldPosition);
    }

    // 모두가 듣는 루프 사운드 종료
    public static void StopLoopAudioForEveryone(uint ownerNetId, AudioKey key)
    {
        if (Instance == null)
            return;

        if (Instance.isServer)
            Instance.RpcStopLoopAudio(ownerNetId, key);
        else
            Instance.CmdStopLoopAudio(ownerNetId, key);
    }
}