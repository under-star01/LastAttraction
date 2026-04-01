using UnityEngine;
using UnityEngine.InputSystem;

public class TestMng : MonoBehaviour
{
    public GameObject killerObject;    // 살인마 캐릭터 (카메라 포함)
    public GameObject survivorObject;  // 생존자 캐릭터 (카메라 포함)

    private static InputSystem _inputSys;

    public static InputSystem inputSys
    {
        get
        {
            if (_inputSys == null)
            {
                _inputSys = new InputSystem();
                _inputSys.Enable(); // 생성과 동시에 켜기
            }
            return _inputSys;
        }
    }

    private Camera killerCam;
    private Camera survivorCam;
    private AudioListener killerListener;
    private AudioListener survivorListener;

    private bool isKillerActive = true;

    private void Awake()
    {
        inputSys.Enable();

        killerCam = killerObject.GetComponentInChildren<Camera>();
        survivorCam = survivorObject.GetComponentInChildren<Camera>();
        killerListener = killerObject.GetComponentInChildren<AudioListener>();
        survivorListener = survivorObject.GetComponentInChildren<AudioListener>();
    }

    void Start()
    {
        // 시작 시 초기 상태 설정
        UpdateCharacterState();
    }

    void Update()
    {
        // F1 키 입력 시 전환
        if (Input.GetKeyDown(KeyCode.F1))
        {
            isKillerActive = !isKillerActive;
            UpdateCharacterState();
        }
    }

    private void UpdateCharacterState()
    {
        if (!isKillerActive)
        {
            killerObject.GetComponent<TrapHandler>().ForceCancelTrapMode();
        }

        if (killerCam != null) killerCam.enabled = isKillerActive;
        if (survivorCam != null) survivorCam.enabled = !isKillerActive;

        if (killerListener != null) killerListener.enabled = isKillerActive;
        if (survivorListener != null) survivorListener.enabled = !isKillerActive;

        ChangeMode(isKillerActive);

        Debug.Log(isKillerActive ? "살인마 조작 모드" : "생존자 조작 모드");
    }

    public static void ChangeMode(bool toKiller)
    {
        if (inputSys == null) return;

        if (toKiller)
        {
            inputSys.Player.Disable(); // 생존자 인풋 끄기
            inputSys.Killer.Enable();  // 살인마 인풋 켜기
        }
        else
        {
            inputSys.Killer.Disable(); // 살인마 인풋 끄기
            inputSys.Player.Enable();  // 생존자 인풋 켜기
        }
    }
}