using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Canvas나 UI 루트에 붙이면 자식 Button들에게 클릭음을 자동으로 붙인다.
public class UIButtonSound : MonoBehaviour
{
    [Header("버튼 클릭음")]
    [SerializeField] private AudioKey clickSoundKey = AudioKey.UIButtonClick;

    [Header("비활성화된 버튼도 미리 등록")]
    [SerializeField] private bool includeInactive = true;

    private void Awake()
    {
        BindButtons();
    }

    // 자식 Button들을 찾아서 클릭음 컴포넌트를 붙인다.
    public void BindButtons()
    {
        Button[] buttons = GetComponentsInChildren<Button>(includeInactive);

        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];

            if (button == null)
                continue;

            UIButtonClickSoundTarget target = button.GetComponent<UIButtonClickSoundTarget>();

            if (target == null)
                target = button.gameObject.AddComponent<UIButtonClickSoundTarget>();

            target.SetClickSound(clickSoundKey);
        }
    }
}

// 실제 버튼에 붙어서 클릭음을 재생하는 컴포넌트
public class UIButtonClickSoundTarget : MonoBehaviour, IPointerDownHandler
{
    [SerializeField] private AudioKey clickSoundKey = AudioKey.UIButtonClick;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    public void SetClickSound(AudioKey key)
    {
        clickSoundKey = key;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (clickSoundKey == AudioKey.None)
            return;

        if (button == null)
            button = GetComponent<Button>();

        // 비활성화되었거나 interactable이 꺼진 버튼은 소리도 안 나게 한다.
        if (button != null && !button.IsInteractable())
            return;

        AudioManager.PlayLocalAudio(clickSoundKey, AudioDimension.Sound2D);
    }
}