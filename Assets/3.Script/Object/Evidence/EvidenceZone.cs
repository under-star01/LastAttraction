using UnityEngine;

public class EvidenceZone : MonoBehaviour
{
    [SerializeField] private EvidencePoint[] points;

    // 이 구역에서 진짜 증거 1개
    private EvidencePoint realEvidencePoint;

    private void Awake()
    {
        // 배열을 직접 안 넣었으면 자식에서 자동 수집
        if (points == null || points.Length == 0)
            points = GetComponentsInChildren<EvidencePoint>();

        SelectRandomEvidence();
    }

    // 여러 포인트 중 하나를 랜덤으로 진짜 증거로 선택
    private void SelectRandomEvidence()
    {
        if (points == null || points.Length == 0)
            return;

        int randomIndex = Random.Range(0, points.Length);
        realEvidencePoint = points[randomIndex];

        for (int i = 0; i < points.Length; i++)
        {
            bool isReal = points[i] == realEvidencePoint;

            points[i].SetIsRealEvidence(isReal);
            points[i].SetZone(this);
        }

        Debug.Log($"{name} : 진짜 증거는 {realEvidencePoint.name}");
    }

    // 진짜 증거가 발견됐을 때 호출됨
    public void OnRealEvidenceFound(EvidencePoint point)
    {
        Debug.Log($"{name} : 진짜 증거 발견 완료 - {point.name}");

        // 나중에 여기서:
        // - 총 증거 개수 증가
        // - 문 열기
        // - 목표 진행도 갱신
        // 같은 처리 추가 가능
    }
}