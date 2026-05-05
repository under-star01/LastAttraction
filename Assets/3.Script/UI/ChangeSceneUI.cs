using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ChangeSceneUI : MonoBehaviour
{
    public static ChangeSceneUI Instance { get; private set; }

    [Header("Fade Image")]
    [SerializeField] private Image fadeImage;

    [Header("Fade Settings")]
    [SerializeField] private float fadeDuration = 1f;

    private Coroutine fadeRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        SetAlpha(0f);
        SetActive(false);
    }

    public void Show(bool value)
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        fadeRoutine = StartCoroutine(Fade(value));
    }

    private IEnumerator Fade(bool show)
    {
        if (fadeImage == null)
            yield break;

        if (show)
            SetActive(true);

        float startAlpha = fadeImage.color.a;
        float targetAlpha = show ? 1f : 0f;

        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            float t = elapsed / fadeDuration;
            float alpha = Mathf.Lerp(startAlpha, targetAlpha, t);

            SetAlpha(alpha);

            yield return null;
        }

        SetAlpha(targetAlpha);

        if (!show)
            SetActive(false);

        fadeRoutine = null;
    }

    private void SetAlpha(float alpha)
    {
        if (fadeImage == null)
            return;

        Color color = fadeImage.color;
        color.a = alpha;
        fadeImage.color = color;
    }

    private void SetActive(bool value)
    {
        if (fadeImage == null)
            return;

        fadeImage.gameObject.SetActive(value);
    }
}