using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class QTEUI : MonoBehaviour
{
    private enum QTEKey
    {
        None,
        W,
        A,
        S,
        D
    }

    [System.Serializable]
    private class QTEPoint
    {
        public GameObject pointObject;
        public Image pointImage;
        public RectTransform rangeTransform;
    }

    [Header("QTE Points")]
    [SerializeField] private List<QTEPoint> qtePoints = new();

    [Header("Key Sprites")]
    [SerializeField] private Sprite wSprite;
    [SerializeField] private Sprite aSprite;
    [SerializeField] private Sprite sSprite;
    [SerializeField] private Sprite dSprite;

    [Header("QTE Settings")]
    [SerializeField] private int successTargetCount = 4;
    [SerializeField] private float startScale = 5f;
    [SerializeField] private float minSuccessScale = 0.5f;
    [SerializeField] private float maxSuccessScale = 1.5f;
    [SerializeField] private float shrinkSpeed = 4f;
    [SerializeField] private float nextDelay = 0.25f;

    private InputSystem inputSys;
    private Coroutine qteRoutine;

    private bool inputReceived;
    private QTEKey pressedKey = QTEKey.None;
    private QTEKey answerKey = QTEKey.None;

    private QTEPoint currentPoint;
    private bool isRunning;

    private void OnEnable()
    {
        inputSys = new InputSystem();
        inputSys.Player.Enable();
        inputSys.Player.Interact3.performed += OnInteract3Performed;

        HideAllPoints();
        ResetStepState();

        qteRoutine = StartCoroutine(QTERoutine());
    }

    private void OnDisable()
    {
        if (inputSys != null)
        {
            inputSys.Player.Interact3.performed -= OnInteract3Performed;
            inputSys.Player.Disable();
            inputSys = null;
        }

        if (qteRoutine != null)
        {
            StopCoroutine(qteRoutine);
            qteRoutine = null;
        }

        HideAllPoints();
        ResetStepState();
        isRunning = false;
    }

    // 전체 QTE 루프 코루틴
    private IEnumerator QTERoutine()
    {
        isRunning = true;
        int successCount = 0;

        while (successCount < successTargetCount)
        {
            // 반환값이 IEnumerator로 고정이라서, 지역 변수 success에 값을 변경할 수 있는 임시 메소드를 매개변수로 전달
            bool success = false;
            yield return StartCoroutine(SingleQTERoutine(result => success = result)); // result는 매개변수, => 뒤에 내용이 메소드 내용

            if (!success)
            {
                Debug.Log("[QTE] 실패");
                isRunning = false;
                gameObject.SetActive(false);
                yield break;
            }

            successCount++;
            Debug.Log($"[QTE] 성공 ({successCount}/{successTargetCount})");

            if (successCount < successTargetCount)
                yield return new WaitForSeconds(nextDelay);
        }

        Debug.Log("[QTE] 전체 성공");
        isRunning = false;
        gameObject.SetActive(false);
    }

    // 단독 QTE 실행 코루틴 
    private IEnumerator SingleQTERoutine(Action<bool> onFinished)
    {
        ResetStepState();
        HideAllPoints();

        currentPoint = GetRandomPoint();
        if (currentPoint == null)
        {
            onFinished?.Invoke(false);
            yield break;
        }

        answerKey = GetRandomKey();

        currentPoint.pointImage.sprite = GetSprite(answerKey);
        currentPoint.rangeTransform.localScale = Vector3.one * startScale;
        currentPoint.pointObject.SetActive(true);

        while (!inputReceived && currentPoint.rangeTransform.localScale.x > minSuccessScale)
        {
            float currentScale = currentPoint.rangeTransform.localScale.x;
            float nextScale = Mathf.MoveTowards(currentScale, 0f, shrinkSpeed * Time.deltaTime);
            currentPoint.rangeTransform.localScale = Vector3.one * nextScale;
            yield return null;
        }

        bool result = CheckSuccess();

        currentPoint.pointObject.SetActive(false);
        ResetStepState();

        onFinished?.Invoke(result);
    }

    // QTE 상호작용 Input 입력시 실행 메소드
    private void OnInteract3Performed(InputAction.CallbackContext context)
    {
        if (!isRunning)
            return;

        if (inputReceived)
            return;

        pressedKey = ConvertControlToKey(context.control);
        if (pressedKey == QTEKey.None)
            return;

        inputReceived = true;
    }

    private bool CheckSuccess()
    {
        if (!inputReceived)
            return false;

        if (pressedKey != answerKey)
            return false;

        if (currentPoint == null || currentPoint.rangeTransform == null)
            return false;

        float scale = currentPoint.rangeTransform.localScale.x;
        return scale >= minSuccessScale && scale <= maxSuccessScale;
    }

    private QTEPoint GetRandomPoint()
    {
        if (qtePoints == null || qtePoints.Count == 0)
            return null;

        int index = UnityEngine.Random.Range(0, qtePoints.Count);
        return qtePoints[index];
    }

    private QTEKey GetRandomKey()
    {
        int value = UnityEngine.Random.Range(1, 5);
        return (QTEKey)value;
    }

    private Sprite GetSprite(QTEKey key)
    {
        switch (key)
        {
            case QTEKey.W: return wSprite;
            case QTEKey.A: return aSprite;
            case QTEKey.S: return sSprite;
            case QTEKey.D: return dSprite;
            default: return null;
        }
    }

    private QTEKey ConvertControlToKey(InputControl control)
    {
        if (control == null)
            return QTEKey.None;

        string keyName = control.name.ToLower();

        switch (keyName)
        {
            case "w": return QTEKey.W;
            case "a": return QTEKey.A;
            case "s": return QTEKey.S;
            case "d": return QTEKey.D;
            default: return QTEKey.None;
        }
    }

    private void HideAllPoints()
    {
        for (int i = 0; i < qtePoints.Count; i++)
        {
            if (qtePoints[i] != null && qtePoints[i].pointObject != null)
                qtePoints[i].pointObject.SetActive(false);
        }
    }

    private void ResetStepState()
    {
        inputReceived = false;
        pressedKey = QTEKey.None;
        answerKey = QTEKey.None;
        currentPoint = null;
    }
}