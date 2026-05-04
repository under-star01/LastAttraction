using UnityEngine;
using UnityEngine.UI;

public class ObjectiveProgressUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject root;
    [SerializeField] private Slider objectiveSlider;
    [SerializeField] private Text objectiveText;

    [Header("대상")]
    [SerializeField] private UploadComputer targetComputer;

    [Header("문구")]
    [SerializeField] private string cameraGoalText = "살인마를 촬영하고 증거를 수집";
    [SerializeField] private string waitingEvidenceText = "증거 수집 대기";
    [SerializeField] private string uploadGoalText = "컴퓨터에 증거를 업로드";
    [SerializeField] private string gateTimerText = "탈출문 개방까지 남은시간";

    private void Awake()
    {
        // 루트가 비어 있으면 자기 자신을 루트로 사용한다.
        if (root == null)
            root = gameObject;

        // Slider는 0~1 값을 사용한다.
        if (objectiveSlider != null)
        {
            objectiveSlider.minValue = 0f;
            objectiveSlider.maxValue = 1f;
            objectiveSlider.value = 0f;
            objectiveSlider.interactable = false;
        }

        Hide();
    }

    private void Update()
    {
        GameManager gm = GameManager.Instance;

        if (gm == null)
        {
            Hide();
            return;
        }

        // 문이 열렸으면 목표 UI를 숨긴다.
        if (IsGateOpened(gm))
        {
            Hide();
            return;
        }

        // 업로드 완료 후 문 개방 대기 중이면 문 타이머를 보여준다.
        if (targetComputer != null && targetComputer.GateTimerVisible)
        {
            ShowGateTimer();
            return;
        }

        // 업로드 컴퓨터가 열렸거나 업로드가 조금이라도 진행됐다면 업로드 게이지를 보여준다.
        if (targetComputer != null && (targetComputer.IsOpen || targetComputer.UploadProgress01 > 0f))
        {
            ShowUploadGoal();
            return;
        }

        // 업로드가 아직 열리지 않았다면 카메라 목표 게이지를 보여준다.
        ShowCameraGoal(gm);
    }

    // 문이 열렸는지 GameManager 또는 UploadComputer 상태로 판단한다.
    private bool IsGateOpened(GameManager gm)
    {
        if (gm != null && gm.GateOpened)
            return true;

        if (targetComputer != null && targetComputer.GateOpened)
            return true;

        return false;
    }

    // 살인마 촬영 목표 게이지를 보여준다.
    private void ShowCameraGoal(GameManager gm)
    {
        Show();

        if (objectiveSlider != null)
            objectiveSlider.value = gm.KillerDetectProgress01;

        if (objectiveText != null)
        {
            if (gm.IsKillerDetectComplete)
                objectiveText.text = waitingEvidenceText;
            else
                objectiveText.text = cameraGoalText;
        }
    }

    // 컴퓨터 업로드 진행도 게이지를 보여준다.
    private void ShowUploadGoal()
    {
        Show();

        if (objectiveSlider != null)
            objectiveSlider.value = targetComputer.UploadProgress01;

        if (objectiveText != null)
            objectiveText.text = uploadGoalText;
    }

    // 탈출문 개방 대기 시간 게이지를 보여준다.
    private void ShowGateTimer()
    {
        Show();

        if (objectiveSlider != null)
            objectiveSlider.value = targetComputer.GateRemain01;

        if (objectiveText != null)
            objectiveText.text = gateTimerText + " " + Mathf.CeilToInt(targetComputer.GateRemainTime) + "초";
    }

    // UI를 표시한다.
    private void Show()
    {
        if (root != null && !root.activeSelf)
            root.SetActive(true);
    }

    // UI를 숨긴다.
    private void Hide()
    {
        if (objectiveSlider != null)
            objectiveSlider.value = 0f;

        if (objectiveText != null)
            objectiveText.text = "";

        if (root != null && root.activeSelf)
            root.SetActive(false);
    }
}