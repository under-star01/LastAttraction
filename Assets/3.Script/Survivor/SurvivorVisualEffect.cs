using UnityEngine;

public class SurvivorVisualEffect : MonoBehaviour
{
    [Header("·»´õ·¯ ÂüÁ¶")]
    [SerializeField] private Renderer[] outlineRenderers;    // OutlineMesh
    [SerializeField] private Renderer[] silhouetteRenderers; // SilhouetteMesh

    public enum DetectState { None, Visible, Hidden }
    private DetectState currentState = DetectState.None;

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
        switch (currentState)
        {
            case DetectState.Visible:
                // ¹Ù·Î º¸ÀÓ ¡æ »¡°£ ¾Æ¿ô¶óÀÎ
                SetRenderers(outlineRenderers, true);
                SetRenderers(silhouetteRenderers, false);
                break;

            case DetectState.Hidden:
                // º® µÚ ¡æ »¡°£ ½Ç·ç¿§
                SetRenderers(outlineRenderers, false);
                SetRenderers(silhouetteRenderers, true);
                break;

            case DetectState.None:
                SetRenderers(outlineRenderers, false);
                SetRenderers(silhouetteRenderers, false);
                break;
        }
    }

    private void SetRenderers(Renderer[] renderers, bool enable)
    {
        foreach (var r in renderers)
            if (r != null) r.enabled = enable;
    }
}