using Mirror;
using UnityEngine;

public class EscapeGate : NetworkBehaviour
{
    [Header("문 오브젝트")]
    [SerializeField] private GameObject gateObject;

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

    // 업로드가 완료되면 GameManager가 서버에서 호출한다.
    [Server]
    public void Open()
    {
        if (isOpen)
            return;

        isOpen = true;

        // 서버에서도 즉시 문 큐브를 비활성화한다.
        ApplyOpen(true);

        Debug.Log($"[EscapeGate] 탈출문 열림: {name}");
    }

    // isOpen 값이 동기화되면 클라이언트에서도 문 상태를 적용한다.
    private void OnOpenChanged(bool oldValue, bool newValue)
    {
        ApplyOpen(newValue);
    }

    // 문이 열리면 오브젝트를 꺼서 통과 가능하게 만든다.
    private void ApplyOpen(bool open)
    {
        if (gateObject != null)
            gateObject.SetActive(!open);
    }
}