using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class QTEUI : MonoBehaviour
{
    private enum QTEKey
    {
        W,
        A,
        S,
        D
    }

    [System.Serializable]
    private class QTEPointSlot
    {
        public GameObject root;              // QTE Point 전체 오브젝트
        public Image keyImage;               // W/A/S/D 아이콘 표시용
        public RectTransform rangeTransform; // 줄어드는 Range 오브젝트
    }

    [Header("QTE Point Slots")]
    [SerializeField] private List<QTEPointSlot> pointSlots = new();

    [Header("Key Sprites")]
    [SerializeField] private Sprite wSprite;
    [SerializeField] private Sprite aSprite;
    [SerializeField] private Sprite sSprite;
    [SerializeField] private Sprite dSprite;

    [Header("QTE Settings")]
    [SerializeField] private int requiredSuccessCount = 4;
    [SerializeField] private float delayBetweenQTE = 0.5f;
    [SerializeField] private float shrinkDuration = 1.2f;
    [SerializeField] private float startScale = 5f;
    [SerializeField] private float targetScale = 1f;

    [Header("Success Window")]
    [SerializeField] private float successMinScale = 0.9f;
    [SerializeField] private float successMaxScale = 1.1f;

    private Coroutine qteRoutine;
    private bool isRunning;

    private void Awake()
    {
        HideAllPoints();
        gameObject.SetActive(false);
    }

    public void StartQTE()
    {
        if (isRunning)
            return;

        gameObject.SetActive(true);
        isRunning = true;
        qteRoutine = StartCoroutine(QTESequenceRoutine());
    }

    public void StopQTE()
    {
        if (qteRoutine != null)
        {
            StopCoroutine(qteRoutine);
            qteRoutine = null;
        }

        isRunning = false;
        HideAllPoints();
        gameObject.SetActive(false);
    }

    private IEnumerator QTESequenceRoutine()
    {
        int successCount = 0;

        while (successCount < requiredSuccessCount)
        {
            yield return new WaitForSeconds(delayBetweenQTE);

            bool stepSuccess = false;
            yield return StartCoroutine(RunSingleQTE(result => stepSuccess = result));

            if (!stepSuccess)
            {
                Debug.Log("[QTE] 실패");
                StopQTE();
                yield break;
            }

            successCount++;
            Debug.Log($"[QTE] 성공 ({successCount}/{requiredSuccessCount})");
        }

        Debug.Log("[QTE] 전체 성공");
        StopQTE();
    }

    private IEnumerator RunSingleQTE(System.Action<bool> onFinished)
    {
        HideAllPoints();

        if (pointSlots.Count == 0)
        {
            Debug.LogWarning("[QTE] Point Slot이 설정되지 않았습니다.");
            onFinished?.Invoke(false);
            yield break;
        }

        int randomPointIndex = Random.Range(0, pointSlots.Count);
        QTEPointSlot selectedPoint = pointSlots[randomPointIndex];

        QTEKey targetKey = (QTEKey)Random.Range(0, 4);

        selectedPoint.root.SetActive(true);
        selectedPoint.keyImage.sprite = GetSprite(targetKey);
        selectedPoint.rangeTransform.localScale = Vector3.one * startScale;

        float elapsed = 0f;
        bool finished = false;

        while (elapsed < shrinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / shrinkDuration);
            float currentScale = Mathf.Lerp(startScale, targetScale, t);
            selectedPoint.rangeTransform.localScale = Vector3.one * currentScale;

            if (TryGetPressedQTEKey(out QTEKey pressedKey))
            {
                bool isCorrectKey = pressedKey == targetKey;
                bool isInSuccessWindow = currentScale >= successMinScale && currentScale <= successMaxScale;

                if (isCorrectKey && isInSuccessWindow)
                {
                    HideAllPoints();
                    finished = true;
                    onFinished?.Invoke(true);
                    yield break;
                }
                else
                {
                    HideAllPoints();
                    finished = true;
                    onFinished?.Invoke(false);
                    yield break;
                }
            }

            yield return null;
        }

        if (!finished)
        {
            HideAllPoints();
            onFinished?.Invoke(false);
        }
    }

    private bool TryGetPressedQTEKey(out QTEKey pressedKey)
    {
        pressedKey = default;

        if (Keyboard.current == null)
            return false;

        if (Keyboard.current.wKey.wasPressedThisFrame)
        {
            pressedKey = QTEKey.W;
            return true;
        }

        if (Keyboard.current.aKey.wasPressedThisFrame)
        {
            pressedKey = QTEKey.A;
            return true;
        }

        if (Keyboard.current.sKey.wasPressedThisFrame)
        {
            pressedKey = QTEKey.S;
            return true;
        }

        if (Keyboard.current.dKey.wasPressedThisFrame)
        {
            pressedKey = QTEKey.D;
            return true;
        }

        return false;
    }

    private Sprite GetSprite(QTEKey key)
    {
        switch (key)
        {
            case QTEKey.W: return wSprite;
            case QTEKey.A: return aSprite;
            case QTEKey.S: return sSprite;
            case QTEKey.D: return dSprite;
        }

        return null;
    }

    private void HideAllPoints()
    {
        for (int i = 0; i < pointSlots.Count; i++)
        {
            if (pointSlots[i].root != null)
                pointSlots[i].root.SetActive(false);
        }
    }
}