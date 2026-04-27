using Mirror;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    // 어디서든 접근할 수 있는 GameManager
    public static GameManager Instance { get; private set; }

    [Header("생존자 입력 상태")]
    [SerializeField] private bool survivorInputEnabled;

    // 현재 생존자 입력 가능 여부
    public bool SurvivorInputEnabled => survivorInputEnabled;

    private void Awake()
    {
        // 중복 GameManager 제거
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 현재 GameManager를 저장
        Instance = this;

        // 씬이 바뀌어도 유지
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // 서버에서만 초기 입력 차단
        if (NetworkServer.active)
            SetAllSurvivorInput(false);
    }

    public void SetAllSurvivorInput(bool value)
    {
        // 서버가 아니면 실행하지 않음
        if (!NetworkServer.active)
            return;

        // 현재 입력 상태 저장
        survivorInputEnabled = value;

        // 접속 중인 모든 플레이어 검사
        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            // 플레이어 오브젝트가 없으면 건너뜀
            if (conn == null || conn.identity == null)
                continue;

            // 생존자 입력 컴포넌트 찾기
            SurvivorInput input = conn.identity.GetComponent<SurvivorInput>();

            // 생존자가 아니면 건너뜀
            if (input == null)
                continue;

            // 생존자 입력 상태 변경
            input.SetInputEnabledServer(value);
        }

        Debug.Log($"[GameManager] 생존자 입력 상태 변경: {value}");
    }

    public void ToggleAllSurvivorInput()
    {
        // 서버가 아니면 실행하지 않음
        if (!NetworkServer.active)
            return;

        // 현재 상태 반대로 변경
        SetAllSurvivorInput(!survivorInputEnabled);
    }

    public void StartGame()
    {
        // 게임 시작 시 입력 허용
        SetAllSurvivorInput(true);
    }

    public void EnterLobby()
    {
        // 로비 진입 시 입력 차단
        SetAllSurvivorInput(false);
    }
}