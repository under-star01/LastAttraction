using UnityEngine;

public class SurvivorVisualEffect : MonoBehaviour
{
    [Header("레이어 설정 (Project Settings에서 생성 필요)")]
    [SerializeField] private string defaultLayer = "Survivor";
    [SerializeField] private string silhouetteLayer = "SurvivorSilhouette";

    private int _defaultLayerInt;
    private int _silhouetteLayerInt;

    public enum DetectState { None, Visible, Hidden }
    private DetectState currentState = DetectState.None;

    private void Awake()
    {
        _defaultLayerInt = LayerMask.NameToLayer(defaultLayer);
        _silhouetteLayerInt = LayerMask.NameToLayer(silhouetteLayer);
    }

    public void SetDetected(bool hasLOS)
    {
        // 시야에 보이면(hasLOS) Visible, 가려지면 Hidden
        DetectState next = hasLOS ? DetectState.Visible : DetectState.Hidden;
        if (currentState == next) return;
        currentState = next;
        ApplyEffect();
    }

    public void SetUndetected()
    {
        if (currentState == DetectState.None) return;
        currentState = DetectState.None;
        ApplyEffect();
    }

    private void ApplyEffect()
    {
        int targetLayer = _defaultLayerInt;

        switch (currentState)
        {
            case DetectState.Visible:
                // 시야에 바로 보일 때는 아웃라인 대신 기본 상태 유지
                targetLayer = _defaultLayerInt;
                break;
            case DetectState.Hidden:
                // 벽 뒤에 가려졌을 때만 실루엣 레이어 적용
                targetLayer = _silhouetteLayerInt;
                break;
            case DetectState.None:
                // 탐지 범위 밖일 때 기본 상태 유지
                targetLayer = _defaultLayerInt;
                break;
        }

        SetLayerRecursive(gameObject, targetLayer);
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