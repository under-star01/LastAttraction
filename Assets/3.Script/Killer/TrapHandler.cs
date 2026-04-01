using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class TrapHandler : MonoBehaviour
{
    [Header("Settings")]
    public GameObject trapPrefab;
    public float maxInstallDist = 3f;
    public float buildTime = 2.0f;
    public LayerMask groundMask;
    public LayerMask obstacleMask;

    private GameObject ghostInstance;
    private GameObject currentBuildingTrap;
    private TrapNode currentTrapNode;

    private bool isBuildMode = false;
    private bool isConstructing = false;

    private Animator animator;
    private Camera cam;

    // [변경] 개별 inputSys 변수는 삭제했습니다. (TestMng.inputSys 사용)

    void Awake()
    {
        animator = GetComponent<Animator>();
        cam = GetComponentInChildren<Camera>();
    }

    // [변경] OnEnable/OnDisable에서 이벤트를 등록하던 코드는 모두 삭제했습니다.
    // 중앙 시스템(TestMng)에서 관리하므로 여기서 중복 등록할 필요가 없습니다.

    void Update()
    {
        // 중앙 인풋 시스템이 없으면 실행 안 함
        if (TestMng.inputSys == null) return;

        // 1. [통합] 트랩 모드 토글 (우클릭)
        if (TestMng.inputSys.Killer.TrapMode.WasPressedThisFrame())
        {
            ToggleTrapMode();
        }

        // 2. [통합] 설치 확정 (좌클릭) - 이제 빨간 줄 안 뜹니다!
        if (TestMng.inputSys.Killer.Attack.WasPressedThisFrame())
        {
            ConfirmInstallation();
        }

        // 3. 기존 빌드 모드 로직
        if (isBuildMode && ghostInstance != null)
        {
            UpdateGhostPosition();
        }

        // 4. 기존 건설 진행 로직
        if (isConstructing && currentTrapNode != null)
        {
            float progressPerSec = 100f / buildTime;
            currentTrapNode.AddProgress(progressPerSec * Time.deltaTime);
        }
    }

    // [리팩토링] 기존 OnToggleTrapMode를 통합했습니다.
    private void ToggleTrapMode()
    {
        if (isConstructing) return;

        isBuildMode = !isBuildMode;

        if (isBuildMode)
        {
            if (ghostInstance == null)
            {
                ghostInstance = Instantiate(trapPrefab);
                if (ghostInstance.TryGetComponent(out TrapNode node)) node.enabled = false;
                SetGhostTransparent(ghostInstance, 0.4f);
            }
        }
        else
        {
            if (ghostInstance != null)
            {
                Destroy(ghostInstance);
                ghostInstance = null; // 명확히 비워줍니다.
            }
        }
    }

    // [리팩토링] 기존 OnConfirm을 Update에서 쓸 수 있게 형식을 바꿨습니다.
    private void ConfirmInstallation()
    {
        if (!isBuildMode || isConstructing) return;

        if (CanPlace(out Vector3 installPos))
        {
            StartConstruction(installPos);
        }
    }

    // --- 아래는 유저님의 기존 로직과 동일합니다 (유지) ---

    private void SetGhostTransparent(GameObject target, float alpha)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            foreach (Material mat in r.materials)
            {
                if (mat.HasProperty("_BaseColor"))
                {
                    Color color = mat.GetColor("_BaseColor");
                    color.a = alpha;
                    mat.SetColor("_BaseColor", color);
                }
                else if (mat.HasProperty("_Color"))
                {
                    Color color = mat.GetColor("_Color");
                    color.a = alpha;
                    mat.SetColor("_Color", color);
                }
            }
        }
    }

    private void UpdateGhostPosition()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        if (Physics.Raycast(ray, out RaycastHit hit, maxInstallDist, groundMask))
        {
            ghostInstance.SetActive(true);
            ghostInstance.transform.position = hit.point;

            bool canPlace = CanPlace(out _);
            UpdateGhostVisual(canPlace);
        }
        else
        {
            ghostInstance.SetActive(false);
        }
    }

    private void UpdateGhostVisual(bool canPlace)
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

    private void StartConstruction(Vector3 pos)
    {
        isBuildMode = false;
        if (ghostInstance != null) Destroy(ghostInstance);

        isConstructing = true;
        currentBuildingTrap = Instantiate(trapPrefab, pos, Quaternion.identity);
        currentTrapNode = currentBuildingTrap.GetComponent<TrapNode>();

        if (animator != null) animator.SetBool("isConstructing", true);
    }

    public void CancelConstruction()
    {
        if (!isConstructing) return;

        isConstructing = false;
        if (animator != null) animator.SetBool("isConstructing", false);
        if (currentBuildingTrap != null) Destroy(currentBuildingTrap);
        currentTrapNode = null;
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

    public void ForceCancelTrapMode()
    {
        isBuildMode = false;
        if (ghostInstance != null)
        {
            Destroy(ghostInstance);
            ghostInstance = null;
        }
    }
}