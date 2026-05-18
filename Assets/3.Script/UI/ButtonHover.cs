using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Hover 대상 Panel Image")]
    [SerializeField] private Image targetPanelImage;

    [Header("Color 설정")]
    [SerializeField] private Color normalColor = new Color(0f, 0f, 0f, 0.7f);
    [SerializeField] private Color hoverColor = new Color(1f, 1f, 1f, 0.7f);

    private void Awake()
    {
        SetNormal();
    }

    private void OnEnable()
    {
        // UI가 다시 켜질 때 hover 상태가 남지 않게 초기화
        SetNormal();
    }

    private void OnDisable()
    {
        // UI가 꺼질 때도 초기화
        SetNormal();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        SetHover();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetNormal();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // 클릭 직후 hover 상태가 남는 문제 방지
        SetNormal();

        // 버튼 선택 상태도 해제해서 Unity Button의 Highlight 상태가 남는 것도 방지
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private void SetHover()
    {
        if (targetPanelImage == null)
            return;

        targetPanelImage.color = hoverColor;
    }

    private void SetNormal()
    {
        if (targetPanelImage == null)
            return;

        targetPanelImage.color = normalColor;
    }
}