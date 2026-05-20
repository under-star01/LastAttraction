using System.Collections;
using Mirror;
using UnityEngine;

public class SurvivorPrisonEffect : NetworkBehaviour
{
    [Header("공포 연출")]
    [SerializeField] private Transform terrorPos;
    [SerializeField] private GameObject terrorEffectPrefab;
    [SerializeField] private string effectLayerName = "Effect";
    [SerializeField] private int blinkCount = 2;

    [Header("시간")]
    [SerializeField] private float beforeBlackoutDelay = 1f;
    [SerializeField] private float blackoutTime = 1.2f;
    [SerializeField] private float afterBlackoutDelay = 0.4f;

    [SerializeField] private float eyeOpenDuration = 0.35f;
    [SerializeField] private float eyeCloseDuration = 0.45f;
    [SerializeField] private float eyeOpenHoldTime = 0.25f;
    [SerializeField] private float eyeCloseHoldTime = 0.25f;

    private SurvivorCameraSkill camSkill;
    private SurvivorMove move;
    private SurvivorState state;

    private Camera mainCamera;
    private GameObject spawnedEffect;
    private Coroutine routine;
    private int originalCullingMask;

    private void Awake()
    {
        if (camSkill == null)
            camSkill = GetComponent<SurvivorCameraSkill>();

        if (move == null)
            move = GetComponent<SurvivorMove>();

        if (state == null)
            state = GetComponent<SurvivorState>();
    }

    [Server]
    public void BeginPrisonSequenceServer(
    Prison prison,
    AudioKey incageSoundKey,
    Vector3 incageSoundOffset
)
    {
        if (prison == null)
            return;

        if (state == null || !state.IsDowned)
            return;

        StartCoroutine(PrisonSequenceServer(prison, incageSoundKey, incageSoundOffset));
    }

    [Server]
    private IEnumerator PrisonSequenceServer(
    Prison prison,
    AudioKey incageSoundKey,
    Vector3 incageSoundOffset
)
    {
        // 1. 생존자 입력 / 이동 제한
        if (move != null)
            move.SetPrisonSequenceLock(true);

        // 2. 잡힌 생존자 클라이언트에게만 공포 연출 실행
        if (connectionToClient != null)
            TargetPlayPrisonEffect(connectionToClient);

        // 3. 공포 이펙트를 잠깐 보여준 뒤 암전되는 타이밍까지 대기
        yield return new WaitForSeconds(beforeBlackoutDelay);

        if (prison == null || state == null)
            yield break;

        // 4. 암전 중 감옥 이동 확정
        PlayIncageSound(prison.transform.position, incageSoundKey, incageSoundOffset);
        prison.SetPrisoner(state);

        // 5. 감옥에서 깨어나는 연출이 끝날 때까지 대기 후 입력 복구
        float restoreDelay =
            blackoutTime +
            afterBlackoutDelay +
            (eyeOpenDuration + eyeOpenHoldTime + eyeCloseDuration + eyeCloseHoldTime) * blinkCount +
            1f;

        yield return new WaitForSeconds(restoreDelay);

        if (move != null && state != null && !state.IsDead)
            move.SetPrisonSequenceLock(false);
    }

    [Server]
    private void PlayIncageSound(
        Vector3 prisonPosition,
        AudioKey incageSoundKey,
        Vector3 incageSoundOffset
    )
    {
        if (NetworkAudioManager.Instance == null)
            return;

        if (incageSoundKey == AudioKey.None)
            return;

        NetworkAudioManager.PlayAudioForEveryone(
            incageSoundKey,
            AudioDimension.Sound3D,
            prisonPosition + incageSoundOffset
        );
    }

    [TargetRpc]
    public void TargetPlayPrisonEffect(NetworkConnectionToClient target)
    {
        PlayPrisonEffect();
    }

    public void PlayPrisonEffect()
    {
        if (!isLocalPlayer)
            return;

        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(PrisonEffectRoutine());
    }

