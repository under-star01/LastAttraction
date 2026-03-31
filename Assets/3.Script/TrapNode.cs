using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TrapNode : MonoBehaviour
{
    public float currentProgress = 0f;
    public bool isCompleted = false;

    [SerializeField] private Slider progressSlider;
    [SerializeField] private GameObject uiCanvas;

    private void Start()
    {
        UpdateUI();
        if (uiCanvas != null) uiCanvas.SetActive(true);
    }

    public void AddProgress(float amount)
    {
        if (isCompleted) return;
        currentProgress += amount;
        currentProgress = Mathf.Clamp(currentProgress, 0f, 100f);
        UpdateUI();
        if(currentProgress >= 100f)
        {
            CompleteTrap();
        }
    }

    private void UpdateUI()
    {
        if (progressSlider != null)
        {
            progressSlider.value = currentProgress / 100f;
        }
    }

    private void CompleteTrap()
    {
        isCompleted = true;
        if (uiCanvas != null) uiCanvas.SetActive(false);

        // 함정이 완성되었을 때의 로직 (예: 콜라이더 활성화 등)
        Debug.Log("함정 설치 완료!");
    }
}
