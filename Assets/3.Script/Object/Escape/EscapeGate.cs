using Mirror;
using UnityEngine;

public class EscapeGate : NetworkBehaviour
{
    [Header("문 애니메이터")]
    [SerializeField] private Animator animator;

    [Header("애니메이션 트리거 이름")]
    [SerializeField] private string openTriggerName = "Open";

    // 탈출문이 열렸는지 서버에서 동기화한다.
    [SyncVar(hook = nameof(OnOpenChanged))]
    private bool isOpen;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        // 애니메이터가 비어 있으면 자식에서 자동으로 찾는다.
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 늦게 접속한 클라이언트도 이미 열린 문이면 Open 상태를 적용한다.
        if (isOpen)
            PlayOpen();
    }

    // 문 개방 대기 시간이 끝나면 GameManager가 서버에서 호출한다.
    [Server]
    public void Open()
    {
        if (isOpen)
            return;

        isOpen = true;

        // 서버에서도 즉시 Open 애니메이션을 실행한다.
        PlayOpen();

        Debug.Log($"[EscapeGate] 탈출문 열림 애니메이션 실행: {name}");
    }

    // isOpen 값이 동기화되면 클라이언트에서도 Open 애니메이션을 실행한다.
    private void OnOpenChanged(bool oldValue, bool newValue)
    {
        if (!newValue)
            return;

        PlayOpen();
    }

    // 실제 문 열림 애니메이션 트리거를 실행한다.
    private void PlayOpen()
    {
        if (animator == null)
            return;

        if (string.IsNullOrEmpty(openTriggerName))
            return;

        animator.ResetTrigger(openTriggerName);
        animator.SetTrigger(openTriggerName);
    }
}