using Mirror;
using UnityEngine;

public class PrisonManager : NetworkBehaviour
{
    public static PrisonManager Instance { get; private set; }

    [Header("감옥 4개")]
    [SerializeField] private Prison[] prisons;   // 씬에 배치된 감옥들을 연결한다.

    private void Awake()
    {
        Instance = this;
    }

    // 비어있고, 폐쇄되지 않은 감옥 하나를 찾는다.
    // 시간 초과 사망자가 나온 감옥은 IsDisabled가 true라서 다시 선택되지 않는다.
    [Server]
    public Prison GetEmpty()
    {
        if (prisons == null || prisons.Length == 0)
            return null;

        for (int i = 0; i < prisons.Length; i++)
        {
            if (prisons[i] == null)
                continue;

            // 사망자가 나온 감옥은 영구 폐쇄 상태이므로 제외한다.
            if (prisons[i].IsDisabled)
                continue;

            // 사용 가능하고 비어있는 감옥만 반환한다.
            if (!prisons[i].IsOccupied)
                return prisons[i];
        }

        // 전부 사용 중이거나 폐쇄된 상태면 null
        return null;
    }
}