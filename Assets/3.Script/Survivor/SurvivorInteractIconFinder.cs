using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 로컬 생존자 기준으로 가까운 상호작용 물체 위에 하얀 원 아이콘을 표시합니다.
/// UI 연결은 Update에서 계속 확인하지 않고, InGame 씬이 로드될 때 한 번만 시도합니다.
/// 실제 아이콘은 Screen Space Overlay Canvas에 있으므로 벽 뒤에서도 보입니다.
/// 
/// 핵심 구조:
/// - 아이콘 표시 범위: detectRadius
/// - 실제 상호작용 범위: 각 오브젝트의 기존 Trigger / CanUse 로직
/// 
/// 그래서 멀리서는 원 아이콘이 보이고,
/// 실제 E Hold는 기존 상호작용 범위 안에 들어갔을 때만 가능합니다.
/// </summary>
public class SurvivorInteractIconFinder : NetworkBehaviour
{
    [Header("씬 설정")]
    [SerializeField] private string inGameSceneName = "InGame";

    [Header("탐지 설정")]
    [SerializeField] private float detectRadius = 2.5f;
    [SerializeField] private LayerMask interactableLayer;
    [SerializeField] private float updateInterval = 0.05f;

    [Header("UI")]
    [SerializeField] private Image circleIconImage;

    [Header("카메라")]
    [SerializeField] private Camera targetCamera;

    [Header("화면 보정")]
    [SerializeField] private Vector2 screenOffset = new Vector2(0f, 0f);

    private readonly Collider[] results = new Collider[32];

    private SurvivorState localState;

    private InteractIconPoint currentIconPoint;
    private RectTransform iconRect;

    private float updateTimer;
    private bool isUIBound;

    private void Awake()
    {
        localState = GetComponent<SurvivorState>();

        // 혹시 프리팹에 직접 Image가 연결된 경우를 대비합니다.
        CacheIconRect();

        if (circleIconImage != null && iconRect != null)
        {
            isUIBound = true;
            HideIcon();
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // 로컬 생존자만 씬 로드 이벤트를 받습니다.
        SceneManager.sceneLoaded += OnSceneLoaded;

        targetCamera = Camera.main;

        if (localState == null)
            localState = GetComponent<SurvivorState>();

        // 이미 InGame 씬에서 시작한 경우를 대비합니다.
        Scene activeScene = SceneManager.GetActiveScene();

        if (activeScene.name == inGameSceneName)
            StartCoroutine(BindUIAfterSceneLoaded());
    }

    private void OnDestroy()
    {
        if (isLocalPlayer)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Update()
    {
        // 로컬 생존자만 아이콘을 표시합니다.
        if (!isLocalPlayer)
            return;

        // InGame UI가 아직 연결되지 않았으면 아무것도 하지 않습니다.
        if (!isUIBound)
            return;

        if (localState == null)
            localState = GetComponent<SurvivorState>();

        if (localState == null)
        {
            HideIcon();
            return;
        }

        if (targetCamera == null || !targetCamera.gameObject.activeInHierarchy)
            targetCamera = Camera.main;

        if (targetCamera == null)
        {
            HideIcon();
            return;
        }

        updateTimer += Time.deltaTime;

        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;
            SearchClosestInteractIcon();
        }

        UpdateIconPosition();
    }

    private void OnDisable()
    {
        HideIcon();
    }

    /// <summary>
    /// InGame 씬이 로드되면 한 번만 UI 연결을 시도합니다.
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!isLocalPlayer)
            return;

        if (scene.name != inGameSceneName)
            return;

