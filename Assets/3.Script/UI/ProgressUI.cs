using UnityEngine;
using UnityEngine.UI;

public class ProgressUI : MonoBehaviour
{
    [SerializeField] private GameObject root; // UI 전체 루트
    [SerializeField] private Image fillImage; // 채워지는 이미지

    private void Awake()
    {
        Hide();
    }

    // UI 표시
    public void Show()
    {
        if (root != null)
            root.SetActive(true);
    }

    // UI 숨기고 게이지 초기화
    public void Hide()
    {
        if (root != null)
            root.SetActive(false);

        SetProgress(0f);
    }

    // 0~1 값으로 채움량 설정
    public void SetProgress(float value)
    {
        value = Mathf.Clamp01(value);

        if (fillImage != null)
            fillImage.fillAmount = value;
    }
}