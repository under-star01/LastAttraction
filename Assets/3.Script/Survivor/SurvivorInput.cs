using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class SurvivorInput : NetworkBehaviour
{
    private InputSystem inputSys;

    // 이동 입력
    public Vector2 Move
    {
        get
        {
            if (inputSys == null)
                return Vector2.zero;

            return inputSys.Player.Move.ReadValue<Vector2>();
        }
    }

    // 시야 회전 입력
    public Vector2 Look
    {
        get
        {
            if (inputSys == null)
                return Vector2.zero;

            return inputSys.Player.Look.ReadValue<Vector2>();
        }
    }

    // 달리기 입력
    public bool IsRunning
    {
        get
        {
            if (inputSys == null)
                return false;

            return inputSys.Player.Run.IsPressed();
        }
    }

    // 앉기 입력
    public bool IsCrouching
    {
        get
        {
            if (inputSys == null)
                return false;

            return inputSys.Player.Crouch.IsPressed();
        }
    }

    // Hold 타입 상호작용 입력
    public bool IsInteracting1
    {
        get
        {
            if (inputSys == null)
                return false;

            return inputSys.Player.Interact1.IsPressed();
        }
    }

    // Press 타입 상호작용 입력
    public bool IsInteracting2
    {
        get
        {
            if (inputSys == null)
                return false;

            return inputSys.Player.Interact2.WasPressedThisFrame();
        }
    }

    // 우클릭 홀드 카메라 스킬 입력
    public bool IsCameraSkillPressed
    {
        get
        {
            if (inputSys == null)
                return false;

            return inputSys.Player.CameraSkill.IsPressed();
        }
    }

    public override void OnStartLocalPlayer()
    {
        inputSys = new InputSystem();
        inputSys.Player.Enable();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        if (isLocalPlayer && inputSys != null)
            inputSys.Player.Disable();
    }
}