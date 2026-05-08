using Mirror;
using UnityEngine;

public class TerrorRadius : MonoBehaviour
{
    [Header("AudioSource")]
    [SerializeField] private AudioSource ambientSource; // 32m 밖 배경음 / 바람소리
    [SerializeField] private AudioSource range1Source;  // 32m 단계 음악
    [SerializeField] private AudioSource range2Source;  // 16m 단계 음악
    [SerializeField] private AudioSource range3Source;  // 8m 단계 음악

    [Header("음악 최대 볼륨")]
    [SerializeField] private float ambientMaxVolume = 0.15f; // 32m 밖 배경음 최대 볼륨
    [SerializeField] private float range1MaxVolume = 0.2f;   // 32m 음악 최대 볼륨
    [SerializeField] private float range2MaxVolume = 0.3f;   // 16m 음악 최대 볼륨
    [SerializeField] private float range3MaxVolume = 0.4f;   // 8m 음악 최대 볼륨

    [Header("심장소리")]
    [SerializeField] private AudioSource heartbeatSource; // 두근 소리 재생용
    [SerializeField] private AudioClip heartbeatClip;     // 두근 1번짜리 클립
    [SerializeField] private float heartbeatVolume = 0.5f;

    [Header("거리 단계")]
    [SerializeField] private float range1 = 32f; // 바깥 단계
    [SerializeField] private float range2 = 16f; // 중간 단계
    [SerializeField] private float range3 = 8f;  // 가까운 단계

    [Header("음악 전환")]
    [SerializeField] private float musicFadeSpeed = 3f;

    [Header("심장소리 간격")]
    [SerializeField] private float heartbeatInterval1 = 1.2f;  // 32m 이내
    [SerializeField] private float heartbeatInterval2 = 0.85f; // 16m 이내
    [SerializeField] private float heartbeatInterval3 = 0.55f; // 8m 이내

    [Header("탐색")]
    [SerializeField] private float findInterval = 1f;

    private Transform localPlayer;
    private Transform killer;

    private float nextFindTime;
    private float heartbeatTimer;

    private float range1Sqr;
    private float range2Sqr;
    private float range3Sqr;

    private float ambientTarget;
    private float range1Target;
    private float range2Target;
    private float range3Target;

    private void Awake()
    {
        UpdateRangeSqr();
    }

    private void Start()
    {
        FindLocalPlayer();
        FindKiller();

        StartMusicLoop(ambientSource);
        StartMusicLoop(range1Source);
        StartMusicLoop(range2Source);
        StartMusicLoop(range3Source);

        SetupHeartbeatSource();
    }

    private void Update()
    {
        if (localPlayer == null)
            FindLocalPlayer();

        if (killer == null && Time.time >= nextFindTime)
        {
            nextFindTime = Time.time + findInterval;
            FindKiller();
        }

        // 로컬 플레이어 또는 킬러를 못 찾으면 모든 사운드를 천천히 줄인다.
        if (localPlayer == null || killer == null)
        {
            SetMusicTargets(0f, 0f, 0f, 0f);
            UpdateMusicVolumes();
            heartbeatTimer = 0f;
            return;
        }

        // 생존자 로컬 플레이어에게만 Terror Radius 음악을 들려준다.
        if (!localPlayer.CompareTag("Survivor"))
        {
            SetMusicTargets(0f, 0f, 0f, 0f);
            UpdateMusicVolumes();
            heartbeatTimer = 0f;
            return;
        }

        float sqrDistance = (localPlayer.position - killer.position).sqrMagnitude;

        UpdateMusic(sqrDistance);
        UpdateMusicVolumes();

        UpdateHeartbeat(sqrDistance);
    }

    private void FindLocalPlayer()
    {
        if (NetworkClient.localPlayer != null)
            localPlayer = NetworkClient.localPlayer.transform;
    }

    private void FindKiller()
    {
        KillerState[] killers = FindObjectsByType<KillerState>(FindObjectsSortMode.None);

        for (int i = 0; i < killers.Length; i++)
        {
            if (killers[i] == null)
                continue;

            killer = killers[i].transform;
            return;
        }
    }

