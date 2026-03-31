using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class TrapHandler : MonoBehaviour
{
    [Header("Settings")]
    public GameObject trapPrefab;      // 실제 설치될 함정 프리팹
    public float maxInstallDist = 3f;
    public float buildTime = 2.0f;     // 설치 소요 시간
    public LayerMask groundMask;
    public LayerMask obstacleMask;

    private GameObject ghostInstance;   // 프리팹을 복제해서 고스트로 사용
    private GameObject currentBuildingTrap;
    private TrapNode currentTrapNode;

    private bool isBuildMode = false;
    private bool isConstructing = false;

    private Animator animator;
    private Camera cam;
    private KillerInput inputActions;

    void Awake()
    {
        animator = GetComponent<Animator>();
        cam = GetComponentInChildren<Camera>();
        inputActions = new KillerInput();
    }

    void OnEnable()
    {
        inputActions.Killer.Enable();
        inputActions.Killer.TrapMode.performed += OnToggleTrapMode;
        inputActions.Killer.Attack.performed += OnConfirm;
    }

    void OnDisable()
    {
        inputActions.Killer.Disable();
    }

    void Update()
    {
        if (isBuildMode && ghostInstance != null)
        {
            UpdateGhostPosition();
        }

        if (isConstructing && currentTrapNode != null)
        {
            float progressPerSec = 100f / buildTime;
            currentTrapNode.AddProgress(progressPerSec * Time.deltaTime);
        }
    }

    private void OnToggleTrapMode(InputAction.CallbackContext context)
    {
        if (isConstructing) return;

        isBuildMode = !isBuildMode;

        if (isBuildMode)
        {
            // 실제 함정 프리팹을 생성해서 고스트로 활용 [cite: 2026-01-27]
            ghostInstance = Instantiate(trapPrefab);

            // 설치 전이므로 로직 스크립트나 콜라이더는 비활성화 처리 (필요시)
            if (ghostInstance.TryGetComponent(out TrapNode node)) node.enabled = false;

            SetGhostTransparent(ghostInstance, 0.4f);
        }
        else
        {
            if (ghostInstance != null) Destroy(ghostInstance);
        }
    }

    // 머티리얼 알파값을 조절하는 함수 (BuildingMng 응용) [cite: 2026-01-27]
    private void SetGhostTransparent(GameObject target, float alpha)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            // .material을 사용하면 인스턴스화된 머티리얼이 생성되어 원본에 영향을 주지 않음
            foreach (Material mat in r.materials)
            {
                // 표준 URP/HDRP 속성명을 사용하되 변수명에는 언더바 제외
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

    private void OnConfirm(InputAction.CallbackContext context)
    {
        if (!isBuildMode || isConstructing) return;

        if (CanPlace(out Vector3 installPos))
        {
            StartConstruction(installPos);
        }
    }

    private void StartConstruction(Vector3 pos)
    {
        isBuildMode = false;
        // 고스트는 역할을 다했으므로 파괴
        if (ghostInstance != null) Destroy(ghostInstance);

        isConstructing = true;

        // 실제 작동할 함정 생성
        currentBuildingTrap = Instantiate(trapPrefab, pos, Quaternion.identity);
        currentTrapNode = currentBuildingTrap.GetComponent<TrapNode>();

        if (animator != null) animator.SetBool("isConstructing", true);
    }

    public void CancelConstruction()
    {
        if (!isConstructing) return;

        isConstructing = false;
        if (animator != null) animator.SetBool("isConstructing", false);

        // 진행 중 취소 시 오브젝트 파괴 [cite: 2026-01-27]
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
            // 함정 크기에 맞춰 장애물 체크 (와일드 에이트 방식) [cite: 2026-01-27]
            bool isBlocked = Physics.CheckBox(pos + Vector3.up * 0.1f, new Vector3(0.3f, 0.1f, 0.3f), Quaternion.identity, obstacleMask);
            return !isBlocked;
        }
        return false;
    }
}