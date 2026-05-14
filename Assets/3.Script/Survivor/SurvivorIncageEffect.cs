using Mirror;
using UnityEngine;
using Unity.Cinemachine; // 유니티 6 시네머신
using System.Collections;

public class SurvivorIncageEffect : NetworkBehaviour
{
    private SurvivorInput survivorInput;
    private CinemachineCamera normalCam;
    private Transform originalLookAt;

    private void Awake()
    {
        // 생존자 프리팹 내부에 있는 컴포넌트들을 가져옵니다.
        survivorInput = GetComponent<SurvivorInput>();
        normalCam = GetComponentInChildren<CinemachineCamera>();
    }

    [TargetRpc]
    public void TargetPlayIncageEffect(NetworkConnection target, GameObject killerObj)
    {
        if (killerObj == null) return;

        StopAllCoroutines();
        StartCoroutine(Step1_LookAtKillerRoutine(killerObj));
    }

    private IEnumerator Step1_LookAtKillerRoutine(GameObject killerObj)
    {
        Debug.Log("[Incage 1단계] 연출 시작: 조작을 잠그고 살인마를 바라봅니다.");

        // 1. 플레이어 조작 차단 (이동 및 마우스 카메라 회전 정지)
        // SurvivorMove.cs가 SurvivorInput을 참조하므로, 이것만 꺼도 움직임과 회전이 모두 멈춥니다.
        if (survivorInput != null)
        {
            survivorInput.enabled = false;
        }

        // 2. NormalCam의 시야를 살인마 쪽으로 강제 회전
        if (normalCam != null)
        {
            // 나중에 복구할 수 있도록 원래 타겟을 저장해둡니다.
            originalLookAt = normalCam.LookAt;

            // 시네머신 카메라의 타겟을 살인마로 변경합니다.
            // (살인마 모델링의 중심을 바라보게 됩니다)
            normalCam.LookAt = killerObj.transform;
        }

        // 임시 대기 (현재는 1단계 확인용이므로 3초 뒤에 원상 복구 시켜서 테스트해볼 수 있게 합니다)
        yield return new WaitForSeconds(3f);

        // --- 테스트용 복구 로직 (1단계 확인용) ---
        if (normalCam != null) normalCam.LookAt = originalLookAt;
        if (survivorInput != null) survivorInput.enabled = true;

        Debug.Log("[Incage 1단계] 테스트 종료: 다시 조작 가능해집니다.");
    }
}