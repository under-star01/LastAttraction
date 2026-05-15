using UnityEngine;

// 씬이 시작될 때 지정한 BGM을 자동으로 재생한다.
// 타이틀 씬에는 TitleBGM, 로비 씬에는 LobbyBGM을 넣으면 된다.
public class SceneBGMPlayer : MonoBehaviour
{
    [Header("재생할 BGM")]
    [SerializeField] private AudioKey bgmKey = AudioKey.None;

    [Header("오브젝트가 꺼질 때 BGM 정지")]
    [SerializeField] private bool stopOnDisable = false;

    private void Start()
    {
        Play();
    }

    public void Play()
    {
        if (bgmKey == AudioKey.None)
            return;

        AudioManager.PlayLocalBGM(bgmKey);
    }

    public void Stop()
    {
        AudioManager.StopLocalBGM();
    }

    private void OnDisable()
    {
        if (!stopOnDisable)
            return;

        Stop();
    }
}