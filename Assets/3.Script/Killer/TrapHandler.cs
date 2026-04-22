using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class TrapHandler : NetworkBehaviour
{
    [Header("Settings")]
    public GameObject trapPrefab;
    public float maxInstallDist = 3f;
    public LayerMask groundMask;
    public LayerMask obstacleMask;

    private GameObject ghostInstance;
    private bool isBuildMode = false;

    public bool IsBuildMode => isBuildMode;

    private Camera cam;
    private KillerState state;
    private KillerInput killerInput;
    private Animator animator;

    // 서버에서 설치된 함정들을 관리할 리스트 (서버 전용) [cite: 2026-04-06]
    private readonly List<GameObject> spawnedTraps = new List<GameObject>();

    void Awake()
    {
        state = GetComponent<KillerState>();
        cam = GetComponentInChildren<Camera>();
        killerInput = GetComponent<KillerInput>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        // TestMng 참조 제거, killerInput 참조로 변경
        if (!isLocalPlayer || killerInput == null) return;

        // 1. 함정 모드 토글 (트랩 모드 키)
        if (killerInput.IsTrapModePressed)
        {
            ToggleTrapMode();
        }

        if (isBuildMode)
        {
            if (killerInput.IsAttackWasPressed) ConfirmInstallation();
            else if (ghostInstance != null) UpdateGhostPosition();
        }
    }

    private void ToggleTrapMode()
    {
        isBuildMode = !isBuildMode;

        if (isBuildMode)
        {
            if (ghostInstance == null)
            {
                ghostInstance = Instantiate(trapPrefab);
                if (ghostInstance.TryGetComponent(out TrapNode node)) node.enabled = false;
                SetGhostVisual(ghostInstance, 0.4f);
            }
            state.CmdChangeKillerState(KillerCondition.Planting);
        }
        else
        {
            CleanupGhost();

            state.CmdChangeKillerState(KillerCondition.Idle);
        }
    }

    private void ConfirmInstallation()
    {
        if (CanPlace(out Vector3 installPos))
        {
            // 서버에 설치 시작 요청 (상태 변경 및 FIFO 포함)
            CmdStartPlanting(installPos, ghostInstance.transform.rotation);
            // 로컬 모드 즉시 종료
            ExitBuildMode();
        }
    }

    //[Command] // 클라이언트가 요청하면 서버에서 실행 [cite: 2026-04-06]
    //private void CmdSpawnTrap(Vector3 pos)
    //{
    //    // 1. 개수 제한 체크 (FIFO 로직) [cite: 2026-04-06]
    //    // 리스트에 이미 5개가 있다면 가장 오래된 것(0번)을 제거합니다.
    //    while (spawnedTraps.Count >= 5)
    //    {
    //        GameObject oldestTrap = spawnedTraps[0];
    //        spawnedTraps.RemoveAt(0);

    //        if (oldestTrap != null)
    //        {
    //            // 서버와 모든 클라이언트 화면에서 동시에 삭제합니다. [cite: 2026-04-06]
    //            NetworkServer.Destroy(oldestTrap);
    //        }
    //    }

    //    // 2. 서버에서 새 함정 물리적 생성
    //    GameObject trap = Instantiate(trapPrefab, pos, Quaternion.identity);

    //    // 3. 네트워크 상의 모든 클라이언트에게 이 오브젝트를 보이게 함 (Spawn) [cite: 2026-04-06]
    //    NetworkServer.Spawn(trap);

    //    // 4. 관리를 위해 리스트에 추가
    //    spawnedTraps.Add(trap);
    //}

    [Command]
    private void CmdStartPlanting(Vector3 pos, Quaternion rot)
    {
        state.ChangeState(KillerCondition.Planting);

        while (spawnedTraps.Count >= 5)
        {
            GameObject oldest = spawnedTraps[0];
            spawnedTraps.RemoveAt(0);
            if (oldest != null) NetworkServer.Destroy(oldest);
        }

        RpcPlayPlantingEffect();

        GameObject trap = Instantiate(trapPrefab, pos, rot);
        NetworkServer.Spawn(trap);
        spawnedTraps.Add(trap);

        Invoke(nameof(BackToIdle), 1.2f);
    }

    [ClientRpc]
    private void RpcPlayPlantingEffect()
    {
        if (animator != null) animator.SetTrigger("Planting");
    }

    private void BackToIdle() { if (isServer) state.ChangeState(KillerCondition.Idle); }

    public void ExitBuildMode()
    {
        isBuildMode = false;
        CleanupGhost();
    }

    // --- 이하 Ghost 및 예외 처리 로직 (로컬 전용이므로 수정 불필요) ---
    private void UpdateGhostPosition()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (Physics.Raycast(ray, out RaycastHit hit, maxInstallDist, groundMask))
        {
            ghostInstance.SetActive(true);
            ghostInstance.transform.position = hit.point;
            UpdateGhostColor(CanPlace(out _));
        }
        else
        {
            ghostInstance.SetActive(false);
        }
    }

    private bool CanPlace(out Vector3 pos)
    {
        pos = Vector3.zero;
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (Physics.Raycast(ray, out RaycastHit hit, maxInstallDist, groundMask))
        {
            pos = hit.point;
            bool isBlocked = Physics.CheckBox(pos + Vector3.up * 0.1f, new Vector3(0.3f, 0.1f, 0.3f), Quaternion.identity, obstacleMask);
            return !isBlocked;
        }
        return false;
    }

    private void SetGhostVisual(GameObject target, float alpha)
    {
        foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
        {
            foreach (Material mat in r.materials)
            {
                if (mat.HasProperty("_BaseColor"))
                {
                    Color color = mat.GetColor("_BaseColor");
                    color.a = alpha;
                    mat.SetColor("_BaseColor", color);
                }
            }
        }
    }

    private void UpdateGhostColor(bool canPlace)
    {
        Color feedbackColor = canPlace ? Color.green : Color.red;
        feedbackColor.a = 0.4f;
        foreach (Renderer r in ghostInstance.GetComponentsInChildren<Renderer>())
        {
            foreach (Material mat in r.materials)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", feedbackColor);
                else if (mat.HasProperty("_Color")) mat.SetColor("_Color", feedbackColor);
            }
        }
    }

    private void CleanupGhost()
    {
        if (ghostInstance != null)
        {
            Destroy(ghostInstance);
            ghostInstance = null;
        }
    }

    public void ForceCancelTrapMode()
    {
        if (isBuildMode)
        {
            ExitBuildMode();
        }
    }
}