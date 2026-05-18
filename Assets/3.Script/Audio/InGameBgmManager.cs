using Mirror;
using UnityEngine;

// 인게임 전체 BGM 관리 매니저
// - 생존자 공포 반경 BGM
// - 생존자 32m 밖 ambient BGM
// - 살인마 기본 ambient BGM
// - 살인마 Rage 상태 BGM
// - 생존자 심장소리
public class InGameBgmManager : MonoBehaviour
{
    [Header("공통 / 생존자 공포 반경 AudioSource")]
    [SerializeField] private AudioSource ambientSource; // 생존자 32m 밖 배경음 / 살인마 기본 배경음
    [SerializeField] private AudioSource range1Source;  // 생존자 32m 단계 음악
    [SerializeField] private AudioSource range2Source;  // 생존자 16m 단계 음악
    [SerializeField] private AudioSource range3Source;  // 생존자 8m 단계 음악

    [Header("살인마 전용 AudioSource")]
    [SerializeField] private AudioSource killerRageSource; // 살인마 Rage 상태 BGM

    [Header("음악 최대 볼륨")]
    [SerializeField] private float ambientMaxVolume = 0.15f;    // 32m 밖 ambient / 살인마 기본 배경음 최대 볼륨
    [SerializeField] private float range1MaxVolume = 0.2f;      // 32m 음악 최대 볼륨
    [SerializeField] private float range2MaxVolume = 0.3f;      // 16m 음악 최대 볼륨
    [SerializeField] private float range3MaxVolume = 0.4f;      // 8m 음악 최대 볼륨
    [SerializeField] private float killerRageMaxVolume = 0.55f; // 살인마 Rage BGM 최대 볼륨

    [Header("심장소리")]
    [SerializeField] private AudioSource heartbeatSource; // 생존자 심장소리 재생용
    [SerializeField] private AudioClip heartbeatClip;     // 두근 1번짜리 클립
    [SerializeField] private float heartbeatVolume = 0.5f;

    [Header("거리 단계")]
    [SerializeField] private float range1 = 32f; // 공포 반경 바깥 기준
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
    private KillerState killerState;

    private float nextFindTime;
    private float heartbeatTimer;

    private float range1Sqr;
    private float range2Sqr;
    private float range3Sqr;

    private float ambientTarget;
    private float range1Target;
    private float range2Target;
    private float range3Target;
    private float killerRageTarget;

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
        StartMusicLoop(killerRageSource);

        SetupHeartbeatSource();
    }

    private void Update()
    {
        if (localPlayer == null)
            FindLocalPlayer();

        if ((killer == null || killerState == null) && Time.time >= nextFindTime)
        {
            nextFindTime = Time.time + findInterval;
            FindKiller();
        }

        if (localPlayer == null || killer == null || killerState == null)
        {
            StopAllTargets();
            UpdateMusicVolumes();
            heartbeatTimer = 0f;
            return;
        }

        if (localPlayer.CompareTag("Killer"))
        {
            UpdateKillerBgm();
            UpdateMusicVolumes();
            heartbeatTimer = 0f;
            return;
        }

        if (localPlayer.CompareTag("Survivor"))
        {
            float sqrDistance = (localPlayer.position - killer.position).sqrMagnitude;

            UpdateSurvivorBgm(sqrDistance);
            UpdateMusicVolumes();
            UpdateHeartbeat(sqrDistance);
            return;
        }

        StopAllTargets();
        UpdateMusicVolumes();
        heartbeatTimer = 0f;
    }

    private void FindLocalPlayer()
    {
        if (NetworkClient.localPlayer == null)
            return;

        localPlayer = NetworkClient.localPlayer.transform;
    }

    private void FindKiller()
    {
        KillerState[] killers = FindObjectsByType<KillerState>(FindObjectsSortMode.None);

        for (int i = 0; i < killers.Length; i++)
        {
            if (killers[i] == null)
                continue;

            killerState = killers[i];
            killer = killers[i].transform;
            return;
        }

        killerState = null;
        killer = null;
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

    // 살인마가 듣는 BGM
    // - 평상시: 생존자가 32m 밖에서 듣는 ambientSource와 같은 소리
    // - Rage: ambientSource를 끄고 killerRageSource만 재생
    // - Lobby: 전부 꺼짐
    private void UpdateKillerBgm()
    {
        StopAllTargets();

        if (killerState == null)
            return;

        if (killerState.CurrentCondition == KillerCondition.Lobby)
            return;

        if (killerState.IsRaging)
        {
            killerRageTarget = 1f;
            return;
        }

        ambientTarget = 1f;
    }

    // 생존자가 듣는 BGM
    // - 32m 밖: ambientSource
    // - 32m 안: range1 / range2 / range3 단계 음악
    // - Rage BGM은 살인마 전용이므로 생존자에게는 재생하지 않음
    private void UpdateSurvivorBgm(float sqrDistance)
    {
        StopAllTargets();

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

    private void StopAllTargets()
    {
        ambientTarget = 0f;
        range1Target = 0f;
        range2Target = 0f;
        range3Target = 0f;
        killerRageTarget = 0f;
    }

    private void UpdateMusicVolumes()
    {
        FadeMusic(ambientSource, ambientTarget * ambientMaxVolume);
        FadeMusic(range1Source, range1Target * range1MaxVolume);
        FadeMusic(range2Source, range2Target * range2MaxVolume);
        FadeMusic(range3Source, range3Target * range3MaxVolume);
        FadeMusic(killerRageSource, killerRageTarget * killerRageMaxVolume);
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
        if (killerRageMaxVolume < 0f) killerRageMaxVolume = 0f;
        if (heartbeatVolume < 0f) heartbeatVolume = 0f;

        UpdateRangeSqr();
    }
}