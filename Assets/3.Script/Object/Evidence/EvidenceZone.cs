using Mirror;
using UnityEngine;

public class EvidenceZone : MonoBehaviour
{
    [Header("생성할 증거 프리팹")]
    [SerializeField] private EvidencePoint evidencePrefab; // 이 Zone에서 생성할 증거 프리팹

    [Header("증거 스폰 포인트")]
    [SerializeField] private Transform[] evidenceSpawnPoints; // 증거가 랜덤으로 생성될 후보 위치들

    // 이 Zone에서 실제로 생성된 증거다.
    private EvidencePoint spawnedEvidencePoint;

    // 같은 Zone이 중복으로 완료 처리되는 것을 막는다.
    private bool isFound;

    private void Start()
    {
        // 증거 생성과 목표 등록은 서버에서만 처리한다.
        if (!NetworkServer.active)
            return;

        // GameManager에 이 EvidenceZone을 목표로 등록한다.
        if (GameManager.Instance != null)
            GameManager.Instance.AddZone(this);

        SpawnEvidence();
    }

    // 서버에서 증거 프리팹을 랜덤 스폰포인트에 생성한다.
    [Server]
    private void SpawnEvidence()
    {
        if (spawnedEvidencePoint != null)
            return;

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

        // 이 Zone에 지정된 EvidencePoint 프리팹을 생성한다.
        EvidencePoint point = Instantiate(
            evidencePrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        // 생성된 EvidencePoint에게 이 Zone을 알려준다.
        // 증거 타입, 이름, 아이콘은 EvidencePoint 프리팹 안에 들어있다.
        point.ServerInit(this);

        // Mirror 네트워크 오브젝트로 생성한다.
        NetworkServer.Spawn(point.gameObject);

        spawnedEvidencePoint = point;

        Debug.Log(
            $"[EvidenceZone] {name} : 증거 생성 완료 / " +
            $"종류: {point.DisplayName} / 위치: {spawnPoint.name}"
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

    // 생성된 진짜 증거가 조사 완료되면 호출된다.
    [Server]
    public void OnRealEvidenceFound(EvidencePoint point, uint finderNetId)
    {
        if (isFound)
            return;

        isFound = true;

        Debug.Log(
            $"[EvidenceZone] {name} : 증거 발견 완료 / " +
            $"증거: {point.DisplayName} / 발견자 NetId: {finderNetId}"
        );

        // 현재 GameManager는 Zone 기준으로 증거 개수만 올린다.
        // 나중에 결과창 만들 때는 여기서 point.EvidenceType, point.DisplayName, point.Icon, finderNetId를 기록하면 된다.
        if (GameManager.Instance != null)
            GameManager.Instance.AddEvidence(this);
    }
}