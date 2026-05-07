using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class KillerSkillUI : MonoBehaviour
{
    [Header("Attack Cooldown")]
    [SerializeField] private Slider attackSlider;
    [SerializeField] private Image attackFillImage;

    [Header("Trap Cooldown")]
    [SerializeField] private Slider trapSlider;
    [SerializeField] private Image trapFillImage;

    [Header("Fill Alpha")]
    [SerializeField] private float normalAlpha = 0.2f;
    [SerializeField] private float usingAlpha = 0.1f;

    private Coroutine attackRoutine;
    private Coroutine trapRoutine;

    private void Awake()
    {
        InitSlider(attackSlider, attackFillImage);
        InitSlider(trapSlider, trapFillImage);
    }

    private void InitSlider(Slider slider, Image fillImage)
    {
        if (slider != null)
        {
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
            slider.interactable = false;
        }

        SetFillAlpha(fillImage, normalAlpha);
    }

    public void SetAttackUsing()
    {
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }

        if (attackSlider != null)
            attackSlider.value = 0f;

        SetFillAlpha(attackFillImage, usingAlpha);
    }

    public void StartAttackCooldown(float duration)
    {
        if (attackRoutine != null)
            StopCoroutine(attackRoutine);

        attackRoutine = StartCoroutine(CooldownRoutine(attackSlider, attackFillImage, duration));
    }

    public void SetTrapUsing()
    {
        if (trapRoutine != null)
        {
            StopCoroutine(trapRoutine);
            trapRoutine = null;
        }

        if (trapSlider != null)
            trapSlider.value = 0f;

        SetFillAlpha(trapFillImage, usingAlpha);
    }

    public void StartTrapCooldown(float duration)
    {
        if (trapRoutine != null)
            StopCoroutine(trapRoutine);

        trapRoutine = StartCoroutine(CooldownRoutine(trapSlider, trapFillImage, duration));
    }

    private IEnumerator CooldownRoutine(Slider slider, Image fillImage, float duration)
    {
        if (slider == null)
            yield break;

        slider.value = 0f;
        SetFillAlpha(fillImage, normalAlpha);

        if (duration <= 0f)
        {
            slider.value = 1f;
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = elapsed / duration;
            slider.value = Mathf.Clamp01(t);

            yield return null;
        }

        slider.value = 1f;
    }

    private void SetFillAlpha(Image image, float alpha)
    {
        if (image == null)
            return;

        Color color = image.color;
        color.a = alpha;
        image.color = color;
    }
}