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

    [Header("Objective Text")]
    [SerializeField] private Text objectiveText;

    [TextArea]
    [SerializeField]
    private string collectEvidenceText =
        "관객들이 진실에 접근하지 못하도록 감옥에 가두세요.";

    [TextArea]
    [SerializeField]
    private string gateOpeningText =
        "증거 업로드가 완료되었습니다. 곧 출구가 개방됩니다.";

    [TextArea]
    [SerializeField]
    private string gateOpenedText =
        "출구가 개방되었습니다. 관객들이 탈출하지 못하도록 막으세요.";

    [Header("Fill Alpha")]
    [SerializeField] private float normalAlpha = 0.2f;
    [SerializeField] private float usingAlpha = 0.05f;

    private Coroutine attackRoutine;
    private Coroutine trapRoutine;
    private MatchObjectiveState currentObjectiveState = (MatchObjectiveState)(-1);

    private void Awake()
    {
        InitSlider(attackSlider, attackFillImage);
        InitSlider(trapSlider, trapFillImage);

        SetObjectiveText(MatchObjectiveState.CollectEvidence);
    }

    private void OnEnable()
    {
        GameManager.OnObjectiveStateChanged += HandleObjectiveStateChanged;
    }

    private void OnDisable()
    {
        GameManager.OnObjectiveStateChanged -= HandleObjectiveStateChanged;
    }

    private void Start()
    {
        if (GameManager.Instance != null)
            HandleObjectiveStateChanged(GameManager.Instance.ObjectiveState);
    }

    private void HandleObjectiveStateChanged(MatchObjectiveState state)
    {
        if (state == MatchObjectiveState.UploadEvidence)
            return;

        SetObjectiveText(state);
    }

    private void SetObjectiveText(MatchObjectiveState state)
    {
        if (currentObjectiveState == state)
            return;

        currentObjectiveState = state;

        if (objectiveText == null)
            return;

        switch (state)
        {
            case MatchObjectiveState.CollectEvidence:
                objectiveText.text = collectEvidenceText;
                break;

            case MatchObjectiveState.GateOpening:
                objectiveText.text = gateOpeningText;
                break;

            case MatchObjectiveState.GateOpened:
                objectiveText.text = gateOpenedText;
                break;
        }
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