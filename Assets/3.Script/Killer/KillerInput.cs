using UnityEngine;

public class KillerInput : MonoBehaviour
{
    public Vector2 Move => TestMng.inputSys?.Killer.Move.ReadValue<Vector2>() ?? Vector2.zero;
    public Vector2 Look => TestMng.inputSys?.Killer.Look.ReadValue<Vector2>() ?? Vector2.zero;
    public bool IsAttackPressed => TestMng.inputSys?.Killer.Attack.IsPressed() ?? false;
    public bool IsInteractPressed => TestMng.inputSys?.Killer.Interact1.WasPressedThisFrame() ?? false;
    public bool IsPickUpPressed => TestMng.inputSys?.Killer.Interact2.WasPressedThisFrame() ?? false;
}