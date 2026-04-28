using Mirror;
using UnityEngine;

public class EvidenceZone : MonoBehaviour
{
    [SerializeField] private EvidencePoint[] points;

    // 이 구역에서 선택된 진짜 증거 1개다.
    private EvidencePoint realEvidencePoint;

    // 같은 구역이 중복 완료 처리되는 것을 막는다.
    private bool isFound;

    private void Start()
    {
        // 서버에서만 진짜 증거 선택과 구역 등록을 처리한다.
        if (!NetworkServer.active)
            return;

        if (points == null || points.Length == 0)
            points = GetComponentsInChildren<EvidencePoint>(true);

        if (GameManager.Instance != null)
            GameManager.Instance.AddZone(this);

        PickReal();
    }

    // 서버에서 여러 EvidencePoint 중 하나를 진짜 증거로 고른다.
    private void PickReal()
    {
        if (points == null || points.Length == 0)
            return;

        int randomIndex = Random.Range(0, points.Length);
        realEvidencePoint = points[randomIndex];

        for (int i = 0; i < points.Length; i++)
        {
            bool isReal = points[i] == realEvidencePoint;

            points[i].SetZone(this);
            points[i].SetIsRealEvidenceServer(isReal);
        }

        Debug.Log($"{name} : 진짜 증거는 {realEvidencePoint.name}");
    }

    // 진짜 증거가 발견되면 GameManager에 완료를 보고한다.
    public void OnRealEvidenceFound(EvidencePoint point)
    {
        if (!NetworkServer.active)
            return;

        if (isFound)
            return;

        isFound = true;

        Debug.Log($"{name} : 진짜 증거 발견 완료 - {point.name}");

        if (GameManager.Instance != null)
            GameManager.Instance.AddEvidence(this);
    }
}