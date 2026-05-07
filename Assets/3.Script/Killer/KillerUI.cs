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
        // 로컬 플레이어 컴포넌트 참조
        GameObject killer = transform.root.gameObject;
        input = killer.GetComponent<KillerInput>();
        state = killer.GetComponent<KillerState>();
        trapHandler = killer.GetComponent<TrapHandler>();

        // 최초 1회 연결 시도
        TryBindUI();
    }

    // UI 오브젝트들을 찾아서 연결하는 별도의 함수
    private void TryBindUI()
    {
        if (SceneBinder.Instance != null)
        {
            attackTarget = SceneBinder.Instance.GetKillerAttackTarget();
            trapTarget = SceneBinder.Instance.GetKillerTrapTarget();
        }

        if (attackTarget == null) attackTarget = GameObject.Find("Killer_Skill_1_Fill");
        if (trapTarget == null) trapTarget = GameObject.Find("Killer_Skill_2_Fill");

        if (attackTarget != null) attackIcon = attackTarget.GetComponentInChildren<Image>();
        if (trapTarget != null) trapFillIcon = trapTarget.GetComponentInChildren<Image>();
    }

    private void Update()
    {
        // 1. 필수 컴포넌트가 없으면 중단
        if (input == null || state == null) return;

        // 2. 만약 UI가 아직 연결 안 됐다면 다시 시도 (인게임 진입 직후 대응)
        if (attackIcon == null || trapFillIcon == null)
        {
            TryBindUI();
            return; // 이번 프레임은 건너뜀
        }

        UpdateAttackUI();
        UpdateTrapUI();
    }

    private void UpdateAttackUI()
    {
        // 위에서 체크했으므로 attackIcon이 null일 수 없음
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
        // 위에서 체크했으므로 trapFillIcon이 null일 수 없음
        if (state.CurrentCondition == KillerCondition.Planting)
        {
            trapFillIcon.color = cooldownColor;
            trapFillIcon.fillAmount = trapHandler.PlantProgress;
        }
        else
        {
            trapFillIcon.color = readyColor;
            trapFillIcon.fillAmount = 1f;
        }
    }
}