using Mirror;
using UnityEngine;

public class EscapeGate : NetworkBehaviour
{
    [Header("문 큐브")]
    [SerializeField] private Renderer gateRenderer;
    [SerializeField] private Collider gateCollider;

    // 탈출문이 열렸는지 서버에서 동기화한다.
    [SyncVar(hook = nameof(OnOpenChanged))]
    private bool isOpen;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        // 시작 시 현재 문 상태를 적용한다.
        ApplyOpen(isOpen);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 늦게 접속한 클라이언트도 현재 문 상태를 적용한다.
        ApplyOpen(isOpen);
    }

    // 문 개방 대기 시간이 끝나면 GameManager가 서버에서 호출한다.
    [Server]
    public void Open()
    {
        if (isOpen)
            return;

        isOpen = true;

        // 서버에서도 즉시 문 표시와 충돌을 끈다.
        ApplyOpen(true);

        Debug.Log($"[EscapeGate] 탈출문 열림: {name}");
    }

    // isOpen 값이 동기화되면 클라이언트에서도 문 상태를 적용한다.
    private void OnOpenChanged(bool oldValue, bool newValue)
    {
        ApplyOpen(newValue);
    }

    // 문이 열리면 Renderer와 Collider만 꺼서 네트워크 오브젝트는 유지한다.
    private void ApplyOpen(bool open)
    {
        if (gateRenderer != null)
            gateRenderer.enabled = !open;

        if (gateCollider != null)
            gateCollider.enabled = !open;
    }
}