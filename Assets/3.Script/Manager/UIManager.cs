using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Buttons")]
    [SerializeField] private Button killerButton;
    [SerializeField] private Button survivorButton;
    [SerializeField] private GameObject loadingPanel;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        BindButtons();
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

        CustomNetworkManager.Instance.BackToRoleSelect();
    }

    public void ShowLoading(bool isActive)
    {
        if (loadingPanel != null)
            loadingPanel.SetActive(isActive);
    }
}