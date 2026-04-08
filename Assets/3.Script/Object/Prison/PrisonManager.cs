using Mirror;
using UnityEngine;

public class PrisonManager : NetworkBehaviour
{
    public static PrisonManager Instance { get; private set; }

    [Header("감옥 4개")]
    [SerializeField] private Prison[] prisons;   // 감옥 4개 연결

    private void Awake()
    {
        Instance = this;
    }

    // 비어있는 감옥 하나 찾기
    [Server]
    public Prison GetEmpty()
    {
        if (prisons == null || prisons.Length == 0)
            return null;

        for (int i = 0; i < prisons.Length; i++)
        {
            if (prisons[i] == null)
                continue;

            if (!prisons[i].IsOccupied)
                return prisons[i];
        }

        // 전부 차 있으면 null
        return null;
    }
}