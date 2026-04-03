using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class SurvivorInput : NetworkBehaviour
{
    private InputSystem inputSys;

    public Vector2 Move
    {
        get
        {
            if (inputSys == null)
                return Vector2.zero;

            return inputSys.Player.Move.ReadValue<Vector2>();
        }
    }

    public Vector2 Look
    {
        get
        {
            if (inputSys == null)
                return Vector2.zero;

            return inputSys.Player.Look.ReadValue<Vector2>();
        }
    }

    public bool IsRunning
    {
        get
        {
            if (inputSys == null)
                return false;

            return inputSys.Player.Run.IsPressed();
        }
    }

    public bool IsCrouching
    {
        get
        {
            if (inputSys == null)
                return false;

            return inputSys.Player.Crouch.IsPressed();
        }
    }

    public bool IsInteracting1
    {
        get
        {
            if (inputSys == null)
                return false;

            return inputSys.Player.Interact1.IsPressed();
        }
    }

    public bool IsInteracting2
    {
        get
        {
            if (inputSys == null)
                return false;

            return inputSys.Player.Interact2.WasPressedThisFrame();
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
        {
            inputSys.Player.Disable();
        }
    }
}