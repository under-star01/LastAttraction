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

    [Header("UI Root")]
    [SerializeField] private GameObject root;

    [Header("QTE Points")]
    [SerializeField] private List<QTEPoint> qtePoints = new();

    [Header("Key Sprites")]
    [SerializeField] private Sprite wSprite;
    [SerializeField] private Sprite aSprite;
    [SerializeField] private Sprite sSprite;
    [SerializeField] private Sprite dSprite;

    [Header("QTE Settings")]
    [SerializeField] private float startScale = 5f;
    [SerializeField] private float minSuccessScale = 0.5f;
    [SerializeField] private float maxSuccessScale = 1.5f;
    [SerializeField] private float shrinkSpeed = 4f;

    private InputSystem inputSys;
    private Coroutine qteRoutine;

    private bool inputReceived;
    private QTEKey pressedKey = QTEKey.None;
    private QTEKey answerKey = QTEKey.None;

    private QTEPoint currentPoint;
    private bool isRunning;

    private Action<bool> finishCallback;

    public bool IsRunning => isRunning;

    private void Awake()
    {
        HideUI();
        HideAllPoints();
        ResetStepState();
    }

    // 외부에서 QTE 시작
    public void StartQTE(Action<bool> onFinished)
    {
        ForceClose(false);

        finishCallback = onFinished;
        EnableInput();

        ShowUI();
        HideAllPoints();
        ResetStepState();

        qteRoutine = StartCoroutine(SingleQTERoutine());
    }

    // 외부에서 강제 종료
    // notifyResult = true 면 실패 처리까지 같이 전달
    public void ForceClose(bool notifyResult)
    {
        if (qteRoutine != null)
        {
            StopCoroutine(qteRoutine);
            qteRoutine = null;
        }

        bool wasRunning = isRunning;

        isRunning = false;
        DisableInput();

        HideAllPoints();
        HideUI();
        ResetStepState();

        if (notifyResult && wasRunning)
            Finish(false);
    }

    // 단일 QTE 1회 실행
    private IEnumerator SingleQTERoutine()
    {
        isRunning = true;

        currentPoint = GetRandomPoint();
        if (currentPoint == null)
        {
            Finish(false);
            yield break;
        }

        answerKey = GetRandomKey();

        if (currentPoint.pointImage != null)
            currentPoint.pointImage.sprite = GetSprite(answerKey);

        if (currentPoint.rangeTransform != null)
            currentPoint.rangeTransform.localScale = Vector3.one * startScale;

        if (currentPoint.pointObject != null)
            currentPoint.pointObject.SetActive(true);

        while (!inputReceived)
        {
            if (currentPoint == null || currentPoint.rangeTransform == null)
            {
                Finish(false);
                yield break;
            }

            float currentScale = currentPoint.rangeTransform.localScale.x;
            float nextScale = Mathf.MoveTowards(currentScale, 0f, shrinkSpeed * Time.deltaTime);
            currentPoint.rangeTransform.localScale = Vector3.one * nextScale;

            // 너무 늦게 눌렀으면 실패
            if (nextScale < minSuccessScale)
            {
                Finish(false);
                yield break;
            }

            yield return null;
        }

        bool result = CheckSuccess();
        Finish(result);
    }

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

    private void Finish(bool success)
    {
        if (qteRoutine != null)
        {
            StopCoroutine(qteRoutine);
            qteRoutine = null;
        }

        isRunning = false;
        DisableInput();

        HideAllPoints();
        HideUI();

        Action<bool> callback = finishCallback;
        finishCallback = null;

        ResetStepState();

        callback?.Invoke(success);
    }

    private void EnableInput()
    {
        if (inputSys != null)
            return;

        inputSys = new InputSystem();
        inputSys.Player.Enable();
        inputSys.Player.Interact3.performed += OnInteract3Performed;
    }

    private void DisableInput()
    {
        if (inputSys == null)
            return;

        inputSys.Player.Interact3.performed -= OnInteract3Performed;
        inputSys.Player.Disable();
        inputSys = null;
    }

    private void ShowUI()
    {
        if (root != null)
            root.SetActive(true);
        else
            gameObject.SetActive(true);
    }

    private void HideUI()
    {
        if (root != null)
            root.SetActive(false);
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