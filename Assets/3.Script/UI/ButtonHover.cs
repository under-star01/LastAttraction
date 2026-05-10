using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Hover 대상 Panel Image")]
    [SerializeField] private Image targetPanelImage;

    [Header("Color 설정")]
    [SerializeField] private Color normalColor = new Color(0f, 0f, 0f, 0.7f);
    [SerializeField] private Color hoverColor = new Color(1f, 1f, 1f, 0.7f);

    private void Awake()
    {
        if (targetPanelImage != null)
        {
            targetPanelImage.color = normalColor;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (targetPanelImage == null)
            return;

        targetPanelImage.color = hoverColor;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (targetPanelImage == null)
            return;

        targetPanelImage.color = normalColor;
    }
}