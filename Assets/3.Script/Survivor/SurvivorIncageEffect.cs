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

        Vector3 targetPos = killerObj.transform.position + Vector3.up * 1.6f;

        Vector3 lookDir = (targetPos - transform.position).normalized;
        lookDir.y = 0;
        if (lookDir != Vector3.zero) transform.rotation = Quaternion.LookRotation(lookDir);
        yield return new WaitForEndOfFrame();

        // 2. 카메라 및 레이어 설정
        if (camSkill != null) camSkill.ApplyIncageView(true);

        // 인케이지 전용 카메라 컴포넌트 찾아오기 (줌 조절용)
        // SurvivorCameraSkill에 선언된 incageCinemachine을 가져오거나 직접 할당받아야 합니다.
        incageCam = GetComponentInChildren<CinemachineCamera>();

        int cinematicLayer = LayerMask.NameToLayer(cinematicLayerName);
        if (mainCamera != null)
        {
            originalCullingMask = mainCamera.cullingMask;
            mainCamera.cullingMask = 1 << cinematicLayer;
        }

        // 3. 안개 및 살인마 생성
        if (fogParticles != null)
        {
            SetLayerRecursive(fogParticles.gameObject, cinematicLayer);
            fogParticles.Play();
        }

        Vector3 startPos = transform.position + transform.forward * initialDistance;
        spawnedMori = Instantiate(partialMoriPrefab, startPos, transform.rotation);
        spawnedMori.transform.localScale = Vector3.one * 3f;
        SetLayerRecursive(spawnedMori, cinematicLayer);

        // 4. [핵심] 가속 접근 및 줌인 연출
        float elapsed = 0f;
        while (elapsed < effectDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / effectDuration;

            // 가속도 계산 (점점 빨라지게 - 쿼드라틱 이징)
            float accelerationT = t * t * t;

            // 살인마 위치 업데이트
            float currentDist = Mathf.Lerp(initialDistance, targetDistance, accelerationT);
            spawnedMori.transform.position = transform.position + transform.forward * currentDist;

            // 카메라 FOV 업데이트 (점점 줌인)
            if (incageCam != null)
            {
                incageCam.Lens.FieldOfView = Mathf.Lerp(initialFOV, targetFOV, accelerationT);
            }

            yield return null;
        }

        // 5. 암전 연출 시작 (ChangeSceneUI 활용)
        if (ChangeSceneUI.Instance != null)
        {
            ChangeSceneUI.Instance.Show(true);
        }

        // Fade 시간만큼 대기 (기본 1초)
        yield return new WaitForSeconds(1.0f);

        // 6. 위치 이동 및 복구
        transform.position = cagePosition; // 서버로부터 전달받은 감옥 위치로 이동

        CleanupStep3();

        // 7. 암전 해제
        if (ChangeSceneUI.Instance != null)
        {
            ChangeSceneUI.Instance.Show(false);
        }
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