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
    public Vector2 Move => moveAction.ReadValue<Vector2>();             // WASD
    public Vector2 Look => lookAction.ReadValue<Vector2>();             // 마우스 이동
    public bool IsRunning => runAction.IsPressed();                     // Shift
    public bool IsCrouching => crouchAction.IsPressed();                // Ctrl
    public bool IsInteracting1 => interactAction1.IsPressed();          // 좌클릭 유지
    public bool IsInteracting2 => interactAction2.WasPressedThisFrame(); // Space 1회 입력

    private void Awake()
    {
        playerInput = GetComponent<PlayerInput>();

        // Input Actions 이름과 연결
        moveAction = playerInput.actions["Move"];
        lookAction = playerInput.actions["Look"];
        runAction = playerInput.actions["Run"];
        crouchAction = playerInput.actions["Crouch"];
        interactAction1 = playerInput.actions["Interact1"];
        interactAction2 = playerInput.actions["Interact2"];
    }
}