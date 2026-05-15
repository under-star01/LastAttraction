using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Canvas나 UI 루트에 붙이면 자식 Button들에게 클릭음 / 호버음을 자동으로 붙인다.
public class UIButtonSound : MonoBehaviour
{
    [Header("버튼 클릭음")]
    [SerializeField] private AudioKey clickSoundKey = AudioKey.UIButtonClick;

    [Header("버튼 호버음")]
    [SerializeField] private AudioKey hoverSoundKey = AudioKey.UIButtonHover;

    [Header("비활성화된 버튼도 미리 등록")]
    [SerializeField] private bool includeInactive = true;

    private void Awake()
    {
        BindButtons();
    }

    // 자식 Button들을 찾아서 사운드 컴포넌트를 붙인다.
    public void BindButtons()
    {
        Button[] buttons = GetComponentsInChildren<Button>(includeInactive);

        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];

            if (button == null)
                continue;

            UIButtonSoundTarget target = button.GetComponent<UIButtonSoundTarget>();

            if (target == null)
                target = button.gameObject.AddComponent<UIButtonSoundTarget>();

            target.SetSounds(clickSoundKey, hoverSoundKey);
        }
    }
}

// 실제 버튼에 붙어서 클릭음 / 호버음을 재생하는 컴포넌트
public class UIButtonSoundTarget : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler
{
    [SerializeField] private AudioKey clickSoundKey = AudioKey.UIButtonClick;
    [SerializeField] private AudioKey hoverSoundKey = AudioKey.UIButtonHover;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    public void SetSounds(AudioKey clickKey, AudioKey hoverKey)
    {
        clickSoundKey = clickKey;
        hoverSoundKey = hoverKey;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanPlay())
            return;

        if (clickSoundKey == AudioKey.None)
            return;

        AudioManager.PlayLocalAudio(clickSoundKey, AudioDimension.Sound2D);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!CanPlay())
            return;

        if (hoverSoundKey == AudioKey.None)
            return;

        AudioManager.PlayLocalAudio(hoverSoundKey, AudioDimension.Sound2D);
    }

    private bool CanPlay()
    {
        if (button == null)
            button = GetComponent<Button>();

        if (button != null && !button.IsInteractable())
            return false;

        return true;
    }
}