    private IEnumerator PrisonEffectRoutine()
    {
        if (camSkill != null)
            camSkill.ApplyPrisonView(true);
        
        AudioManager.PlayLocalAudio(AudioKey.PrisonEffect, AudioDimension.Sound2D);

        yield return new WaitForSeconds(0.1f);

        mainCamera = Camera.main;

        if (mainCamera != null)
        {
            originalCullingMask = mainCamera.cullingMask;

            int effectMask = LayerMask.GetMask(effectLayerName);

            if (effectMask != 0)
                mainCamera.cullingMask = effectMask;
            else
                Debug.LogWarning($"[SurvivorPrisonEffect] {effectLayerName} Layer를 찾지 못했습니다.");

            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.black;
        }

        SpawnTerrorEffect();
        
        // 1. PrisonCam 상태에서 공포 이펙트를 잠깐 보여줌
        yield return new WaitForSeconds(beforeBlackoutDelay);

        // 2. 즉시 암전
        if (ChangeSceneUI.Instance != null)
            ChangeSceneUI.Instance.ShowInstant(true);

        // 3. 이 시간 동안 서버에서 prison.SetPrisoner가 실행되어 감옥 위치로 이동됨
        yield return new WaitForSeconds(blackoutTime);

        // 4. 검은 화면 상태에서 공포 이펙트 제거
        CleanupEffect();

        // 5. CullingMask 복구
        if (mainCamera != null)
        {
            mainCamera.cullingMask = originalCullingMask;
            mainCamera.clearFlags = CameraClearFlags.Skybox;
        }

        // 사망했다면 깜빡임 연출은 생략하고 ResultCam 흐름에 맡김
        if (state != null && state.IsDead)
        {
            if (camSkill != null)
                camSkill.ReleasePrisonViewOnly();

            routine = null;
            yield break;
        }

        // 6. PrisonCam 상태는 유지한 채 감옥에서 깨어나기 전 잠깐 검은 화면 유지
        yield return new WaitForSeconds(afterBlackoutDelay);

        // 7. PrisonCam 상태에서 감옥 화면 기준으로 깜빡임
        yield return BlinkRoutine();

        // 8. 깜빡임이 끝난 뒤 기존 Cam으로 복구
        if (camSkill != null)
            camSkill.ApplyPrisonView(false);

        // 9. 마지막 검은 화면을 부드럽게 제거
        if (ChangeSceneUI.Instance != null)
            ChangeSceneUI.Instance.Show(false);

        routine = null;
    }

    private void SpawnTerrorEffect()
    {
        if (terrorEffectPrefab == null)
            return;

        if (spawnedEffect != null)
            Destroy(spawnedEffect);

        Vector3 spawnPos = transform.position;

        if (terrorPos != null)
            spawnPos.y = terrorPos.position.y;

        spawnedEffect = Instantiate(
            terrorEffectPrefab,
            spawnPos,
            Quaternion.identity
        );

        if (mainCamera != null)
        {
            Vector3 dir = mainCamera.transform.position - spawnedEffect.transform.position;
            dir.y = 0f;

            if (dir.sqrMagnitude > 0.001f)
                spawnedEffect.transform.rotation = Quaternion.LookRotation(dir);
        }

        int effectLayer = LayerMask.NameToLayer(effectLayerName);
        SetLayerRecursive(spawnedEffect.transform, effectLayer);
    }

    private IEnumerator BlinkRoutine()
    {
        if (ChangeSceneUI.Instance == null)
            yield break;

        for (int i = 0; i < blinkCount; i++)
        {
            // 눈을 천천히 뜨는 느낌
            ChangeSceneUI.Instance.ShowWithDuration(false, eyeOpenDuration);
            yield return new WaitForSeconds(eyeOpenDuration + eyeOpenHoldTime);

            // 다시 천천히 눈을 감는 느낌
            ChangeSceneUI.Instance.ShowWithDuration(true, eyeCloseDuration);
            yield return new WaitForSeconds(eyeCloseDuration + eyeCloseHoldTime);
        }
    }

    private void CleanupEffect()
    {
        if (spawnedEffect == null)
            return;

        Destroy(spawnedEffect);
        spawnedEffect = null;
    }

    private void SetLayerRecursive(Transform target, int layer)
    {
        if (target == null)
            return;

        if (layer < 0)
            return;

        target.gameObject.layer = layer;

        for (int i = 0; i < target.childCount; i++)
            SetLayerRecursive(target.GetChild(i), layer);
    }
}