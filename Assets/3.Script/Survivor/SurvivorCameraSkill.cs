using Mirror;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

public class SurvivorCameraSkill : NetworkBehaviour
{
    [Header("참조")]
    [SerializeField] private SurvivorInput input;
    [SerializeField] private SurvivorMove move;
    [SerializeField] private SurvivorActionState act;
    [SerializeField] private SurvivorState state;

    [Header("스킬 화면")]
    [SerializeField] private Camera skillCamera;
    [SerializeField] private CameraSkillUI skillUI;

    [Header("카메라 모델")]
    [SerializeField] private GameObject localCameraModel;   // 내 화면용 카메라 모델
    [SerializeField] private GameObject worldCameraModel;   // 월드에 보이는 카메라 모델

    [Header("카메라 위치")]
    [SerializeField] private GameObject cinemachineRoot;
    [SerializeField] private CinemachineCamera normalCinemachine;
    [SerializeField] private CinemachineCamera skillCinemachine;
    [SerializeField] private CinemachineCamera lobbyCinemachine;

    [Header("카메라 탐지")]
    [SerializeField] private Transform detectOrigin;        // Ray 시작 기준 위치
    [SerializeField] private LayerMask killerLayerMask;     // Killer Layer
    [SerializeField] private LayerMask cameraDetectBlockMask;   // Obstacle, Killer, Survivor Layer
    [SerializeField] private float detectDistance = 12f;    // 탐지 거리
    [SerializeField] private float detectAngle = 60f;       // 부채꼴 시야각
    [SerializeField] private int rayCount = 15;             // Ray 개수
    [SerializeField] private float detectInterval = 0.1f;   // 탐지 간격
    [SerializeField] private float detectHeight = 1.5f;     // 고정 탐지 높이
    [SerializeField] private bool drawDebugRay = true;      // Scene View Ray 표시

    [Header("카메라 프레임 UI")]
    [SerializeField] private Color normalFrameColor = Color.white;
    [SerializeField] private Color detectedFrameColor = Color.red;
    [SerializeField] private float detectedHoldTime = 0.25f;
    private Image[] frameImages;

    [SyncVar(hook = nameof(OnSkillChanged))]
    private bool isUse;

    public bool IsUse => isUse;

    // 로컬 플레이어 UI / 카메라 준비 완료 여부
    private bool isLocalReady;

    [Header("Rage 전용 상태")]
    [SyncVar]
    [SerializeField] private bool isRecordingKiller = false;
    public bool IsRecordingKiller => isRecordingKiller;

    // 자주 쓰는 레이어 번호 캐시
    private int camWorldLayer;
    private int hideSelfLayer;
    private int survivorLayer;
    private int downedLayer;
    private float nextDetectTime;

    private float lastKillerDetectedTime = -999f;
    private bool isFrameDetected;

