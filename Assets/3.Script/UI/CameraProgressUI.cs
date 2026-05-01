using UnityEngine;
using UnityEngine.UI;

public class CameraProgressUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject root;
    [SerializeField] private Slider detectSlider;

    [Header("표시 옵션")]
    [SerializeField] private bool hideWhenComplete = false;

    private void Awake()
    {
        if (root == null)
            root = gameObject;

        if (detectSlider != null)
        {
            detectSlider.minValue = 0f;
            detectSlider.maxValue = 1f;
            detectSlider.value = 0f;
        }
    }

    private void Update()
    {
        GameManager gm = GameManager.Instance;

        if (gm == null)
            return;

        if (detectSlider == null)
            return;

        // GameManager의 SyncVar 진행도를 통해
        // 모든 클라이언트가 같은 공용 탐지 게이지를 본다.
        detectSlider.value = gm.KillerDetectProgress01;

        if (root != null)
        {
            if (hideWhenComplete && gm.IsKillerDetectComplete)
                root.SetActive(false);
            else
                root.SetActive(true);
        }
    }
}