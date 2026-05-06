using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    private float plantStartTime;
    private const float plantDuration = 1.2f;

    // 서버에서 설치된 함정들을 관리할 리스트
    private readonly List<GameObject> spawnedTraps = new List<GameObject>();

    private void Awake()
    {
        state = GetComponent<KillerState>();
        killerInput = GetComponent<KillerInput>();
        animator = GetComponentInChildren<Animator>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        StartCoroutine(AssignMainCameraNextFrame());
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        if (isLocalPlayer)
        {
            cam = null;
            CleanupGhost();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!isLocalPlayer)
            return;

        StartCoroutine(AssignMainCameraNextFrame());
    }

    private IEnumerator AssignMainCameraNextFrame()
    {
        // 씬 로드 직후 Main Camera / CinemachineBrain 초기화 타이밍을 한 프레임 기다림
        yield return null;

        cam = Camera.main;

        if (cam == null)
        {
            Debug.LogWarning("[TrapHandler] Main Camera를 찾지 못했습니다. 씬의 Main Camera 태그를 확인해주세요.", this);
            yield break;
        }

        Debug.Log($"[TrapHandler] Main Camera 연결 완료: {cam.name}");
    }

    private void Update()
    {
        if (!isLocalPlayer || killerInput == null)
            return;

        if (state.CurrentCondition == KillerCondition.Planting)
            return;

        // 함정 모드 토글
        if (killerInput.IsTrapModePressed)
        {
            ToggleTrapMode();
        }

        if (!isBuildMode)
            return;

        if (killerInput.IsAttackWasPressed)
        {
            ConfirmInstallation();
        }
        else if (ghostInstance != null)
        {
            UpdateGhostPosition();
        }
    }

    public float PlantProgress
    {
        get
        {
            if (state.CurrentCondition != KillerCondition.Planting) return 1f;

            float elapsed = Time.time - plantStartTime;
            return Mathf.Clamp01(elapsed / plantDuration);
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

                if (ghostInstance.TryGetComponent(out TrapNode node))
                    node.enabled = false;

                SetGhostVisual(ghostInstance, 0.4f);
            }

            //state.CmdChangeKillerState(KillerCondition.Planting);
        }
        else
        {
            CleanupGhost();
            if (state.CurrentCondition != KillerCondition.Idle)
                state.CmdChangeKillerState(KillerCondition.Idle);
        }
    }

    private void ConfirmInstallation()
    {
        if (!CanPlace(out Vector3 installPos))
            return;

        CmdStartPlanting(installPos, ghostInstance.transform.rotation);

        // 로컬 모드 즉시 종료
        ExitBuildMode();
    }

    [Command]
    private void CmdStartPlanting(Vector3 pos, Quaternion rot)
    {
        plantStartTime = Time.time;

        state.ChangeState(KillerCondition.Planting);

        while (spawnedTraps.Count >= 5)
        {
            GameObject oldest = spawnedTraps[0];
            spawnedTraps.RemoveAt(0);

            if (oldest != null)
                NetworkServer.Destroy(oldest);
        }

        RpcPlayPlantingEffect();

        GameObject trap = Instantiate(trapPrefab, pos, rot);
        NetworkServer.Spawn(trap);
        spawnedTraps.Add(trap);

        Invoke(nameof(BackToIdle), plantDuration);
    }

    [ClientRpc]
    private void RpcPlayPlantingEffect()
    {
        if (animator != null)
            animator.SetTrigger("Planting");
    }

    private void BackToIdle()
    {
        if (isServer)
            state.ChangeState(KillerCondition.Idle);
    }

    public void ExitBuildMode()
    {
        isBuildMode = false;
        CleanupGhost();
    }

    private void UpdateGhostPosition()
    {
        if (cam == null)
            return;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

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

        if (cam == null)
            return false;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (!Physics.Raycast(ray, out RaycastHit hit, maxInstallDist, groundMask))
            return false;

        pos = hit.point;

        bool isBlocked = Physics.CheckBox(
            pos + Vector3.up * 0.1f,
            new Vector3(0.3f, 0.1f, 0.3f),
            Quaternion.identity,
            obstacleMask
        );

        return !isBlocked;
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
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", feedbackColor);
                else if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", feedbackColor);
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