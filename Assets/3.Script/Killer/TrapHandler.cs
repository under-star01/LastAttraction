using UnityEngine;
using Mirror;

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

    void Awake()
    {
        animator = GetComponent<Animator>();
        cam = GetComponentInChildren<Camera>();
    }

    void Update()
    {
        // 내 로컬 캐릭터가 아닐 경우 인풋 처리를 하지 않음
        if (!isLocalPlayer || TestMng.inputSys == null) return;

        // 1. 트랩 모드 토글 (우클릭)
        if (TestMng.inputSys.Killer.TrapMode.WasPressedThisFrame())
        {
            ToggleTrapMode();
        }

        // 2. 설치 확정 (좌클릭)
        if (isBuildMode && TestMng.inputSys.Killer.Attack.WasPressedThisFrame())
        {
            ConfirmInstallation();
        }

        // 3. 고스트 위치 업데이트 (로컬에서만 수행)
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
            // 즉시 설치
            Instantiate(trapPrefab, installPos, Quaternion.identity);

            // 설치 후 모드 종료 (연속 설치를 원하면 이 줄을 주석 처리하세요)
            ToggleTrapMode();
        }
    }

    [Command]
    private void CmdSpawnTrap(Vector3 pos)
    {
        // 1. 서버에서 함정 프리팹 생성
        GameObject trap = Instantiate(trapPrefab, pos, Quaternion.identity);

        // 2. 네트워크상의 모든 클라이언트에게 이 오브젝트를 생성(동기화)하라고 명령 [cite: 2026-04-06]
        NetworkServer.Spawn(trap);
    }

    private void UpdateGhostPosition()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (Physics.Raycast(ray, out RaycastHit hit, maxInstallDist, groundMask))
        {
            ghostInstance.SetActive(true);
            ghostInstance.transform.position = hit.point;

            bool canPlace = CanPlace(out _);
            UpdateGhostColor(canPlace);
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
            // 지면 위 0.1m 지점에서 박스 체크로 장애물 확인 (obstacleMask 레이어만 감지)
            bool isBlocked = Physics.CheckBox(pos + Vector3.up * 0.1f, new Vector3(0.3f, 0.1f, 0.3f), Quaternion.identity, obstacleMask);
            return !isBlocked;
        }
        return false;
    }

    private void SetGhostVisual(GameObject target, float alpha)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            foreach (Material mat in r.materials)
            {
                // 투명 처리가 가능한 셰이더인 경우 알파값 조절
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

        Renderer[] renderers = ghostInstance.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
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