using UnityEngine;

public class TrapNode : MonoBehaviour
{
    // 함정이 완전히 설치된 상태인지 (즉시 설치이므로 시작부터 true)
    public bool isReady = false;

    private void Start()
    {
        // 생성 즉시 작동 준비 완료
        isReady = true;

        // 여기에 초기화 로직 (예: 콜라이더 활성화, 작동 애니메이션 등)을 넣으세요.
        Debug.Log("함정 매설 완료!");
    }

    // 이후 생존자가 밟았을 때 호출될 함수 등을 여기에 구현하면 됩니다.
    /*
    private void OnTriggerEnter(Collider other)
    {
        if (isReady && other.CompareTag("Survivor"))
        {
            // 함정 발동 로직
        }
    }
    */
}