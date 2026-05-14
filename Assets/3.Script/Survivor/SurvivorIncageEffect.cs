using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal; // URP 포스트 프로세싱
using Unity.Cinemachine;             // 시네머신 카메라
using System.Collections;

public class SurvivorIncageEffect : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private CinemachineCamera normalCinemachine; // 생존자의 기본 시네머신 가상 카메라
    [SerializeField] private Volume postProcessVolume;          // 씬에 배치된 글로벌 포스트 프로세싱 볼륨

    [Header("연출 설정")]
    [SerializeField] private float fovZoom = 25f;               // 얼굴 확대 시 FOV 값 (매우 낮을수록 확대됨)
    [SerializeField] private float fadeDuration = 0.8f;         // 어두워지는 속도 (단위: 초)
    [SerializeField] private float targetVignetteIntensity = 0.9f; // 비네팅(화면 어두워짐) 최종 강도 (0~1)

    private Vignette vignette;
    private float originalFOV;

    private void Awake()
    {
        // [수정] 인스펙터에서 넣지 않아도 씬에서 자동으로 Volume을 찾습니다.
        if (postProcessVolume == null)
        {
            postProcessVolume = GameObject.FindAnyObjectByType<Volume>();
        }

        if (postProcessVolume != null && postProcessVolume.profile.TryGet(out vignette))
        {
            vignette.active = false;
        }

        if (normalCinemachine != null)
            originalFOV = normalCinemachine.Lens.FieldOfView;
    }

    // ========================================================
    // [핵심] 서버가 호출하는 [TargetRpc]. 오직 이 생존자 클라이언트에서만 실행됨.
    // ========================================================
    [TargetRpc]
    public void TargetPlayIncageEffect(NetworkConnection target, GameObject killerObj)
    {
        if (killerObj == null) return;

        StopAllCoroutines();
        StartCoroutine(PlayEffectRoutine(killerObj));
    }

    private IEnumerator PlayEffectRoutine(GameObject killerObj)
    {
        if (normalCinemachine == null || vignette == null) yield break;

        // [연출 전 상태 저장] 연출 종료 후 복구를 위해 현재 상태를 저장합니다.
        Transform originalLookAt = normalCinemachine.LookAt;
        Transform originalFollow = normalCinemachine.Follow;
        float originalFOV = normalCinemachine.Lens.FieldOfView;

        Debug.Log($"<color=orange>[Incage Effect]</color> 살인마의 입(mouth)을 추적합니다.");

        // [수정된 부분] 계층 구조에서 'mouth'를 정확하게 찾습니다.
        Transform mouthTarget = null;
        Transform[] allChildren = killerObj.GetComponentsInChildren<Transform>();
        foreach (var child in allChildren)
        {
            // 대소문자 구분 없이 'mouth'라는 이름의 오브젝트를 찾습니다.
            if (child.name.ToLower().Equals("mouth"))
            {
                mouthTarget = child;
                break;
            }
        }

        // mouth를 못 찾을 경우를 대비한 예외 처리 (Head나 루트라도 잡음)
        if (mouthTarget == null)
        {
            mouthTarget = killerObj.transform.Find("Armature/Hips/Spine/Chest/Neck/Head") ?? killerObj.transform;
        }

        // [연출 시작] 카메라 타겟을 살인마의 입으로 변경 및 줌인
        normalCinemachine.LookAt = mouthTarget;
        normalCinemachine.Follow = mouthTarget;

        // FOV를 낮춰서 입 부분이 화면에 가득 차게 만듭니다. (잡아먹히는 느낌)
        normalCinemachine.Lens.FieldOfView = fovZoom;

        // [화면 어두워짐] 비네팅 효과 활성화 및 페이드
        vignette.active = true;
        float elapsed = 0f;
        float originalVignetteIntensity = vignette.intensity.value;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            vignette.intensity.value = Mathf.Lerp(originalVignetteIntensity, targetVignetteIntensity, elapsed / fadeDuration);
            yield return null;
        }
        vignette.intensity.value = targetVignetteIntensity;

        // [유지] 킬러의 IncageRoutineServer 대기 시간(2.1초) 동안 연출 유지
        // 만약 3~5초를 원하신다면 KillerInteractor의 yield return 시간과 이 시간을 함께 늘려야 싱크가 맞습니다.
        yield return new WaitForSeconds(2.1f);

        // [복구] 카메라 타겟 및 FOV 원상복구
        normalCinemachine.LookAt = originalLookAt;
        normalCinemachine.Follow = originalFollow;
        normalCinemachine.Lens.FieldOfView = originalFOV;

        // [화면 밝아짐] 비네팅 서서히 제거
        elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            vignette.intensity.value = Mathf.Lerp(targetVignetteIntensity, originalVignetteIntensity, elapsed / fadeDuration);
            yield return null;
        }

        vignette.intensity.value = originalVignetteIntensity;
        vignette.active = false;

        Debug.Log($"<color=orange>[Incage Effect]</color> 연출 종료");
    }
}