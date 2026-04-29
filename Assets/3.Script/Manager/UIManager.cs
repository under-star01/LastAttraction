using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Role Select Buttons")]
    [SerializeField] private Button killerButton;
    [SerializeField] private Button survivorButton;

    [Header("Lobby Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button readyButton;
    [SerializeField] private Button returnButton;

    [Header("Lobby Text")]
    [SerializeField] private TMP_Text readyCountText;

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

        BindButtons();
        ShowRoleSelectUI();
        ShowLoading(false);
    }

    private void BindButtons()
    {
        if (killerButton != null)
        {
            killerButton.onClick.RemoveAllListeners();
            killerButton.onClick.AddListener(OnClickConnectKiller);
        }

        if (survivorButton != null)
        {
            survivorButton.onClick.RemoveAllListeners();
            survivorButton.onClick.AddListener(OnClickConnectSurvivor);
        }

        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(OnClickStartButton);
        }

        if (readyButton != null)
        {
            readyButton.onClick.RemoveAllListeners();
            readyButton.onClick.AddListener(OnClickReadyButton);
        }

        if (returnButton != null)
        {
            returnButton.onClick.RemoveAllListeners();
            returnButton.onClick.AddListener(OnClickBackButton);
        }
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

    private void OnClickReadyButton()
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

    private void OnClickStartButton()
    {
        if (CustomNetworkManager.Instance == null)
        {
            Debug.LogError("[UIManager] CustomNetworkManager Instance가 없습니다.");
            return;
        }

        CustomNetworkManager.Instance.RequestStartGame();
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
        if (readyButton == null)
            return;

        TMP_Text buttonText = readyButton.GetComponentInChildren<TMP_Text>();

        if (buttonText == null)
            return;

        buttonText.text = isReady ? "READY" : "READY?";
    }

    public void DisableCanvas()
    {
        ShowRoleSelectUI();
    }
}