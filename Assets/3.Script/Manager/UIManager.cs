using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Role Select Buttons")]
    [SerializeField] private Button killerButton;
    [SerializeField] private Button survivorButton;
    [SerializeField] private Button returnButton;

    [Header("Start Game UI")]
    [SerializeField] private GameObject startGameUI;
    [SerializeField] private Button startButton;
    [SerializeField] private Button readyButton;
    [SerializeField] private TMP_Text readyCntText;

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

        if (returnButton != null)
        {
            returnButton.onClick.RemoveAllListeners();
            returnButton.onClick.AddListener(OnClickBackButton);
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
        UpdateReadyButtonView();

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
        SetButtonActive(returnButton, false);

        if (startGameUI != null)
            startGameUI.SetActive(false);

        SetButtonActive(startButton, false);
        SetButtonActive(readyButton, false);

        SetReadyCntActive(false);
        SetStartButtonInteractable(false);

        isReady = false;
        UpdateReadyButtonView();
        SetLobbyReadyCount(0, 0);
    }

    public void ShowKillerLobbyUI()
    {
        SetButtonActive(killerButton, false);
        SetButtonActive(survivorButton, false);
        SetButtonActive(returnButton, true);

        if (startGameUI != null)
            startGameUI.SetActive(true);

        SetButtonActive(startButton, true);
        SetButtonActive(readyButton, false);
        SetReadyCntActive(true);

        SetStartButtonInteractable(false);
    }

    public void ShowSurvivorLobbyUI()
    {
        SetButtonActive(killerButton, false);
        SetButtonActive(survivorButton, false);
        SetButtonActive(returnButton, true);

        if (startGameUI != null)
            startGameUI.SetActive(true);

        SetButtonActive(startButton, false);
        SetButtonActive(readyButton, true);
        SetReadyCntActive(true);

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
        if (readyCntText != null)
            readyCntText.text = $"{readyCount}/{survivorCount}";
    }

    private void SetButtonActive(Button button, bool isActive)
    {
        if (button != null)
            button.gameObject.SetActive(isActive);
    }

    private void SetReadyCntActive(bool isActive)
    {
        if (readyCntText != null)
            readyCntText.gameObject.SetActive(isActive);
    }

    private void UpdateReadyButtonView()
    {
        if (readyButton == null)
            return;

        TMP_Text text = readyButton.GetComponentInChildren<TMP_Text>();

        if (text != null)
            text.text = isReady ? "READY" : "READY?";
    }

    public void DisableCanvas()
    {
        ShowRoleSelectUI();
    }
}