    private void Awake()
    {
        if (input == null)
            input = GetComponent<SurvivorInput>();

        if (move == null)
            move = GetComponent<SurvivorMove>();

        if (act == null)
            act = GetComponent<SurvivorActionState>();

        if (state == null)
            state = GetComponent<SurvivorState>();

        // 시작 시 스킬 카메라는 꺼둔다
        if (skillCamera != null)
            skillCamera.enabled = false;

        // 시작 시 카메라 모델도 숨긴다
        if (localCameraModel != null)
            localCameraModel.SetActive(false);

        if (worldCameraModel != null)
            worldCameraModel.SetActive(false);

        // 시작 시 내 플레이어가 아니면 시네머신도 꺼져 있어야 안전하다
        if (normalCinemachine != null)
            normalCinemachine.gameObject.SetActive(false);

        if (skillCinemachine != null)
            skillCinemachine.gameObject.SetActive(false);

        // 사용할 레이어 이름
        camWorldLayer = LayerMask.NameToLayer("CamWorld");
        hideSelfLayer = LayerMask.NameToLayer("HideSelf");
        survivorLayer = LayerMask.NameToLayer("Survivor");
        downedLayer = LayerMask.NameToLayer("Downed");
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        BindUI();
        isLocalReady = true;

        // 내 플레이어의 시네머신만 활성화
        if (cinemachineRoot != null)
            cinemachineRoot.SetActive(true);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // 남의 플레이어 카메라는 절대 활성화되면 안 된다
        if (!isLocalPlayer)
        {
            if (skillCamera != null)
                skillCamera.enabled = false;

            if (normalCinemachine != null)
                normalCinemachine.gameObject.SetActive(false);

            if (skillCinemachine != null)
                skillCinemachine.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (!isLocalPlayer)
            return;

        bool want = false;

        if (input != null)
            want = input.IsCameraSkillPressed;

        // 현재 상태상 사용 불가능하면 강제로 끈다
        if (!CanUse())
            want = false;

        if (want != isUse)
            CmdSetSkill(want);

        // 카메라 스킬 사용 중일 때만 Killer 탐지
        if (isUse)
        {
            DetectKillerInCameraView();
            UpdateFrameDetectState();
        }
        else
        {
            SetFrameDetected(false);
        }
    }

    // 스킬 사용 가능 여부 검사
    private bool CanUse()
    {
        if (act == null)
            return false;

        return act.CanCam();
    }

    [Command]
    private void CmdSetSkill(bool value)
    {
        if (act == null)
        {
            value = false;
        }
        else if (value && !act.CanCam())
        {
            value = false;
        }

        isUse = value;
        act.SetCam(value);

        // 카메라 스킬이 꺼지는 순간 촬영 상태도 서버 기준으로 초기화
        if (!value)
        {
            isRecordingKiller = false;
        }
    }

    // 스킬 on/off가 바뀌면 애니메이션 / 카메라 모델 / 로컬 화면을 갱신한다
    private void OnSkillChanged(bool oldValue, bool newValue)
    {
        if (move != null)
            move.SetCamAnim(newValue);

        // 월드 카메라 모델은 스킬 중에만 보이게
        if (worldCameraModel != null)
            worldCameraModel.SetActive(newValue);

        if (isLocalPlayer)
        {
            // 내 월드 카메라 모델만 스킬 카메라에서 안 보이게
            SetOwnWorldCamHidden(newValue);

            // 내 몸 모델도 스킬 카메라에서 안 보이게
            SetOwnBodyHidden(newValue);

            // 로컬 UI / 카메라 반영
            ApplyLocalView(newValue);

            if (!newValue)
                SetFrameDetected(false, true);
        }
    }

    // 카메라 시야 부채꼴 범위 안에 Killer가 있는지 탐지한다
    private void DetectKillerInCameraView()
    {
        if (Time.time < nextDetectTime)
            return;

        nextDetectTime = Time.time + detectInterval;

        if (detectOrigin == null)
            return;

        if (killerLayerMask.value == 0)
            return;

        if (cameraDetectBlockMask.value == 0)
            return;

        int safeRayCount = Mathf.Max(1, rayCount);

        Vector3 origin = detectOrigin.position;

        // Y축 방향으로 Ray를 낭비하지 않도록 생존자 기준 고정 높이에서만 탐지
        origin.y = transform.position.y + detectHeight;

        Vector3 forward = detectOrigin.forward;

        // 수평 방향만 사용
        forward.y = 0f;

        if (forward.sqrMagnitude <= 0.001f)
            forward = transform.forward;

        forward.Normalize();

        float halfAngle = detectAngle * 0.5f;

        for (int i = 0; i < safeRayCount; i++)
        {
            float t = 0f;

            if (safeRayCount > 1)
                t = i / (float)(safeRayCount - 1);

            float currentAngle = Mathf.Lerp(-halfAngle, halfAngle, t);

            Vector3 rayDir = Quaternion.Euler(0f, currentAngle, 0f) * forward;

            bool isHit = Physics.Raycast(
                origin,
                rayDir,
                out RaycastHit hit,
                detectDistance,
                cameraDetectBlockMask
            );

            bool isKiller = false;

            if (isHit)
                isKiller = IsInLayerMask(hit.collider.gameObject.layer, killerLayerMask);

            if (drawDebugRay)
            {
                Color rayColor = Color.green;

                if (isHit)
                    rayColor = isKiller ? Color.red : Color.yellow;

                Debug.DrawRay(origin, rayDir * detectDistance, rayColor, detectInterval);
            }

            if (isHit && isKiller)
            {
                lastKillerDetectedTime = Time.time;
                return;
            }
        }
    }

    // 마지막 탐지 이후 일정 시간 동안은 촬영 중으로 유지한다
    private void UpdateFrameDetectState()
    {
        bool detected = Time.time <= lastKillerDetectedTime + detectedHoldTime;
        SetFrameDetected(detected);

        // 촬영 상태가 변경될 때만 서버에 Command 보냄
        if (isLocalPlayer && isRecordingKiller != detected)
        {
            CmdSetRecordingKiller(detected);
        }
    }

    // 프레임 UI 색상 변경
    private void SetFrameDetected(bool detected, bool force = false)
    {
        if (!force && isFrameDetected == detected)
            return;

        isFrameDetected = detected;

        Color targetColor = detected ? detectedFrameColor : normalFrameColor;

        for (int i = 0; i < frameImages.Length; i++)
        {
            if (frameImages[i] == null)
                continue;

            frameImages[i].color = targetColor;
        }
    }

    private bool IsInLayerMask(int layer, LayerMask layerMask)
    {
        return (layerMask.value & (1 << layer)) != 0;
    }

    // 씬 UI 찾기
    private void BindUI()
    {
        if (LobbySceneBinder.Instance != null)
            frameImages = LobbySceneBinder.Instance.GetFrameUI();

        if (skillUI == null && LobbySceneBinder.Instance != null)
            skillUI = LobbySceneBinder.Instance.GetCameraSkillUI();

        if (skillUI == null)
            skillUI = FindFirstObjectByType<CameraSkillUI>(FindObjectsInactive.Include);
    }

    // 내 월드 카메라 모델만 숨김 레이어로 바꾼다
    // 상대 월드 카메라는 그대로 CamWorld라서 스킬 카메라에 보인다
    private void SetOwnWorldCamHidden(bool hide)
    {
        if (!isLocalPlayer)
            return;

        if (worldCameraModel == null)
            return;

        int targetLayer = camWorldLayer;

        if (hide)
            targetLayer = hideSelfLayer;

        SetLayerRecursive(worldCameraModel.transform, targetLayer);
    }

    // 내 몸 모델도 숨김 레이어로 바꾼다
    // 스킬 종료 시 현재 몸 상태에 맞는 원래 레이어로 되돌린다
    private void SetOwnBodyHidden(bool hide)
    {
        if (!isLocalPlayer)
            return;

        if (move == null)
            return;

        if (hide)
        {
            move.SetModelLayer(hideSelfLayer);
            return;
        }

        if (state != null && state.IsDowned)
            move.SetModelLayer(downedLayer);
        else
            move.SetModelLayer(survivorLayer);
    }

    // 오브젝트와 자식들의 레이어를 전부 바꾼다
    private void SetLayerRecursive(Transform target, int layer)
    {
        if (target == null)
            return;

        target.gameObject.layer = layer;

        for (int i = 0; i < target.childCount; i++)
            SetLayerRecursive(target.GetChild(i), layer);
    }

    // 로컬 플레이어 화면에만 적용되는 요소
    // - 스킬 카메라
    // - 시네머신 전환
    // - 로컬 카메라 모델
    // - UI
    private void ApplyLocalView(bool value)
    {
        if (!isLocalPlayer)
            return;

        if (!isLocalReady)
        {
            if (skillCamera != null)
                skillCamera.enabled = false;

            if (localCameraModel != null)
                localCameraModel.SetActive(false);

            SetFrameDetected(false, true);

            return;
        }

        BindUI();

        if (skillCamera != null)
            skillCamera.enabled = value;

        if (normalCinemachine != null && skillCinemachine != null)
        {
            if (value)
            {
                normalCinemachine.Priority = 0;
                skillCinemachine.Priority = 30;
            }
            else
            {
                normalCinemachine.Priority = 30;
                skillCinemachine.Priority = 0;
            }
        }

        // 내 화면 전용 카메라 모델
        if (localCameraModel != null)
            localCameraModel.SetActive(value);

        if (skillUI != null)
        {
            if (value)
                skillUI.Show();
            else
                skillUI.Hide();
        }

        if (!value)
            SetFrameDetected(false, true);
    }

    // 로비 카메라 뷰 적용 메소드
    public void ApplyLobbyView(bool value)
    {
        if (value)
        {
            lobbyCinemachine.Priority = 30;

            normalCinemachine.Priority = 0;
            skillCinemachine.Priority = 0;
        }
        else
        {
            lobbyCinemachine.Priority = 0;

            normalCinemachine.Priority = 30;
            skillCinemachine.Priority = 0;
        }
    }


    [Command]
    private void CmdSetRecordingKiller(bool value)
    {
        isRecordingKiller = value;
    }
}