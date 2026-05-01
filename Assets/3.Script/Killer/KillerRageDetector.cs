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

    private KillerState killerState;

    private void Awake()
    {
        killerState = GetComponent<KillerState>();
    }

    private void Update()
    {
        if (!isLocalPlayer || killerState == null || !killerState.IsRaging)
        {
            // 분노가 끝났을 때 한 번만 효과를 정리하도록 로직 추가 가능
            if (activeEffects.Count > 0) ClearAllEffects();
            return;
        }

        timer += Time.deltaTime;
        if (timer < updateInterval) return;
        timer = 0f;

        DetectSurvivors();
    }

    public void SetActive(bool value)
    {
        isActive = value;
        Debug.Log($"[RageDetector] SetActive({value}) 호출됨");
        if (!value) ClearAllEffects();
    }

    private void DetectSurvivors()
    {
        Collider[] hits = Physics.OverlapSphere(
        transform.position, detectRadius, survivorLayer);

        Debug.Log($"[RageDetector] OverlapSphere 감지 수: {hits.Length} / 위치: {transform.position}");

        HashSet<SurvivorVisualEffect> detectedThisFrame = new();

        foreach (var hit in hits)
        {
            Debug.Log($"[RageDetector] 감지된 콜라이더: {hit.gameObject.name}");

            SurvivorVisualEffect vfx = hit.GetComponentInParent<SurvivorVisualEffect>()
                        ?? hit.GetComponentInChildren<SurvivorVisualEffect>();
            Debug.Log($"[RageDetector] SurvivorVisualEffect 존재 여부: {vfx != null}");

            if (vfx == null) continue;

            bool hasLOS = CheckLineOfSight(hit.transform.position);
            Debug.Log($"[RageDetector] LOS 결과: {hasLOS}");
            vfx.SetDetected(hasLOS);

            detectedThisFrame.Add(vfx);
            activeEffects.Add(vfx);
        }

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