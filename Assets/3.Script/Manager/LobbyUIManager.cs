using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUIManager : MonoBehaviour
{
    public static LobbyUIManager Instance { get; private set; }

    [Header("Role Select Buttons")]
    [SerializeField] private Button killerButton;
    [SerializeField] private Button survivorButton;

    [Header("Lobby Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button readyButton;
    [SerializeField] private Button returnButton;

    [Header("Lobby Text")]
    [SerializeField] private TMP_Text readyCountText;
    [SerializeField] private Text readyButtonText;

    [Header("Ready State UI")]
    [SerializeField] private RectTransform ready1Object;
    [SerializeField] private RectTransform ready2Object;
    [SerializeField] private RectTransform ready3Object;
    [SerializeField] private RectTransform ready4Object;

    [Header("Ready UI Position - Killer View")]
    [SerializeField] private Vector2 killerReady1Pos;
    [SerializeField] private Vector2 killerReady2Pos;
    [SerializeField] private Vector2 killerReady3Pos;
    [SerializeField] private Vector2 killerReady4Pos;

    [Header("Ready UI Position - Survivor View")]
    [SerializeField] private Vector2 survivorReady1Pos;
    [SerializeField] private Vector2 survivorReady2Pos;
    [SerializeField] private Vector2 survivorReady3Pos;
    [SerializeField] private Vector2 survivorReady4Pos;

    [Header("Loading UI")]
    [SerializeField] private GameObject loadingPanel;

    private bool isReady;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        ShowRoleSelectUI();
        ShowLoading(false);
    }

    public void OnClickConnectKiller()
    {
        if (CustomNetworkManager.Instance == null)
        {
            Debug.LogError("[UIManager] CustomNetworkManager Instance가 없습니다.");
            return;
        }

        CustomNetworkManager.Instance.ConnectAsKiller();
    }

    public void OnClickConnectSurvivor()
    {
        if (CustomNetworkManager.Instance == null)
        {
            Debug.LogError("[UIManager] CustomNetworkManager Instance가 없습니다.");
            return;
        }

        CustomNetworkManager.Instance.ConnectAsSurvivor();
    }

    public void OnClickBackButton()
    {
        if (CustomNetworkManager.Instance == null)
            return;

        isReady = false;

        CustomNetworkManager.Instance.BackToRoleSelect();
        ShowRoleSelectUI();
    }

    public void OnClickReadyButton()
    {
        if (CustomNetworkManager.Instance == null)
        {
            Debug.LogError("[UIManager] CustomNetworkManager Instance가 없습니다.");
            return;
        }

        isReady = !isReady;

        CustomNetworkManager.Instance.RequestSurvivorReady(isReady);

        UpdateReadyButtonView();
    }

    public void OnClickStartButton()
    {
        if (CustomNetworkManager.Instance == null)
        {
            Debug.LogError("[UIManager] CustomNetworkManager Instance가 없습니다.");
            return;
        }

        CustomNetworkManager.Instance.RequestStartGame();
    }

    private void ApplyReadyUIPositionForKiller()
    {
        SetReadyUIPosition(ready1Object, killerReady1Pos);
        SetReadyUIPosition(ready2Object, killerReady2Pos);
        SetReadyUIPosition(ready3Object, killerReady3Pos);
        SetReadyUIPosition(ready4Object, killerReady4Pos);
    }

    private void ApplyReadyUIPositionForSurvivor()
    {
        SetReadyUIPosition(ready1Object, survivorReady1Pos);
        SetReadyUIPosition(ready2Object, survivorReady2Pos);
        SetReadyUIPosition(ready3Object, survivorReady3Pos);
        SetReadyUIPosition(ready4Object, survivorReady4Pos);
    }

    private void SetReadyUIPosition(RectTransform readyObject, Vector2 anchoredPosition)
    {
        if (readyObject == null)
            return;

        readyObject.anchoredPosition = anchoredPosition;
    }

    public void ShowLoading(bool isActive)
    {
        if (loadingPanel != null)
            loadingPanel.SetActive(isActive);
    }

    public void ShowRoleSelectUI()
    {
        SetButtonActive(killerButton, true);
        SetButtonActive(survivorButton, true);

        SetButtonActive(startButton, false);
        SetButtonActive(readyButton, false);
        SetButtonActive(returnButton, false);

        SetReadyCountActive(false);

        SetStartButtonInteractable(false);

        isReady = false;
        UpdateReadyButtonView();
        SetLobbyReadyCount(0, 0);
        SetReadySlotUI(false, false, false, false);
    }

    public void ShowKillerLobbyUI()
    {
        SetButtonActive(killerButton, false);
        SetButtonActive(survivorButton, false);

        SetButtonActive(startButton, true);
        SetButtonActive(readyButton, false);
        SetButtonActive(returnButton, true);

        SetReadyCountActive(true);

        SetStartButtonInteractable(false);

        isReady = false;
        UpdateReadyButtonView();

        ApplyReadyUIPositionForKiller();
    }

    public void ShowSurvivorLobbyUI()
    {
        SetButtonActive(killerButton, false);
        SetButtonActive(survivorButton, false);

        SetButtonActive(startButton, false);
        SetButtonActive(readyButton, true);
        SetButtonActive(returnButton, true);

        SetReadyCountActive(true);

        isReady = false;
        UpdateReadyButtonView();

        ApplyReadyUIPositionForSurvivor();
    }

    public void SetStartButtonInteractable(bool value)
    {
        if (startButton != null)
            startButton.interactable = value;
    }

    public void SetLobbyReadyCount(int readyCount, int survivorCount)
    {
        if (readyCountText != null)
            readyCountText.text = $"{readyCount}/{survivorCount}";
    }

    public void SetReadySlotUI(bool ready1, bool ready2, bool ready3, bool ready4)
    {
        SetReadyObjectActive(ready1Object, ready1);
        SetReadyObjectActive(ready2Object, ready2);
        SetReadyObjectActive(ready3Object, ready3);
        SetReadyObjectActive(ready4Object, ready4);
    }

    private void SetReadyObjectActive(RectTransform readyObject, bool value)
    {
        if (readyObject != null)
            readyObject.gameObject.SetActive(value);
    }

    private void SetButtonActive(Button button, bool isActive)
    {
        if (button != null)
            button.gameObject.SetActive(isActive);
    }

    private void SetReadyCountActive(bool isActive)
    {
        if (readyCountText != null)
            readyCountText.gameObject.SetActive(isActive);
    }

    private void UpdateReadyButtonView()
    {
        if (readyButtonText == null && readyButton != null)
            readyButtonText = readyButton.GetComponentInChildren<Text>();

        if (readyButtonText == null)
            return;

        readyButtonText.text = isReady ? "준비 완료" : "준비 중";
    }

    public void DisableCanvas()
    {
        ShowRoleSelectUI();
    }
}