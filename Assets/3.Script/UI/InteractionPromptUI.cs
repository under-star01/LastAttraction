using UnityEngine;
using UnityEngine.UI;

public class InteractionPromptUI : MonoBehaviour
{
    [Header("루트")]
    [SerializeField] private GameObject root;

    [Header("입력 이미지")]
    [SerializeField] private Image inputIconImage;

    [Header("설명 텍스트")]
    [SerializeField] private Text actionText;

    private void Awake()
    {
        if (root == null)
            root = gameObject;

        Hide();
    }

    public void Show(Sprite inputSprite, string action)
    {
        if (root == null)
            root = gameObject;

        if (inputIconImage != null)
        {
            inputIconImage.sprite = inputSprite;
            inputIconImage.enabled = inputSprite != null;
        }

        if (actionText != null)
            actionText.text = action;

        root.SetActive(true);
    }

    public void Hide()
    {
        if (root == null)
            root = gameObject;

        root.SetActive(false);
    }
}