        StartCoroutine(BindUIAfterSceneLoaded());
    }

    /// <summary>
    /// 씬 로드 직후 UI 오브젝트의 Awake가 끝난 다음 안전하게 연결합니다.
    /// </summary>
    private IEnumerator BindUIAfterSceneLoaded()
    {
        yield return null;

        TryBindUIFromInGameUIManager();
    }

    /// <summary>
    /// InGameUIManager에서 하얀 원 Image를 한 번만 가져옵니다.
    /// </summary>
    private void TryBindUIFromInGameUIManager()
    {
        if (isUIBound)
            return;

        if (InGameUIManager.Instance == null)
        {
            Debug.LogWarning("[SurvivorInteractIconFinder] InGameUIManager.Instance를 찾지 못했습니다.");
            return;
        }

        circleIconImage = InGameUIManager.Instance.GetInteractCircleIcon();
        CacheIconRect();

        if (circleIconImage == null || iconRect == null)
        {
            Debug.LogWarning("[SurvivorInteractIconFinder] InteractCircleIcon Image가 연결되지 않았습니다.");
            return;
        }

        isUIBound = true;
        HideIcon();

        targetCamera = Camera.main;

        Debug.Log("[SurvivorInteractIconFinder] InteractCircleIcon Image 연결 완료");
    }

    /// <summary>
    /// Image의 RectTransform을 캐싱합니다.
    /// </summary>
    private void CacheIconRect()
    {
        if (circleIconImage == null)
        {
            iconRect = null;
            return;
        }

        iconRect = circleIconImage.GetComponent<RectTransform>();
    }

    /// <summary>
    /// 주변 상호작용 물체 중 가장 가까운 아이콘 포인트를 찾습니다.
    /// 벽 뒤에서도 보여야 하므로 Raycast로 시야 체크를 하지 않습니다.
    /// 
    /// 실제 상호작용 가능 범위가 아니라,
    /// InteractIconPoint.CanShowIconFor()로 "아이콘 표시 가능 상태"만 검사합니다.
    /// </summary>
    private void SearchClosestInteractIcon()
    {
        if (localState == null)
        {
            currentIconPoint = null;
            HideIcon();
            return;
        }

        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            detectRadius,
            results,
            interactableLayer,
            QueryTriggerInteraction.Collide
        );

        InteractIconPoint nearestPoint = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider col = results[i];

            if (col == null)
                continue;

            // IInteractable이 있는 물체만 표시 대상으로 사용합니다.
            IInteractable interactable = col.GetComponentInParent<IInteractable>();

            if (interactable == null)
                continue;

            InteractIconPoint iconPoint = col.GetComponentInParent<InteractIconPoint>();

            if (iconPoint == null)
                continue;

            // 핵심:
            // 실제 상호작용 범위가 아니라 아이콘 표시 가능 상태만 검사합니다.
            // 증거 완료, 업로드 비활성화, 빈 감옥, 다른 유저 사용 중 같은 조건은 여기서 걸러집니다.
            if (!iconPoint.CanShowIconFor(localState, interactable))
                continue;

            float distance = Vector3.Distance(transform.position, iconPoint.transform.position);

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestPoint = iconPoint;
            }
        }

        currentIconPoint = nearestPoint;

        if (currentIconPoint == null)
            HideIcon();
        else
            ShowIcon();
    }

    /// <summary>
    /// 월드 위치를 화면 좌표로 바꿔 원 아이콘을 이동시킵니다.
    /// Screen Space Overlay Canvas 기준이므로 벽에 가려지지 않습니다.
    /// </summary>
    private void UpdateIconPosition()
    {
        if (currentIconPoint == null)
            return;

        if (circleIconImage == null || iconRect == null)
            return;

        if (targetCamera == null)
            return;

        Vector3 worldPos = currentIconPoint.GetIconWorldPosition();
        Vector3 screenPos = targetCamera.WorldToScreenPoint(worldPos);

        // 카메라 뒤쪽에 있는 물체는 표시하지 않습니다.
        if (screenPos.z < 0f)
        {
            HideIcon();
            return;
        }

        if (!circleIconImage.gameObject.activeSelf)
            circleIconImage.gameObject.SetActive(true);

        iconRect.position = new Vector2(
            screenPos.x + screenOffset.x,
            screenPos.y + screenOffset.y
        );
    }

    private void ShowIcon()
    {
        if (circleIconImage == null)
            return;

        if (!circleIconImage.gameObject.activeSelf)
            circleIconImage.gameObject.SetActive(true);
    }

    private void HideIcon()
    {
        if (circleIconImage == null)
            return;

        if (circleIconImage.gameObject.activeSelf)
            circleIconImage.gameObject.SetActive(false);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, detectRadius);
    }
#endif
}