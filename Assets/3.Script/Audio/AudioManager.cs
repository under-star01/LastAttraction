using System.Collections.Generic;
using Mirror;
using UnityEngine;

// 실제로 소리를 재생하는 매니저
// - 2D 재생
// - 3D 재생
// - 킬러만 / 생존자만 / 모두 들을지 판정
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("오디오 목록")]
    [SerializeField] private AudioData[] audioDataList;

    [Header("2D 사운드 재생용 AudioSource")]
    [SerializeField] private AudioSource audioSource2D;

    // 오디오를 빠르게 찾기 위한 Dictionary
    private Dictionary<AudioKey, AudioData> audioDataMap = new Dictionary<AudioKey, AudioData>();

    // 루프 사운드를 관리하기 위한 Dictionary
    // key는 네트워크 오브젝트 netId + AudioKey 조합으로 만든다.
    private Dictionary<string, GameObject> loopAudioMap = new Dictionary<string, GameObject>();

    private void Awake()
    {
        // 싱글톤 중복 방지
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // 인스펙터에서 넣은 오디오들을 Dictionary에 저장
        audioDataMap.Clear();

        if (audioDataList == null)
            return;

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

        // 2D / 3D 방식에 따라 재생
        if (dimension == AudioDimension.Sound2D)
            Play2DAudio(data);
        else
            Play3DAudio(data, worldPosition);
    }

    // 2D 소리 재생
    // 버튼 소리, UI 소리, 개인 알림음 등에 사용
    private void Play2DAudio(AudioData data)
    {
        if (data == null)
            return;

        if (audioSource2D == null)
            return;

        if (data.clip == null)
            return;

        audioSource2D.PlayOneShot(data.clip, data.volume);
    }

    // 3D 소리 재생
    // 월드 위치에서 나는 소리
    // PlayClipAtPoint 대신 직접 AudioSource를 만들어서
    // 거리 설정(min/max distance)을 같이 적용할 수 있게 함
    private void Play3DAudio(AudioData data, Vector3 worldPosition)
    {
        if (data == null)
            return;

        if (data.clip == null)
            return;

        GameObject tempAudioObject = new GameObject("3D_Audio_" + data.key);
        tempAudioObject.transform.position = worldPosition;

        AudioSource source = tempAudioObject.AddComponent<AudioSource>();
        source.clip = data.clip;
        source.volume = data.volume;

        // 3D 사운드 설정
        source.spatialBlend = 1f;
        source.minDistance = data.minDistance;
        source.maxDistance = data.maxDistance;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.playOnAwake = false;

        source.Play();

        // 재생 끝나면 임시 오브젝트 삭제
        Destroy(tempAudioObject, data.clip.length + 0.1f);
    }

    // 루프 사운드 시작
    // loopId는 네트워크에서 같은 루프 사운드를 구분하기 위한 ID다.
    public void StartLoopAudio(
        uint ownerNetId,
        AudioKey key,
        AudioListenerTarget listenerTarget,
        AudioDimension dimension,
        Vector3 worldPosition)
    {
        if (!CanThisClientHear(listenerTarget))
            return;

        if (!audioDataMap.TryGetValue(key, out AudioData data))
            return;

        if (data == null || data.clip == null)
            return;

        string loopId = GetLoopId(ownerNetId, key);

        // 이미 같은 루프가 재생 중이면 중복 생성하지 않는다.
        if (loopAudioMap.ContainsKey(loopId))
            return;

        GameObject loopObject = new GameObject("Loop_Audio_" + key + "_" + ownerNetId);
        loopObject.transform.position = worldPosition;

        AudioSource source = loopObject.AddComponent<AudioSource>();
        source.clip = data.clip;
        source.volume = data.volume;
        source.loop = true;
        source.playOnAwake = false;

        if (dimension == AudioDimension.Sound3D)
        {
            source.spatialBlend = 1f;
            source.minDistance = data.minDistance;
            source.maxDistance = data.maxDistance;
            source.rolloffMode = AudioRolloffMode.Linear;
        }
        else
        {
            source.spatialBlend = 0f;
        }

        source.Play();

        loopAudioMap.Add(loopId, loopObject);
    }

    // 루프 사운드 종료
    public void StopLoopAudio(uint ownerNetId, AudioKey key)
    {
        string loopId = GetLoopId(ownerNetId, key);

        if (!loopAudioMap.TryGetValue(loopId, out GameObject loopObject))
            return;

        loopAudioMap.Remove(loopId);

        if (loopObject != null)
            Destroy(loopObject);
    }

    private string GetLoopId(uint ownerNetId, AudioKey key)
    {
        return ownerNetId + "_" + key;
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
        if (localPlayerObject == null)
            return false;

        if (listenerTarget == AudioListenerTarget.KillerOnly)
            return localPlayerObject.CompareTag("Killer");

        if (listenerTarget == AudioListenerTarget.SurvivorOnly)
            return localPlayerObject.CompareTag("Survivor");

        return false;
    }

    // 로컬 클라이언트에서만 바로 소리를 재생할 때 쓰는 편의 함수
    // 예: 버튼 클릭음, UI 효과음, 내 전용 경고음
    public static void PlayLocalAudio(
        AudioKey key,
        AudioDimension dimension = AudioDimension.Sound2D,
        Vector3? worldPosition = null)
    {
        if (AudioManager.Instance == null)
            return;

        Vector3 playPosition = worldPosition ?? Vector3.zero;

        AudioManager.Instance.PlayAudio(
            key,
            AudioListenerTarget.LocalOnly,
            dimension,
            playPosition
        );
    }
}