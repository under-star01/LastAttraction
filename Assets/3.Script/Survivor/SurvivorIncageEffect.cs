using Mirror;
using UnityEngine;
using Unity.Cinemachine;
using System.Collections;

public class SurvivorIncageEffect : NetworkBehaviour
{
    [Header("연출 설정")]
    [SerializeField] private GameObject partialMoriPrefab;
    [SerializeField] private ParticleSystem fogParticles;
    [SerializeField] private string cinematicLayerName = "Cinematic";

    [Header("3단계: 접근 및 줌 수치")]
    [SerializeField] private float initialDistance = 8f;     // 시작 거리 (멀리서 시작)
    [SerializeField] private float targetDistance = 1.2f;    // 최종 도착 거리 (코앞)
    [SerializeField] private float initialFOV = 60f;         // 시작 시야각
    [SerializeField] private float targetFOV = 30f;          // 최종 시야각 (줌인)
    [SerializeField] private float effectDuration = 5f;      // 연출 지속 시간

    [SerializeField] private float initialLookAtHeight = 1.6f;
    [SerializeField] private float cinematicKillerYOffset = -1.0f;

    private SurvivorInput survivorInput;
    private SurvivorCameraSkill camSkill;
    private Camera mainCamera;
    private CinemachineCamera incageCam; // 인케이지 전용 캠 참조용

    private int originalCullingMask;
    private GameObject spawnedMori;

    private void Awake()
    {
        survivorInput = GetComponent<SurvivorInput>();
        camSkill = GetComponent<SurvivorCameraSkill>();
        mainCamera = Camera.main;

        if (fogParticles != null) fogParticles.Stop();
    }

    [TargetRpc]
    public void TargetPlayIncageEffect(NetworkConnection target, GameObject killerObj, Vector3 cagePosition)
    {
        if (killerObj == null) return;
        StopAllCoroutines();
        StartCoroutine(Step3_AbyssFinalRoutine(killerObj, cagePosition));
    }

    private IEnumerator Step3_AbyssFinalRoutine(GameObject killerObj, Vector3 cagePosition)
    {
        // 1. 초기화 및 살인마 바라보기
        if (survivorInput != null) survivorInput.enabled = false;

        Vector3 dirToKiller = (killerObj.transform.position - transform.position).normalized;
        dirToKiller.y = 0;

        // 생존자가 살인마를 보게 회전 (이후 다른 스크립트에 의해 회전이 변해도 dirToKiller는 유지됨)
        if (dirToKiller != Vector3.zero) transform.rotation = Quaternion.LookRotation(dirToKiller);

        yield return new WaitForSeconds(0.1f);

        // 2. 카메라 설정
        if (camSkill != null) camSkill.ApplyIncageView(true);
        if (incageCam == null) incageCam = GetComponentInChildren<CinemachineCamera>();

        // 3. 레이어 설정
        int cinematicLayer = LayerMask.NameToLayer(cinematicLayerName);
        if (mainCamera != null)
        {
            originalCullingMask = mainCamera.cullingMask;
            mainCamera.cullingMask = 1 << cinematicLayer;
        }

        if (fogParticles != null)
        {
            SetLayerRecursive(fogParticles.gameObject, cinematicLayer);
            fogParticles.Play();
        }

        // ======================================================================================
        // [분리 2] 연출용 살인마 배치 로직 (cinematicKillerYOffset 사용)
        // ======================================================================================

        // 생성 위치 계산
        Vector3 startPos = transform.position + dirToKiller * initialDistance;

        // 연출용 모델의 높이만 따로 설정
        startPos.y = transform.position.y + cinematicKillerYOffset;

        // 마주보는 회전값 계산
        Quaternion moriRotation = Quaternion.LookRotation(-dirToKiller);

        // 소환
        spawnedMori = Instantiate(partialMoriPrefab, startPos, moriRotation);
        spawnedMori.transform.localScale = Vector3.one * 3f;
        SetLayerRecursive(spawnedMori, cinematicLayer);

        // 카메라가 돌진해오는 '연출용 살인마'를 추적하도록 설정
        if (incageCam != null) incageCam.LookAt = spawnedMori.transform;

        // ======================================================================================

        // 4. 가속 접근 및 줌인
        float elapsed = 0f;
        while (elapsed < effectDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / effectDuration;
            float accelerationT = t * t * t;

            // [수정] 이동 시에도 dirToKiller를 기준으로 거리 조절
            float currentDist = Mathf.Lerp(initialDistance, targetDistance, accelerationT);
            Vector3 nextPos = transform.position + dirToKiller * currentDist;
            nextPos.y = transform.position.y + cinematicKillerYOffset;

            spawnedMori.transform.position = nextPos;

            if (incageCam != null)
                incageCam.Lens.FieldOfView = Mathf.Lerp(initialFOV, targetFOV, accelerationT);

            yield return null;
        }

        // 5. 암전 및 이동
        if (ChangeSceneUI.Instance != null) ChangeSceneUI.Instance.Show(true);
        yield return new WaitForSeconds(1.0f);

        transform.position = cagePosition;
        CleanupStep3();

        if (ChangeSceneUI.Instance != null) ChangeSceneUI.Instance.Show(false);
    }

    private void CleanupStep3()
    {
        if (camSkill != null) camSkill.ApplyIncageView(false);
        if (mainCamera != null) mainCamera.cullingMask = originalCullingMask;
        if (survivorInput != null) survivorInput.enabled = true;

        if (fogParticles != null) fogParticles.Stop();
        if (spawnedMori != null) Destroy(spawnedMori);

        // FOV 초기화 (다음 연출을 위해)
        if (incageCam != null) incageCam.Lens.FieldOfView = initialFOV;
    }

    private void SetLayerRecursive(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, newLayer);
        }
    }
}