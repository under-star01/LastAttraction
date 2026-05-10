using UnityEngine;

public class TrapNode : MonoBehaviour
{
    // 함정이 완전히 설치된 상태인지 (즉시 설치이므로 시작부터 true)
    public bool isReady = false;

    private void Start()
    {
        // 생성 즉시 작동 준비 완료
        isReady = true;
    }
}