using System.Collections;
using UnityEngine;

// 로비에 살인마가 있으면 켜지는 라이트
public class LobbyKillerLight : MonoBehaviour
{
    [Header("켜고 끌 라이트")]
    [SerializeField] private Light targetLight;

    [Header("켜졌을 때 밝기")]
    [SerializeField] private float onIntensity = 3f;

    [Header("부드럽게 켜지는 시간")]
    [SerializeField] private float fadeTime = 0.5f;

    private bool isLightOn;
    private Coroutine fadeRoutine;

    private void Awake()
    {
        SetLightImmediate(false);
    }

    public void SetLight(bool value)
    {
        if (isLightOn == value)
            return;

        isLightOn = value;

        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine = StartCoroutine(FadeLightRoutine(value));
    }

    private IEnumerator FadeLightRoutine(bool value)
    {
        if (targetLight == null)
            yield break;

        float startIntensity = targetLight.intensity;
        float targetIntensity = value ? onIntensity : 0f;

        if (value)
            targetLight.enabled = true;

        float timer = 0f;

        while (timer < fadeTime)
        {
            timer += Time.deltaTime;

            float t = fadeTime <= 0f ? 1f : timer / fadeTime;
            targetLight.intensity = Mathf.Lerp(startIntensity, targetIntensity, t);

            yield return null;
        }

        targetLight.intensity = targetIntensity;

        if (!value)
            targetLight.enabled = false;

        fadeRoutine = null;
    }

    private void SetLightImmediate(bool value)
    {
        isLightOn = value;

        if (targetLight == null)
            return;

        targetLight.enabled = value;
        targetLight.intensity = value ? onIntensity : 0f;
    }
}