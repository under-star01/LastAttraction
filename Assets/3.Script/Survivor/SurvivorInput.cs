using UnityEngine;
using UnityEngine.InputSystem;

public class SurvivorInput : MonoBehaviour
{
    private PlayerInput playerInput;

    // Input Action 참조
    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction runAction;
    private InputAction crouchAction;
    private InputAction interactAction1;
    private InputAction interactAction2;

    // 외부에서 쉽게 가져다 쓸 수 있도록 프로퍼티로 공개
    public Vector2 Move => TestMng.inputSys.Player.Move.ReadValue<Vector2>();             // WASD
    public Vector2 Look => TestMng.inputSys.Player.Look.ReadValue<Vector2>();             // 마우스 이동
    public bool IsRunning => TestMng.inputSys.Player.Run.IsPressed();                     // Shift
    public bool IsCrouching => TestMng.inputSys.Player.Crouch.IsPressed();                // Ctrl
    public bool IsInteracting1 => TestMng.inputSys.Player.Interact1.IsPressed();          // 좌클릭 유지
    public bool IsInteracting2 => TestMng.inputSys.Player.Interact2.WasPressedThisFrame(); // Space 1회 입력

}