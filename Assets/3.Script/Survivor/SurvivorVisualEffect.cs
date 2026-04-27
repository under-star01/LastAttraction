using UnityEngine;

//public class SurvivorVisualEffect : MonoBehaviour
//{
//    [Header("렌더러 참조")]
//    [SerializeField] private Renderer[] outlineRenderers;    // OutlineMesh
//    [SerializeField] private Renderer[] silhouetteRenderers; // SilhouetteMesh

//    public enum DetectState { None, Visible, Hidden }
//    private DetectState currentState = DetectState.None;

//    public void SetDetected(bool hasLOS)
//    {
//        DetectState next = hasLOS ? DetectState.Visible : DetectState.Hidden;
//        if (currentState == next) return;
//        currentState = next;
//        ApplyEffect();
//    }

//    public void SetUndetected()
//    {
//        if (currentState == DetectState.None) return;
//        currentState = DetectState.None;
//        ApplyEffect();
//    }

//    private void ApplyEffect()
//    {
//        switch (currentState)
//        {
//            case DetectState.Visible:
//                // 바로 보임 → 빨간 아웃라인
//                SetRenderers(outlineRenderers, true);
//                SetRenderers(silhouetteRenderers, false);
//                break;

//            case DetectState.Hidden:
//                // 벽 뒤 → 빨간 실루엣
//                SetRenderers(outlineRenderers, false);
//                SetRenderers(silhouetteRenderers, true);
//                break;

//            case DetectState.None:
//                SetRenderers(outlineRenderers, false);
//                SetRenderers(silhouetteRenderers, false);
//                break;
//        }
//    }

//    private void SetRenderers(Renderer[] renderers, bool enable)
//    {
//        foreach (var r in renderers)
//            if (r != null) r.enabled = enable;
//    }
//}

public class SurvivorVisualEffect : MonoBehaviour
{
    [Header("레이어 설정 (Project Settings에서 생성 필요)")]
    [SerializeField] private string defaultLayer = "Survivor";
    [SerializeField] private string outlineLayer = "SurvivorOutline";
    [SerializeField] private string silhouetteLayer = "SurvivorSilhouette";

    private int _defaultLayerInt;
    private int _outlineLayerInt;
    private int _silhouetteLayerInt;

    public enum DetectState { None, Visible, Hidden }
    private DetectState currentState = DetectState.None;

    private void Awake()
    {
        // 문자열 비교보다 빠른 정수형 레이어 값 미리 캐싱
        _defaultLayerInt = LayerMask.NameToLayer(defaultLayer);
        _outlineLayerInt = LayerMask.NameToLayer(outlineLayer);
        _silhouetteLayerInt = LayerMask.NameToLayer(silhouetteLayer);
    }

    public void SetDetected(bool hasLOS)
    {
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
            case DetectState.Visible: targetLayer = _outlineLayerInt; break;
            case DetectState.Hidden: targetLayer = _silhouetteLayerInt; break;
            case DetectState.None: targetLayer = _defaultLayerInt; break;
        }

        // 본인과 모든 자식(눈, 머리카락 등 파츠)의 레이어를 한꺼번에 변경
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