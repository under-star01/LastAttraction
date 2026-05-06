using UnityEngine;
using UnityEngine.UI;

// 내 로컬 플레이어의 입력 UI를 관리한다.
// - 좌클릭 상호작용 중이면 클릭 아이콘을 흐리게 표시
// - 우클릭 카메라 스킬 중이면 우클릭 아이콘을 흐리게 표시
public class LocalActionUI : MonoBehaviour
{
    [Header("입력 아이콘")]
    [SerializeField] private Image clickIcon;
    [SerializeField] private Image rightClickIcon;

    [Header("투명도")]
    [SerializeField] private float normalAlpha = 1f;
    [SerializeField] private float usedAlpha = 0.35f;

    public void SetClickUsed(bool value)
    {
        SetAlpha(clickIcon, value ? usedAlpha : normalAlpha);
    }

    public void SetRightClickUsed(bool value)
    {
        SetAlpha(rightClickIcon, value ? usedAlpha : normalAlpha);
    }

    private void SetAlpha(Image image, float alpha)
    {
        if (image == null)
            return;

        Color color = image.color;
        color.a = alpha;
        image.color = color;
    }
}