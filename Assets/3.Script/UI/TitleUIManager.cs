using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleUIManager : MonoBehaviour
{
    [Header("Common")]
    [SerializeField] private TMP_InputField inputId;
    [SerializeField] private TMP_InputField inputPassword;
    [SerializeField] private TMP_Text logText;
    [SerializeField] private GameObject logObject;

    [Header("Register UI")]
    [SerializeField] private GameObject registerPanel;
    [SerializeField] private TMP_InputField inputNickname;

    [Header("Login UI")]
    [SerializeField] private GameObject loginPanel;

    [Header("Scene")]
    [SerializeField] private string lobbySceneName = "Lobby";

    private void Start()
    {
        ShowLoginUI();
        SetLog(string.Empty, false);

        if (Application.isBatchMode)
        {
            Debug.Log("[TitleUIManager] 배치모드 서버이므로 인증 서버 접속을 시도하지 않습니다.");
            return;
        }

        if (AuthNetworkManager.Instance != null)
        {
            AuthNetworkManager.Instance.ConnectToAuthServer();
        }
        else
        {
            SetLog("Auth network manager is missing.", true);
        }
    }

    // UI 전환

    public void ShowLoginUI()
    {
        loginPanel.SetActive(true);
        registerPanel.SetActive(false);

        ClearInputs();
        SetLog(string.Empty, false);
    }

    public void ShowRegisterUI()
    {
        loginPanel.SetActive(false);
        registerPanel.SetActive(true);

        ClearInputs();
        SetLog(string.Empty, false);
    }

    private void ClearInputs()
    {
        if (inputId != null)
            inputId.text = string.Empty;

        if (inputPassword != null)
            inputPassword.text = string.Empty;

        if (inputNickname != null)
            inputNickname.text = string.Empty;
    }

    // 버튼 이벤트

    public void OnClickLogin()
    {
        string loginId = inputId.text.Trim();
        string password = inputPassword.text;

        if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(password))
        {
            SetLog("Please enter ID and password.", true);
            return;
        }

        if (AuthPlayer.Local == null)
        {
            SetLog("Not connected to auth server.", true);
            return;
        }

        SetLog("Login request sent...", true);
        AuthPlayer.Local.RequestLogin(loginId, password);
    }

    public void OnClickOpenRegister()
    {
        ShowRegisterUI();
    }

    public void OnClickBackFromRegister()
    {
        ShowLoginUI();
    }

    public void OnClickCreateAccount()
    {
        string loginId = inputId.text.Trim();
        string password = inputPassword.text;
        string nickname = inputNickname.text.Trim();

        if (string.IsNullOrWhiteSpace(loginId) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(nickname))
        {
            SetLog("Please fill in all fields.", true);
            return;
        }

        if (AuthPlayer.Local == null)
        {
            SetLog("Not connected to auth server.", true);
            return;
        }

        SetLog("Register request sent...", true);
        AuthPlayer.Local.RequestRegister(loginId, password, nickname);
    }

    // AuthPlayer 에서 호출

    public void OnRegisterResult(RegisterResult result)
    {
        switch (result)
        {
            case RegisterResult.Success:
                SetLog("Register success. Please log in.", true);
                inputNickname.text = string.Empty;
                ShowLoginUI();
                break;

            case RegisterResult.InvalidInput:
                SetLog("Invalid input.", true);
                break;

            case RegisterResult.DuplicateLoginId:
                SetLog("ID already exists.", true);
                break;

            case RegisterResult.DuplicateNickname:
                SetLog("Nickname already exists.", true);
                break;

            default:
                SetLog("Register failed.", true);
                break;
        }
    }

    public void OnLoginResult(LoginResult result)
    {
        switch (result)
        {
            case LoginResult.InvalidInput:
                SetLog("Please check ID and password.", true);
                break;

            case LoginResult.UserNotFound:
                SetLog("User not found.", true);
                break;

            case LoginResult.WrongPassword:
                SetLog("Wrong password.", true);
                break;

            default:
                SetLog("Login failed.", true);
                break;
        }
    }

    public void OnLoginSuccess(LoginUserData userData)
    {
        SetLog("Login success.", true);

        if (GameSession.Instance != null)
        {
            GameSession.Instance.SetLoginData(userData);
        }

        if (AuthNetworkManager.Instance != null)
        {
            AuthNetworkManager.Instance.DisconnectFromAuthServer();
        }

        SceneManager.LoadScene(lobbySceneName);
    }

    // 공통

    private void SetLog(string message, bool show)
    {
        if (logText != null)
            logText.text = message;

        if (logObject != null)
            logObject.SetActive(show);
    }
}