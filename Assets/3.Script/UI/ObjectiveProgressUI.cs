using Mirror;
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
    [SerializeField] private string objectiveTextValue = "목표 진행도";
    [SerializeField] private string uploadGoalText = "컴퓨터에 증거 업로드";
    [SerializeField] private string gateTimerText = "탈출문 개방까지";

    private void Awake()
    {
        // Root에는 자기 자신이 아니라 실제 UI 패널 오브젝트를 넣어야 한다.
        if (root == gameObject)
        {
            Debug.LogWarning(
                "[ObjectiveProgressUI] Root에 자기 자신을 넣으면 오브젝트가 꺼져서 Update가 실행되지 않습니다. Root는 자식 UI 패널로 연결하세요.",
                this
            );

            root = null;
        }

        // Slider는 0~1 값으로만 사용한다.
        if (objectiveSlider != null)
        {
            objectiveSlider.minValue = 0f;
            objectiveSlider.maxValue = 1f;
            objectiveSlider.value = 0f;
            objectiveSlider.interactable = false;
        }

        // 시작할 때는 UI를 숨긴다.
        Hide();
    }

    private void Update()
    {
        // 생존자로 플레이 중일 때만 목표 UI를 표시한다.
        if (!IsLocalSurvivor())
        {
            Hide();
            return;
        }

        GameManager gm = GameManager.Instance;

        if (gm == null)
        {
            Hide();
            return;
        }

        // 업로드 컴퓨터가 비어 있으면 씬에서 자동으로 하나 찾는다.
        FindTargetComputer();

        // 문이 열렸으면 목표 UI를 숨긴다.
        if (IsGateOpened(gm))
        {
            Hide();
            return;
        }

        // 업로드 완료 후 문 개방 대기 중이면 문 타이머 게이지를 보여준다.
        if (targetComputer != null && targetComputer.GateTimerVisible)
        {
            ShowGateTimer();
            return;
        }

        // 업로드 컴퓨터가 열렸거나 업로드가 진행됐다면 업로드 게이지를 보여준다.
        if (targetComputer != null && (targetComputer.IsOpen || targetComputer.UploadProgress01 > 0f))
        {
            ShowUploadGoal();
            return;
        }

        // 기본 상태에서는 통합 목표 게이지를 보여준다.
        ShowObjectiveGoal(gm);
    }

    // 현재 클라이언트의 로컬 플레이어가 생존자인지 확인한다.
    private bool IsLocalSurvivor()
    {
        if (NetworkClient.localPlayer == null)
            return false;

        return NetworkClient.localPlayer.GetComponent<SurvivorState>() != null;
    }

    // 목표 UI가 읽을 업로드 컴퓨터를 자동으로 찾는다.
    private void FindTargetComputer()
    {
        if (targetComputer != null)
            return;

        targetComputer = FindFirstObjectByType<UploadComputer>();
    }

    // 탈출문이 열렸는지 확인한다.
    private bool IsGateOpened(GameManager gm)
    {
        if (gm != null && gm.GateOpened)
            return true;

        if (targetComputer != null && targetComputer.GateOpened)
            return true;

        return false;
    }

    // 통합 목표 게이지를 표시한다.
    private void ShowObjectiveGoal(GameManager gm)
    {
        Show();

        if (objectiveSlider != null)
            objectiveSlider.value = gm.ObjectiveProgress01;

        if (objectiveText != null)
        {
            int percent = Mathf.RoundToInt(gm.ObjectiveProgress01 * 100f);
            objectiveText.text = objectiveTextValue + " " + percent + "%";
        }
    }

    // 컴퓨터 업로드 진행도 게이지를 표시한다.
    private void ShowUploadGoal()
    {
        Show();

        if (objectiveSlider != null)
            objectiveSlider.value = targetComputer.UploadProgress01;

        if (objectiveText != null)
        {
            int percent = Mathf.RoundToInt(targetComputer.UploadProgress01 * 100f);
            objectiveText.text = uploadGoalText + " " + percent + "%";
        }
    }

    // 탈출문 개방까지 남은 시간 게이지를 표시한다.
    private void ShowGateTimer()
    {
        Show();

        if (objectiveSlider != null)
            objectiveSlider.value = targetComputer.GateRemain01;

        if (objectiveText != null)
            objectiveText.text = gateTimerText + " " + Mathf.CeilToInt(targetComputer.GateRemainTime) + "초";
    }

    // 목표 UI 패널을 표시한다.
    private void Show()
    {
        if (root != null && !root.activeSelf)
            root.SetActive(true);
    }

    // 목표 UI 패널을 숨긴다.
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