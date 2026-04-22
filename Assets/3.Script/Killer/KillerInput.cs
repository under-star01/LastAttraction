using UnityEngine;
using Mirror;

public class KillerInput : NetworkBehaviour
{
    private InputSystem inputSys;

    // 프로퍼티들: inputSys가 활성화된 경우에만 값을 반환
    public Vector2 Move => inputSys?.Killer.Move.ReadValue<Vector2>() ?? Vector2.zero;
    public Vector2 Look => inputSys?.Killer.Look.ReadValue<Vector2>() ?? Vector2.zero;

    // 공격: 런지용(IsPressed)과 설치 확정용(WasPressedThisFrame) 구분
    public bool IsAttackPressed => inputSys?.Killer.Attack.IsPressed() ?? false;
    public bool IsAttackWasPressed => inputSys?.Killer.Attack.WasPressedThisFrame() ?? false;

    public bool IsInteractPressed => inputSys?.Killer.Interact1.WasPressedThisFrame() ?? false;
    public bool IsPickUpPressed => inputSys?.Killer.Interact2.WasPressedThisFrame() ?? false;
    public bool IsTrapModePressed => inputSys?.Killer.TrapMode.WasPressedThisFrame() ?? false;

    public override void OnStartLocalPlayer()
    {
        // 로컬 플레이어만 입력을 생성하고 활성화
        inputSys = new InputSystem();
        inputSys.Enable();
    }

    private void OnDisable()
    {
        // 오브젝트가 비활성화될 때 입력 시스템도 함께 종료 (메모리 누수 방지)
        if (isLocalPlayer && inputSys != null)
        {
            inputSys.Disable();
        }
    }
}