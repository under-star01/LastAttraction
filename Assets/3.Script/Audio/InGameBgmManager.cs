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
    [SerializeField] private float ambientMaxVolume = 0.15f;
    [SerializeField] private float range1MaxVolume = 0.2f;
    [SerializeField] private float range2MaxVolume = 0.3f;
    [SerializeField] private float range3MaxVolume = 0.4f;
    [SerializeField] private float killerRageMaxVolume = 0.55f;

    [Header("심장소리")]
    [SerializeField] private AudioSource heartbeatSource;
    [SerializeField] private AudioClip heartbeatClip;
    [SerializeField] private float heartbeatVolume = 0.5f;

    [Header("거리 단계")]
    [SerializeField] private float range1 = 32f;
    [SerializeField] private float range2 = 16f;
    [SerializeField] private float range3 = 8f;

    [Header("크로스페이드 거리")]
    [SerializeField] private float crossFadeHalfRange = 2f; // 기준 거리 전후 몇 m에서 전환할지

    [Header("음악 전환")]
    [SerializeField] private float musicFadeSpeed = 3f;

    [Header("심장소리 간격")]
    [SerializeField] private float heartbeatInterval1 = 1.2f;
    [SerializeField] private float heartbeatInterval2 = 0.85f;
    [SerializeField] private float heartbeatInterval3 = 0.55f;

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
    // - 각 기준 거리 전후 crossFadeHalfRange 만큼만 크로스페이드한다.
    // - 기본값 2m 기준:
    //   34m ~ 30m : 앰비언트 -> 32m BGM
    //   18m ~ 14m : 32m BGM -> 16m BGM
    //   10m ~ 6m  : 16m BGM -> 8m BGM
    private void UpdateSurvivorBgm(float sqrDistance)
    {
        StopAllTargets();

        float distance = Mathf.Sqrt(sqrDistance);
        float fade = Mathf.Max(0.01f, crossFadeHalfRange);

        // 32m 경계보다 바깥쪽 완전 앰비언트 구간
        if (distance > range1 + fade)
        {
            ambientTarget = 1f;
            return;
        }

        // 32m 경계 전후 전환 구간
        if (distance > range1 - fade)
        {
            SetCrossFadeTarget(
                distance,
                range1 + fade,
                range1 - fade,
                out ambientTarget,
                out range1Target
            );
            return;
        }

        // 16m 경계 전까지는 32m BGM만
        if (distance > range2 + fade)
        {
            range1Target = 1f;
            return;
        }

        // 16m 경계 전후 전환 구간
        if (distance > range2 - fade)
        {
            SetCrossFadeTarget(
                distance,
                range2 + fade,
                range2 - fade,
                out range1Target,
                out range2Target
            );
            return;
        }

        // 8m 경계 전까지는 16m BGM만
        if (distance > range3 + fade)
        {
            range2Target = 1f;
            return;
        }

        // 8m 경계 전후 전환 구간
        if (distance > range3 - fade)
        {
            SetCrossFadeTarget(
                distance,
                range3 + fade,
                range3 - fade,
                out range2Target,
                out range3Target
            );
            return;
        }

        // 8m 경계보다 안쪽 완전 8m BGM 구간
        range3Target = 1f;
    }

    // outerFullDistance에서는 바깥쪽 BGM 100%
    // innerFullDistance에서는 안쪽 BGM 100%
    // 그 사이에서는 두 BGM을 자연스럽게 섞는다.
    private void SetCrossFadeTarget(
        float distance,
        float outerFullDistance,
        float innerFullDistance,
        out float outerTarget,
        out float innerTarget
    )
    {
        float t = Mathf.InverseLerp(outerFullDistance, innerFullDistance, distance);

        outerTarget = 1f - t;
        innerTarget = t;
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

        if (crossFadeHalfRange < 0.01f)
            crossFadeHalfRange = 0.01f;

        if (ambientMaxVolume < 0f) ambientMaxVolume = 0f;
        if (range1MaxVolume < 0f) range1MaxVolume = 0f;
        if (range2MaxVolume < 0f) range2MaxVolume = 0f;
        if (range3MaxVolume < 0f) range3MaxVolume = 0f;
        if (killerRageMaxVolume < 0f) killerRageMaxVolume = 0f;
        if (heartbeatVolume < 0f) heartbeatVolume = 0f;

        UpdateRangeSqr();
    }
}