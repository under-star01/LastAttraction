using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class KillerDetector : NetworkBehaviour
{
    [Header("탐지 설정")]
    public float detectRadius = 20f;
    public float updateInterval = 0.15f; // 매 프레임이 아닌 주기적으로 검사
    public LayerMask survivorLayer;
    public LayerMask obstacleLayer; // 벽/장애물 판정용

    [Header("증거물(상자) 설정")]
    public string boxDefaultLayer = "Interactable";      // 아웃라인 꺼짐
    public string boxSilhouetteLayer = "BoxOutline"; // 아웃라인 켜짐 (해당 레이어 필요)
    public string boxTag = "Evidence";           // 상자에 설정된 태그

    private int _boxDefaultLayerInt;
    private int _boxSilhouetteLayerInt;
    private List<GameObject> evidenceBoxes = new();

    //private bool isActive = false;
    private float timer = 0f;

    // 현재 시각효과가 적용된 생존자 추적 (정리용)
    private HashSet<SurvivorVisualEffect> activeEffects = new();

    private KillerState killerState;

    private void Awake()
    {
        killerState = GetComponent<KillerState>();
        _boxDefaultLayerInt = LayerMask.NameToLayer(boxDefaultLayer);
        _boxSilhouetteLayerInt = LayerMask.NameToLayer(boxSilhouetteLayer);
    }

    private void Update()
    {
        if (!isLocalPlayer || killerState == null) return;

        timer += Time.deltaTime;
        if (timer < updateInterval) return;
        timer = 0f;

        // 1. 상자 아웃라인 처리 (분노와 상관없이 상시 실행)
        HandleBoxOutlines();

        // 2. 생존자 감지 처리 (분노 상태일 때만 실행)
        if (killerState.IsRaging)
        {
            DetectSurvivors();
        }
        else if (activeEffects.Count > 0)
        {
            ClearAllEffects();
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        // 맵에 있는 모든 상자를 미리 찾아둠 (5개)
        GameObject[] boxes = GameObject.FindGameObjectsWithTag(boxTag);
        evidenceBoxes.AddRange(boxes);
    }

    private void HandleBoxOutlines()
    {
        foreach (var box in evidenceBoxes)
        {
            if (box == null) continue;

            float dist = Vector3.Distance(transform.position, box.transform.position);

            // 킬러가 범위 내에 있으면 아웃라인 끔(Default), 멀어지면 켬(Silhouette)
            int targetLayer = (dist <= detectRadius) ? _boxDefaultLayerInt : _boxSilhouetteLayerInt;

            if (box.layer != targetLayer)
            {
                SetLayerRecursive(box, targetLayer);
            }
        }
    }

    public void SetActive(bool value)
    {
        //isActive = value;
        //Debug.Log($"[RageDetector] SetActive({value}) 호출됨");
        if (!value) ClearAllEffects();
    }

    private void DetectSurvivors()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, detectRadius, survivorLayer);

        //Debug.Log($"[RageDetector] OverlapSphere 감지 수: {hits.Length} / 위치: {transform.position}");

        HashSet<SurvivorVisualEffect> detectedThisFrame = new();

        foreach (var hit in hits)
        {
            //Debug.Log($"[RageDetector] 감지된 콜라이더: {hit.gameObject.name}");

            SurvivorVisualEffect vfx = hit.GetComponentInParent<SurvivorVisualEffect>()
                        ?? hit.GetComponentInChildren<SurvivorVisualEffect>();
            //Debug.Log($"[RageDetector] SurvivorVisualEffect 존재 여부: {vfx != null}");

            if (vfx == null) continue;

            bool hasLOS = CheckLineOfSight(hit.transform.position);
            //Debug.Log($"[RageDetector] LOS 결과: {hasLOS}");
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
        return !Physics.Raycast(origin, dir.normalized, dir.magnitude, obstacleLayer);
    }

    private void ClearAllEffects()
    {
        foreach (var vfx in activeEffects)
            vfx?.SetUndetected();
        activeEffects.Clear();
    }

    private void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, layer);
        }
    }

}