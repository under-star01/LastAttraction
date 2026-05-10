using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class SurvivorInput : NetworkBehaviour
{
    private InputSystem inputSys;

    [Header("입력 상태")]
    [SerializeField] private bool lockCursorWhenInputEnabled = true;

    // 서버가 관리하는 생존자 입력 가능 여부
    // false면 로컬 플레이어여도 이동, 시야, 상호작용, 스킬 입력을 모두 무시한다.
    [SyncVar(hook = nameof(OnInputEnabledChanged))]
    private bool canReceiveInput = false;

    public bool CanReceiveInput => canReceiveInput;

    private SurvivorCameraSkill camera;

    private void Awake()
    {
        camera = GetComponent<SurvivorCameraSkill>();

        if (camera == null)
            Debug.LogError("[SurvivorInput] SurvivorCameraSkill 컴포넌트를 찾지 못했습니다.", this);
    }

    // 이동 입력
    public Vector2 Move
    {
        get
        {
            if (!CanReadInput())
                return Vector2.zero;

            return inputSys.Player.Move.ReadValue<Vector2>();
        }
    }

    // 시야 회전 입력
    public Vector2 Look
    {
        get
        {
            if (!CanReadInput())
                return Vector2.zero;

            return inputSys.Player.Look.ReadValue<Vector2>();
        }
    }

    // 달리기 입력
    public bool IsRunning
    {
        get
        {
            if (!CanReadInput())
                return false;

            return inputSys.Player.Run.IsPressed();
        }
    }

    // 앉기 입력
    public bool IsCrouching
    {
        get
        {
            if (!CanReadInput())
                return false;

            return inputSys.Player.Crouch.IsPressed();
        }
    }

    // Hold 타입 상호작용 입력
    public bool IsInteracting1
    {
        get
        {
            if (!CanReadInput())
                return false;

            return inputSys.Player.Interact1.IsPressed();
        }
    }

    // Press 타입 상호작용 입력
    public bool IsInteracting2
    {
        get
        {
            if (!CanReadInput())
                return false;

            return inputSys.Player.Interact2.WasPressedThisFrame();
        }
    }

    // 우클릭 홀드 카메라 스킬 입력
    public bool IsCameraSkillPressed
    {
        get
        {
            if (!CanReadInput())
                return false;

            return inputSys.Player.CameraSkill.IsPressed();
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        inputSys = new InputSystem();
        inputSys.Player.Enable();

        // 생성 직후 현재 입력 상태 반영
        ApplyInputMode(canReceiveInput);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        if (isLocalPlayer && inputSys != null)
        {
            inputSys.Player.Disable();
            inputSys = null;
        }
    }

    // 서버에서 호출하는 입력 상태 변경 함수
    [Server]
    public void SetInputEnabledServer(bool value)
    {
        canReceiveInput = value;
    }

    // 실제 입력값을 읽어도 되는지 검사
    private bool CanReadInput()
    {
        if (!isLocalPlayer)
            return false;

        if (inputSys == null)
            return false;

        if (!canReceiveInput)
            return false;

        return true;
    }

    // 서버에서 canReceiveInput 값이 바뀌면 클라이언트에서도 호출된다.
    private void OnInputEnabledChanged(bool oldValue, bool newValue)
    {
        ApplyInputMode(newValue);
    }

    // 로비 / 게임 상태에 맞게 커서 상태를 바꾼다.
    private void ApplyInputMode(bool value)
    {
        if (!isLocalPlayer)
            return;

        if (camera == null)
        {
            camera = GetComponent<SurvivorCameraSkill>();

            if (camera == null)
            {
                Debug.LogError("[SurvivorInput] camera가 null이라 ApplyInputMode를 적용할 수 없습니다.", this);
                return;
            }
        }

        // 입력 잠금 상태 = 로비
        if (!value)
        {
            camera.ApplyLobbyView(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        // 입력 가능 상태 = 게임 중
        if (lockCursorWhenInputEnabled)
        {
            camera.ApplyLobbyView(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}