    private void StartMusicLoop(AudioSource source)
    {
        if (source == null)
            return;

        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = 0f;

        if (!source.isPlaying)
            source.Play();
    }

    private void SetupHeartbeatSource()
    {
        if (heartbeatSource == null)
            return;

        heartbeatSource.loop = false;
        heartbeatSource.playOnAwake = false;
        heartbeatSource.spatialBlend = 0f;
    }

    // 32m 밖 : 배경음 / 바람소리
    // 32~16 : 1단계 음악만 점점 커짐
    // 16~8  : 2단계 음악만 점점 커짐
    // 8 이내 : 3단계 음악 최대
    private void UpdateMusic(float sqrDistance)
    {
        SetMusicTargets(0f, 0f, 0f, 0f);

        // 32m 밖이면 긴장 음악은 꺼지고 배경음만 켜진다.
        if (sqrDistance > range1Sqr)
        {
            ambientTarget = 1f;
            return;
        }

        float distance = Mathf.Sqrt(sqrDistance);

        if (sqrDistance <= range3Sqr)
        {
            range3Target = 1f;
            return;
        }

        if (sqrDistance <= range2Sqr)
        {
            // 16m일 때 0, 8m일 때 1
            range2Target = 1f - Mathf.InverseLerp(range2, range3, distance);
            return;
        }

        // 32m일 때 0, 16m일 때 1
        range1Target = 1f - Mathf.InverseLerp(range1, range2, distance);
    }

    private void SetMusicTargets(float ambient, float value1, float value2, float value3)
    {
        ambientTarget = ambient;
        range1Target = value1;
        range2Target = value2;
        range3Target = value3;
    }

    private void UpdateMusicVolumes()
    {
        FadeMusic(ambientSource, ambientTarget * ambientMaxVolume);
        FadeMusic(range1Source, range1Target * range1MaxVolume);
        FadeMusic(range2Source, range2Target * range2MaxVolume);
        FadeMusic(range3Source, range3Target * range3MaxVolume);
    }

    private void FadeMusic(AudioSource source, float targetVolume)
    {
        if (source == null)
            return;

        source.volume = Mathf.Lerp(source.volume, targetVolume, Time.deltaTime * musicFadeSpeed);
    }

    private void UpdateHeartbeat(float sqrDistance)
    {
        float interval = GetHeartbeatInterval(sqrDistance);

        if (interval <= 0f)
        {
            heartbeatTimer = 0f;
            return;
        }

        heartbeatTimer -= Time.deltaTime;

        if (heartbeatTimer <= 0f)
        {
            PlayHeartbeat();
            heartbeatTimer = interval;
        }
    }

    private float GetHeartbeatInterval(float sqrDistance)
    {
        if (sqrDistance > range1Sqr)
            return 0f;

        if (sqrDistance <= range3Sqr)
            return heartbeatInterval3;

        if (sqrDistance <= range2Sqr)
            return heartbeatInterval2;

        return heartbeatInterval1;
    }

    private void PlayHeartbeat()
    {
        if (heartbeatSource == null)
            return;

        if (heartbeatClip == null)
            return;

        heartbeatSource.PlayOneShot(heartbeatClip, heartbeatVolume);
    }

    private void UpdateRangeSqr()
    {
        range1Sqr = range1 * range1;
        range2Sqr = range2 * range2;
        range3Sqr = range3 * range3;
    }

    private void OnValidate()
    {
        if (range1 < 0f) range1 = 0f;
        if (range2 < 0f) range2 = 0f;
        if (range3 < 0f) range3 = 0f;

        if (range2 > range1) range2 = range1;
        if (range3 > range2) range3 = range2;

        if (ambientMaxVolume < 0f) ambientMaxVolume = 0f;
        if (range1MaxVolume < 0f) range1MaxVolume = 0f;
        if (range2MaxVolume < 0f) range2MaxVolume = 0f;
        if (range3MaxVolume < 0f) range3MaxVolume = 0f;
        if (heartbeatVolume < 0f) heartbeatVolume = 0f;

        UpdateRangeSqr();
    }
}