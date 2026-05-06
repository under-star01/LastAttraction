using UnityEngine;
using UnityEngine.UI;

public class KillerUI : MonoBehaviour
{
    [Header("공격 UI (왼쪽)")]
    [SerializeField] private GameObject attackTarget;
    private Image attackIcon;
    [SerializeField] private Color normalColor = Color.red;
    [SerializeField] private Color pressedColor = Color.gray;
    [SerializeField] private float pressedScale = 0.9f;

    [Header("트랩 UI (오른쪽)")]
    [SerializeField] private GameObject trapTarget;
    private Image trapFillIcon;
    [SerializeField] private Color cooldownColor = Color.gray;
    [SerializeField] private Color readyColor = Color.red;

    private KillerInput input;
    private KillerState state;
    private TrapHandler trapHandler;

    private void Start()
    {
        // 로컬 플레이어 컴포넌트 참조 (초기화 로직은 상황에 맞춰 수정)
        GameObject killer = transform.root.gameObject;
        input = killer.GetComponent<KillerInput>();
        state = killer.GetComponent<KillerState>();
        trapHandler = killer.GetComponent<TrapHandler>();

        if (SceneBinder.Instance != null)
        {
            attackTarget = SceneBinder.Instance.GetKillerAttackTarget();
            trapTarget = SceneBinder.Instance.GetKillerTrapTarget();
        }

        // 가져온 오브젝트에서 Image 컴포넌트 추출
        if (attackTarget != null)
            attackIcon = attackTarget.GetComponentInChildren<Image>();

        if (trapTarget != null)
            trapFillIcon = trapTarget.GetComponentInChildren<Image>();
    }

    private void Update()
    {
        if (input == null || state == null) return;

        UpdateAttackUI();
        UpdateTrapUI();
    }

    private void UpdateAttackUI()
    {
        // 클릭 유지 중일 때의 피드백 (Lunge 입력 중)
        if (input.IsAttackPressed && state.CanAttack)
        {
            attackIcon.color = pressedColor;
            attackIcon.transform.localScale = Vector3.one * pressedScale;
        }
        else
        {
            attackIcon.color = normalColor;
            attackIcon.transform.localScale = Vector3.one;
        }
    }

    private void UpdateTrapUI()
    {
        if (state.CurrentCondition == KillerCondition.Planting)
        {
            // 회색으로 변경하고[cite: 18]
            trapFillIcon.color = cooldownColor;

            // TrapHandler에서 계산된 0~1 사이의 진행도를 적용[cite: 19]
            // 1.2초 동안 시계 방향으로 차오르게 됨[cite: 18]
            trapFillIcon.fillAmount = trapHandler.PlantProgress;
        }
        else
        {
            // 설치 상태가 아니면 다시 붉은색으로, 게이지는 꽉 채움[cite: 18]
            trapFillIcon.color = readyColor;
            trapFillIcon.fillAmount = 1f;
        }
    }
}