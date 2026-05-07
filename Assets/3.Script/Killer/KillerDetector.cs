using UnityEngine;
using Mirror;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

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
    private bool _isBoxFound = false; // 박스를 찾았는지 확인하는 플래그

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

        if (_boxDefaultLayerInt == -1 || _boxSilhouetteLayerInt == -1)
        {
            Debug.LogError($"[KillerDetector] 레이어 설정 오류! Default: {_boxDefaultLayerInt}, Outline: {_boxSilhouetteLayerInt}");
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // [추가] 씬 로드 이벤트 해제
    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // [추가] 씬이 바뀔 때마다 실행되는 함수
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 인게임 씬에 진입했을 때 (씬 이름이 "InGame"이라고 가정)
        if (scene.name == "InGame")
        {
            _isBoxFound = false; // 플래그 리셋
            evidenceBoxes.Clear();
            // 여기서 바로 찾지 않는 이유는 박스가 씬 로드 직후 1프레임 뒤에 생성될 수도 있기 때문입니다.
            // 실제 검색은 Update의 TryFindBoxes에서 안전하게 진행됩니다.
        }
    }

    private void Update()
    {
        if (!isLocalPlayer || killerState == null) return;

        timer += Time.deltaTime;
        if (timer < updateInterval) return;
        timer = 0f;

        // 인게임 씬인데 아직 박스를 못 찾았다면 검색 시도
        if (!_isBoxFound && SceneManager.GetActiveScene().name == "InGame")
        {
            TryFindBoxes();
            return;
        }

        HandleBoxOutlines();

        if (killerState.IsRaging) DetectSurvivors();
        else if (activeEffects.Count > 0) ClearAllEffects();
    }

    private void TryFindBoxes()
    {
        GameObject[] boxes = GameObject.FindGameObjectsWithTag(boxTag);
        if (boxes.Length > 0)
        {
            evidenceBoxes.AddRange(boxes);
            _isBoxFound = true;
            Debug.Log($"[KillerDetector] {boxes.Length}개의 증거물 검색 완료 (InGame 씬)");
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        // 맵에 있는 모든 상자를 미리 찾아둠 (5개)
        GameObject[] boxes = GameObject.FindGameObjectsWithTag(boxTag);

        // [로그 추가] 박스를 몇 개 찾았는지 확인
        Debug.Log($"[KillerDetector] 찾은 {boxTag} 태그 오브젝트 개수: {boxes.Length}");

        foreach (var b in boxes)
        {
            Debug.Log($"[KillerDetector] 찾은 상자 이름: {b.name}");
        }

        evidenceBoxes.Clear();
        evidenceBoxes.AddRange(boxes);
    }

    private void HandleBoxOutlines()
    {
        if (!_isBoxFound) return;

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
        if (obj == null) return;

        obj.layer = layer;

        // 자식들을 순회하며 재귀 호출
        for (int i = 0; i < obj.transform.childCount; i++)
        {
            SetLayerRecursive(obj.transform.GetChild(i).gameObject, layer);
        }
    }

}