using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class KillerRageDetector : NetworkBehaviour
{
    [Header("탐지 설정")]
    public float detectRadius = 20f;
    public float updateInterval = 0.15f; // 매 프레임이 아닌 주기적으로 검사
    public LayerMask survivorLayer;
    public LayerMask obstacleLayer; // 벽/장애물 판정용

    private bool isActive = false;
    private float timer = 0f;

    // 현재 시각효과가 적용된 생존자 추적 (정리용)
    private HashSet<SurvivorVisualEffect> activeEffects = new();

    public void SetActive(bool value)
    {
        isActive = value;
        if (!value) ClearAllEffects();
    }

    private void Update()
    {
        // 킬러 로컬 클라이언트에서만 실행 (시각효과는 킬러 화면에만 필요)
        if (!isLocalPlayer || !isActive) return;

        timer += Time.deltaTime;
        if (timer < updateInterval) return;
        timer = 0f;

        DetectSurvivors();
    }

    private void DetectSurvivors()
    {
        HashSet<SurvivorVisualEffect> detectedThisFrame = new();

        Collider[] hits = Physics.OverlapSphere(
            transform.position, detectRadius, survivorLayer);

        foreach (var hit in hits)
        {
            SurvivorVisualEffect vfx =
                hit.GetComponentInParent<SurvivorVisualEffect>();
            if (vfx == null) continue;

            bool hasLOS = CheckLineOfSight(hit.transform.position);
            vfx.SetDetected(hasLOS);

            detectedThisFrame.Add(vfx);
            activeEffects.Add(vfx);
        }

        // 이번 프레임에 탐지 안 된 생존자 효과 제거
        foreach (var vfx in activeEffects)
        {
            if (!detectedThisFrame.Contains(vfx))
                vfx.SetUndetected();
        }
        activeEffects = detectedThisFrame;
    }

    private bool CheckLineOfSight(Vector3 targetPos)
    {
        Vector3 origin = transform.position + Vector3.up * 1.5f;
        Vector3 target = targetPos + Vector3.up * 1.0f;
        Vector3 dir = target - origin;

        // 장애물에 막히면 LOS 없음
        return !Physics.Raycast(origin, dir.normalized,
            dir.magnitude, obstacleLayer);
    }

    private void ClearAllEffects()
    {
        foreach (var vfx in activeEffects)
            vfx?.SetUndetected();
        activeEffects.Clear();
    }
}