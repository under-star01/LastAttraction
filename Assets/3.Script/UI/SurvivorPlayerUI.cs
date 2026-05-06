using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 생존자 1명의 상태 UI 슬롯을 관리한다.
// - 기본 초상화
// - 부상 오버레이
// - 다운/감옥/탈출/사망 대체 아이콘
// - 현재 행동 아이콘
// - 감옥 단계 표시
public class SurvivorPlayerUI : MonoBehaviour
{
    [Header("기본")]
    [SerializeField] private GameObject root;         // 이 슬롯 전체 루트
    [SerializeField] private Text nameText;           // 닉네임 텍스트

    [Header("초상화 / 상태")]
    [SerializeField] private Image portraitIcon;        // 생존자 기본 얼굴 이미지
    [SerializeField] private Image injuryOverlay;       // 다쳤을 때 초상화 위에 덮는 상처 이미지
    [SerializeField] private Image replaceStateIcon;    // Down, 감옥, 탈출, 사망 상태 대체 이미지

    [Header("상태 진행도")]
    [SerializeField] private Slider prisonTimeSlider;   // 감옥 남은 시간 표시

    [Header("행동 UI")]
    [SerializeField] private Image actionIcon;           // 현재 행동 아이콘
    [SerializeField] private Slider actionProgressSlider;// 행동 진행도 표시

    [Header("감옥 단계 UI")]
    [SerializeField] private Image catchLine1;           // 첫 번째 감옥 단계 선
    [SerializeField] private Image catchLine2;           // 두 번째 감옥 단계 선
    [SerializeField] private Color emptyColor = Color.black;
    [SerializeField] private Color usedColor = Color.white;

    public void SetVisible(bool value)
    {
        if (root != null)
            root.SetActive(value);
        else
            gameObject.SetActive(value);
    }

    public void SetName(string value)
    {
        if (nameText != null)
            nameText.text = value;
    }

    // 생존자별 기본 초상화 설정
    public void SetPortrait(Sprite sprite)
    {
        if (portraitIcon == null)
            return;

        portraitIcon.sprite = sprite;
        portraitIcon.enabled = sprite != null;
    }

    // 생존자 상태 UI 설정
    // showPortrait = true면 기본 초상화를 보여준다.
    // showInjury = true면 상처 오버레이를 보여준다.
    // replaceSprite가 있으면 기본 초상화를 대체하는 상태 아이콘을 보여준다.
    public void SetConditionUI(bool showPortrait, bool showInjury, Sprite replaceSprite)
    {
        if (portraitIcon != null)
            portraitIcon.enabled = showPortrait && portraitIcon.sprite != null;

        if (injuryOverlay != null)
            injuryOverlay.enabled = showInjury;

        if (replaceStateIcon != null)
        {
            replaceStateIcon.sprite = replaceSprite;
            replaceStateIcon.enabled = replaceSprite != null;
        }
    }

    // 현재 행동 아이콘과 진행도 표시
    public void SetAction(Sprite sprite, bool showProgress, float progress01)
    {
        if (actionIcon != null)
        {
            actionIcon.sprite = sprite;
            actionIcon.enabled = sprite != null;
        }

        if (actionProgressSlider != null)
        {
            actionProgressSlider.gameObject.SetActive(showProgress);
            actionProgressSlider.value = Mathf.Clamp01(progress01);
        }
    }

    // 감옥에 갇힌 상태일 때 남은 시간을 표시한다.
    public void SetPrisonTimer(bool visible, float remain01)
    {
        if (prisonTimeSlider == null)
            return;

        prisonTimeSlider.gameObject.SetActive(visible);
        prisonTimeSlider.value = Mathf.Clamp01(remain01);
    }

    // 감옥에 갇힌 횟수 단계 표시
    // 0 = 검은선 2개
    // 1 = 흰선 1개 + 검은선 1개
    // 2 = 흰선 2개
    public void SetCatchCount(int count)
    {
        count = Mathf.Clamp(count, 0, 2);

        if (catchLine1 != null)
            catchLine1.color = count >= 1 ? usedColor : emptyColor;

        if (catchLine2 != null)
            catchLine2.color = count >= 2 ? usedColor : emptyColor;
    }

    // 슬롯 초기화용
    public void Clear()
    {
        SetName("NickName");
        SetPortrait(null);
        SetConditionUI(false, false, null);
        SetAction(null, false, 0f);
        SetPrisonTimer(false, 0f);
        SetCatchCount(0);
    }
}