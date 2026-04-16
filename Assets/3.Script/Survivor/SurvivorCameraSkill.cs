using Mirror;
using UnityEngine;

// 우클릭 홀드 카메라 스킬
public class SurvivorCameraSkill : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private SurvivorInput input;
    [SerializeField] private SurvivorMove move;
    [SerializeField] private SurvivorActionState act;

    // 현재 스킬 사용 여부
    [SyncVar(hook = nameof(OnSkillChanged))]
    private bool isUse;

    public bool IsUse => isUse;

    private void Awake()
    {
        if (input == null)
            input = GetComponent<SurvivorInput>();

        if (move == null)
            move = GetComponent<SurvivorMove>();

        if (act == null)
            act = GetComponent<SurvivorActionState>();
    }

    private void Update()
    {
        if (!isLocalPlayer)
            return;

        bool want = false;

        if (input != null)
            want = input.IsCameraSkillPressed;

        // 조건이 안 되면 누르고 있어도 강제로 해제
        if (!CanUse())
            want = false;

        if (want != isUse)
            CmdSetSkill(want);
    }

    // 로컬에서 사용 가능 여부 확인
    private bool CanUse()
    {
        if (act == null)
            return false;

        return act.CanCam();
    }

    [Command]
    private void CmdSetSkill(bool value)
    {
        if (act == null)
        {
            value = false;
        }
        else if (value && !act.CanCam())
        {
            value = false;
        }

        isUse = value;
        act.SetCam(value);
    }

    // 값이 바뀌면 애니메이션 bool 동기화
    private void OnSkillChanged(bool oldValue, bool newValue)
    {
        if (move != null)
            move.SetCamAnim(newValue);
    }
}