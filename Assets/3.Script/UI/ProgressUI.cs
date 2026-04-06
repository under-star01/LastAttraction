using UnityEngine;
using UnityEngine.UI;

public class ProgressUI : MonoBehaviour
{
    [SerializeField] private GameObject root; // UI 전체 루트
    [SerializeField] private Image fillImage; // 채워지는 이미지

    private void Awake()
    {
        // 시작할 때는 안 보이게만 처리
        Hide();
        SetProgress(0f);
    }

    // UI 표시
    public void Show()
    {
        if (root != null)
            root.SetActive(true);
    }

    // UI 숨기기
    public void Hide()
    {
        if (root != null)
            root.SetActive(false);
    }

    // 진행도 초기화 + 숨기기
    // 완전히 끝났을 때만 이 함수를 사용
    public void ResetUI()
    {
        SetProgress(0f);
        Hide();
    }

    // 0~1 값으로 채움량 설정
    public void SetProgress(float value)
    {
        value = Mathf.Clamp01(value);

        if (fillImage != null)
            fillImage.fillAmount = value;
    }
}