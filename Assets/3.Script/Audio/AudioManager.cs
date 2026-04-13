using System.Collections.Generic;
using Mirror;
using UnityEngine;

// 2d인지 3d인지 킬러만인지 생존자인지 모두 들을지 판정
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("오디오 목록")]
    [SerializeField] private AudioData[] audioDataList;

    [Header("2D 사운드 재생용 AudioSource")]
    [SerializeField] private AudioSource audioSource2D;

    // 빠르게 찾기 위해 Dictionary 사용
    private Dictionary<AudioKey, AudioData> audioDataMap = new Dictionary<AudioKey, AudioData>();

    private void Awake()
    {
        Instance = this;

        // 인스펙터에서 넣은 오디오들을 Dictionary에 저장
        audioDataMap.Clear();

        for (int i = 0; i < audioDataList.Length; i++)
        {
            AudioData data = audioDataList[i];

            if (data == null)
                continue;

            if (data.clip == null)
                continue;

            if (audioDataMap.ContainsKey(data.key))
                continue;

            audioDataMap.Add(data.key, data);
        }
    }

    // 실제 오디오 재생 함수
    public void PlayAudio(AudioKey key, AudioListenerTarget listenerTarget, AudioDimension dimension, Vector3 worldPosition)
    {
        // 이 클라이언트가 이 소리를 들어야 하는지 먼저 검사
        if (!CanThisClientHear(listenerTarget))
            return;

        // 등록된 오디오 찾기
        if (!audioDataMap.TryGetValue(key, out AudioData data))
            return;

        if (dimension == AudioDimension.Sound2D)
            Play2DAudio(data);
        else
            Play3DAudio(data, worldPosition);
    }

    // 2D 소리 재생
    private void Play2DAudio(AudioData data)
    {
        if (audioSource2D == null)
            return;

        audioSource2D.PlayOneShot(data.clip, data.volume);
    }

    // 3D 소리 재생
    private void Play3DAudio(AudioData data, Vector3 worldPosition)
    {
        AudioSource.PlayClipAtPoint(data.clip, worldPosition, data.volume);
    }

    // 현재 이 클라이언트가 이 소리를 들어야 하는지 판정
    private bool CanThisClientHear(AudioListenerTarget listenerTarget)
    {
        // 모두 듣는 소리
        if (listenerTarget == AudioListenerTarget.Everyone)
            return true;

        // 로컬 전용 소리
        if (listenerTarget == AudioListenerTarget.LocalOnly)
            return true;

        // 내 로컬 플레이어가 아직 없으면 역할 판정 불가
        if (NetworkClient.localPlayer == null)
            return false;

        GameObject localPlayerObject = NetworkClient.localPlayer.gameObject;

        if (listenerTarget == AudioListenerTarget.KillerOnly)
            return localPlayerObject.CompareTag("Killer");

        if (listenerTarget == AudioListenerTarget.SurvivorOnly)
            return localPlayerObject.CompareTag("Survivor");

        return false;
    }

    // 로컬 클라이언트에서만 바로 소리를 재생할 때 쓰는 편의 함수
    // 예: 버튼 클릭음, 내 전용 경고음
    public static void PlayLocalAudio(AudioKey key, AudioDimension dimension = AudioDimension.Sound2D, Vector3? worldPosition = null)
    {
        if (AudioManager.Instance == null)
            return;

        Vector3 playPosition = worldPosition ?? Vector3.zero;

        AudioManager.Instance.PlayAudio(key, AudioListenerTarget.LocalOnly, dimension, playPosition);
    }
}