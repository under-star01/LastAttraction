using UnityEngine;

// 오디오 1개에 대한 데이터
// 인스펙터에서 소리 ID와 실제 AudioClip을 연결할 때 사용
[System.Serializable]
public class AudioData
{
    [Header("오디오 KEY")]
    public AudioKey key;

    [Header("오디오 클립")]
    public AudioClip clip;

    [Header("기본 볼륨")]
    [Range(0f, 1f)]
    public float volume = 1f;
}