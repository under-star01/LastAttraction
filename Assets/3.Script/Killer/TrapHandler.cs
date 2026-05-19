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

    [Header("Cooldown")]
    [SerializeField] private float trapInstallCooldown = 5f;

    [Header("오디오")]
    [SerializeField] private AudioKey trapReadySoundKey = AudioKey.KillerTrapReady;
    [SerializeField] private AudioKey trapInstallSoundKey = AudioKey.KillerTrapInstall;

    // 설치대기 진입음은 살인마 본인에게만 2D로 즉시 재생한다.
    // 실제 설치음은 서버에서 트랩 위치 기준 3D로 모두에게 재생한다.
    [SerializeField] private Vector3 trapInstallSoundOffset = new Vector3(0f, 0.15f, 0f);

    public bool IsBuildMode => isBuildMode;

    private KillerSkillUI killerSkillUI;
    private Coroutine trapCooldownRoutine;
    private GameObject ghostInstance;
    private bool isTrapCooldown;
    private bool isBuildMode = false;

    private Camera cam;
    private KillerState state;
    private KillerInput killerInput;
    private Animator animator;

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

        if (trapCooldownRoutine != null)
        {
            StopCoroutine(trapCooldownRoutine);
            trapCooldownRoutine = null;
        }

        isTrapCooldown = false;
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

    private void BindUI()
    {
        if (killerSkillUI != null)
            return;

        if (InGameUIManager.Instance != null)
            killerSkillUI = InGameUIManager.Instance.GetKillerSkillUI();
    }

    private void Update()
    {
        if (!isLocalPlayer || killerInput == null)
            return;

        // 우클릭으로 함정 설치대기 모드 토글
        if (killerInput.IsTrapModePressed)
        {
            if (!isTrapCooldown)
                ToggleTrapMode();
        }

        if (!isBuildMode)
            return;

        // 설치대기 중 좌클릭하면 실제 설치
        if (killerInput.IsAttackWasPressed)
        {
            ConfirmInstallation();
        }
        else if (ghostInstance != null)
        {
            UpdateGhostPosition();
        }
    }

    private void ToggleTrapMode()
    {
        // 설치대기 진입 전에 프리팹이 없으면 막는다.
        if (!isBuildMode && trapPrefab == null)
        {
            Debug.LogWarning("[TrapHandler] trapPrefab이 비어있어서 설치대기 모드에 들어갈 수 없습니다.", this);
            return;
        }

        isBuildMode = !isBuildMode;

        BindUI();

        if (isBuildMode)
        {
            // 우클릭으로 설치대기 단계에 들어간 순간 살인마 본인에게만 2D 소리 재생
            PlayTrapReadySoundLocal();

            if (killerSkillUI != null)
                killerSkillUI.SetTrapUsing();

            if (ghostInstance == null)
            {
                ghostInstance = Instantiate(trapPrefab);

                if (ghostInstance.TryGetComponent(out TrapNode node))
                    node.enabled = false;

                SetGhostVisual(ghostInstance, 0.4f);
            }

            if (state != null)
                state.CmdChangeKillerState(KillerCondition.Planting);
        }
        else
        {
            ExitBuildMode();

            if (killerSkillUI != null)
                killerSkillUI.CancelTrapUsing();

            if (state != null)
                state.CmdChangeKillerState(KillerCondition.Idle);
        }
    }

    private void PlayTrapReadySoundLocal()
    {
        if (trapReadySoundKey == AudioKey.None)
            return;

        // 설치대기 진입음은 조작 피드백 성격이라 네트워크로 보내지 않고 로컬 2D로만 재생한다.
        AudioManager.PlayLocalAudio(trapReadySoundKey, AudioDimension.Sound2D);
    }

    private void ConfirmInstallation()
    {
        if (isTrapCooldown)
            return;

        if (ghostInstance == null)
            return;

        if (!CanPlace(out Vector3 installPos))
            return;

        // 실제 설치는 서버에서 처리한다.
        // 설치 사운드도 서버에서 한 번만 재생해야 중복 재생이 생기지 않는다.
        CmdStartPlanting(installPos, ghostInstance.transform.rotation);

        ExitBuildMode();

        StartTrapCooldown();
    }

    private void StartTrapCooldown()
    {
        if (trapCooldownRoutine != null)
            StopCoroutine(trapCooldownRoutine);

        trapCooldownRoutine = StartCoroutine(TrapCooldownRoutine());
    }

    private IEnumerator TrapCooldownRoutine()
    {
        isTrapCooldown = true;

        BindUI();

        if (killerSkillUI != null)
            killerSkillUI.StartTrapCooldown(trapInstallCooldown);

        yield return new WaitForSeconds(trapInstallCooldown);

        isTrapCooldown = false;
        trapCooldownRoutine = null;
    }

    [Command]
    private void CmdStartPlanting(Vector3 pos, Quaternion rot)
    {
        if (state == null)
            return;

        state.ChangeState(KillerCondition.Planting);

        while (spawnedTraps.Count >= 5)
        {
            GameObject oldest = spawnedTraps[0];
            spawnedTraps.RemoveAt(0);

            if (oldest != null)
                NetworkServer.Destroy(oldest);
        }

        // 설치 애니메이션 재생
        RpcPlayPlantingEffect();

        // 실제 트랩 설치 순간 모든 클라이언트에게 3D 설치 소리 재생
        ServerPlayTrapInstallSound(pos);

        GameObject trap = Instantiate(trapPrefab, pos, rot);
        NetworkServer.Spawn(trap);
        spawnedTraps.Add(trap);

        Invoke(nameof(BackToIdle), 1.2f);
    }

    [Server]
    private void ServerPlayTrapInstallSound(Vector3 installPos)
    {
        if (trapInstallSoundKey == AudioKey.None)
            return;

        if (NetworkAudioManager.Instance == null)
            return;

        NetworkAudioManager.PlayAudioForEveryone(
            trapInstallSoundKey,
            AudioDimension.Sound3D,
            installPos + trapInstallSoundOffset
        );
    }

    [ClientRpc]
    private void RpcPlayPlantingEffect()
    {
        if (animator != null)
            animator.SetTrigger("Planting");
    }

    private void BackToIdle()
    {
        if (isServer && state != null)
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
}