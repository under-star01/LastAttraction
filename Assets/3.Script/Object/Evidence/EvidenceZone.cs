using Mirror;
using UnityEngine;

// 증거 종류다.
// EvidenceZone이 이 값을 가지고 있고,
// 생성된 EvidencePoint 상자에게 이 정보를 넘겨준다.
public enum EvidenceType
{
    None,
    MissingPoster,       // 실종자 전단
    StaffLogbook,        // 직원 근무일지
    BrokenCamera,        // 부서진 CCTV
    BloodStainedTicket,  // 피 묻은 입장권
    VoiceRecorder        // 낡은 녹음기
}

public class EvidenceZone : MonoBehaviour
{
    [Header("이 존의 증거 종류")]
    [SerializeField] private EvidenceType evidenceType = EvidenceType.None;

    [Header("결과 / UI 표시 정보")]
    [SerializeField] private string customDisplayName; // 비워두면 EvidenceType에 맞는 기본 한글 이름 사용
    [SerializeField] private Sprite icon;              // 나중에 결과창에서 사용할 아이콘

    [Header("공용 증거 상자 프리팹")]
    [SerializeField] private EvidencePoint evidencePrefab; // 모든 존이 같은 상자 프리팹 사용

    [Header("증거 스폰 포인트")]
    [SerializeField] private Transform[] evidenceSpawnPoints;

    // 이 Zone에서 실제로 생성된 증거 상자다.
    private EvidencePoint spawnedEvidencePoint;

    // 같은 Zone이 중복으로 완료 처리되는 것을 막는다.
    private bool isFound;

    public EvidenceType EvidenceType => evidenceType;
    public Sprite Icon => icon;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(customDisplayName))
                return customDisplayName;

            return GetDefaultDisplayName(evidenceType);
        }
    }

    private void Start()
    {
        // 증거 생성과 목표 등록은 서버에서만 처리한다.
        if (!NetworkServer.active)
            return;

        // GameManager는 EvidenceZone 개수를 목표 증거 개수로 사용한다.
        if (GameManager.Instance != null)
            GameManager.Instance.AddZone(this);

        SpawnEvidence();
    }

    // 서버에서 공용 증거 상자 프리팹을 랜덤 스폰포인트에 생성한다.
    [Server]
    private void SpawnEvidence()
    {
        if (spawnedEvidencePoint != null)
            return;

        if (evidenceType == EvidenceType.None)
        {
            Debug.LogWarning($"[EvidenceZone] {name} : EvidenceType이 None입니다.", this);
            return;
        }

        if (evidencePrefab == null)
        {
            Debug.LogWarning($"[EvidenceZone] {name} : EvidencePrefab이 없습니다.", this);
            return;
        }

        Transform spawnPoint = GetRandomValidSpawnPoint();

        if (spawnPoint == null)
        {
            Debug.LogWarning($"[EvidenceZone] {name} : 사용할 수 있는 SpawnPoint가 없습니다.", this);
            return;
        }

        // 공용 상자 프리팹을 생성한다.
        EvidencePoint point = Instantiate(
            evidencePrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        // 이 Zone의 증거 정보를 생성된 상자에게 넘긴다.
        // Mirror Spawn 전에 SyncVar 값을 넣어야 클라이언트에도 처음부터 전달된다.
        point.ServerInit(this, evidenceType, DisplayName);

        // 네트워크 오브젝트로 생성한다.
        NetworkServer.Spawn(point.gameObject);

        spawnedEvidencePoint = point;

        Debug.Log(
            $"[EvidenceZone] {name} : 증거 상자 생성 완료 / " +
            $"종류: {DisplayName} / 위치: {spawnPoint.name}"
        );
    }

    // null이 아닌 스폰포인트 중 하나를 랜덤으로 고른다.
    private Transform GetRandomValidSpawnPoint()
    {
        if (evidenceSpawnPoints == null || evidenceSpawnPoints.Length == 0)
            return null;

        int validCount = 0;

        for (int i = 0; i < evidenceSpawnPoints.Length; i++)
        {
            if (evidenceSpawnPoints[i] != null)
                validCount++;
        }

        if (validCount <= 0)
            return null;

        int randomIndex = Random.Range(0, validCount);
        int currentIndex = 0;

        for (int i = 0; i < evidenceSpawnPoints.Length; i++)
        {
            if (evidenceSpawnPoints[i] == null)
                continue;

            if (currentIndex == randomIndex)
                return evidenceSpawnPoints[i];

            currentIndex++;
        }

        return null;
    }

    // 생성된 진짜 증거 상자가 조사 완료되면 호출된다.
    [Server]
    public void OnRealEvidenceFound(EvidencePoint point, uint finderNetId)
    {
        if (isFound)
            return;

        isFound = true;

        Debug.Log(
            $"[EvidenceZone] {name} : 증거 발견 완료 / " +
            $"증거: {DisplayName} / 타입: {evidenceType} / 발견자 NetId: {finderNetId}"
        );

        // 현재 GameManager는 Zone 기준으로 증거 개수만 올린다.
        // 나중에 결과창을 만들 때는 여기서 evidenceType, DisplayName, icon, finderNetId를 기록하면 된다.
        
        if (GameManager.Instance != null)
        {
            // 기존 목표 진행도 갱신
            GameManager.Instance.AddEvidence(this);

            // 결과창에 표시할 생존자별 획득 증거 기록
            if (NetworkServer.spawned.TryGetValue(finderNetId, out NetworkIdentity finderIdentity))
            {
                int evidenceIndex = (int)evidenceType - 1;

                GameManager.Instance.UpdateSurvivorResult(
                    finderIdentity,
                    evidenceIndex
                );
            }
        }
    }

    // EvidenceType에 맞는 기본 한글 이름을 반환한다.
    private string GetDefaultDisplayName(EvidenceType type)
    {
        switch (type)
        {
            case EvidenceType.MissingPoster:
                return "실종자 전단";

            case EvidenceType.StaffLogbook:
                return "직원 근무일지";

            case EvidenceType.BrokenCamera:
                return "부서진 CCTV";

            case EvidenceType.BloodStainedTicket:
                return "피 묻은 입장권";

            case EvidenceType.VoiceRecorder:
                return "낡은 녹음기";

            default:
                return "알 수 없는 증거";
        }
    }
}