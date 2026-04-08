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

    private Animator animator;
    private Camera cam;

    // 서버에서 설치된 함정들을 관리할 리스트 (서버 전용) [cite: 2026-04-06]
    private readonly List<GameObject> spawnedTraps = new List<GameObject>();

    void Awake()
    {
        animator = GetComponent<Animator>();
        cam = GetComponentInChildren<Camera>();
    }

    void Update()
    {
        if (!isLocalPlayer || TestMng.inputSys == null) return;

        if (TestMng.inputSys.Killer.TrapMode.WasPressedThisFrame())
        {
            ToggleTrapMode();
        }

        if (isBuildMode && TestMng.inputSys.Killer.Attack.WasPressedThisFrame())
        {
            ConfirmInstallation();
        }

        if (isBuildMode && ghostInstance != null)
        {
            UpdateGhostPosition();
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
        }
        else
        {
            CleanupGhost();
        }
    }

    private void ConfirmInstallation()
    {
        if (CanPlace(out Vector3 installPos))
        {
            // [수정] 로컬 생성이 아닌 서버 명령(Command)을 반드시 호출해야 합니다. [cite: 2026-04-06]
            CmdSpawnTrap(installPos);

            ToggleTrapMode();
        }
    }

    [Command] // 클라이언트가 요청하면 서버에서 실행 [cite: 2026-04-06]
    private void CmdSpawnTrap(Vector3 pos)
    {
        // 1. 개수 제한 체크 (FIFO 로직) [cite: 2026-04-06]
        // 리스트에 이미 5개가 있다면 가장 오래된 것(0번)을 제거합니다.
        while (spawnedTraps.Count >= 5)
        {
            GameObject oldestTrap = spawnedTraps[0];
            spawnedTraps.RemoveAt(0);

            if (oldestTrap != null)
            {
                // 서버와 모든 클라이언트 화면에서 동시에 삭제합니다. [cite: 2026-04-06]
                NetworkServer.Destroy(oldestTrap);
            }
        }

        // 2. 서버에서 새 함정 물리적 생성
        GameObject trap = Instantiate(trapPrefab, pos, Quaternion.identity);

        // 3. 네트워크 상의 모든 클라이언트에게 이 오브젝트를 보이게 함 (Spawn) [cite: 2026-04-06]
        NetworkServer.Spawn(trap);

        // 4. 관리를 위해 리스트에 추가
        spawnedTraps.Add(trap);
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
        isBuildMode = false;
        CleanupGhost();
    }
}