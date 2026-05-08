using UnityEngine;

// 자식 Animator에서 발생한 Animation Event를 루트 스크립트로 전달하는 스크립트
// 이 스크립트는 반드시 Animator가 붙어있는 자식 오브젝트에 붙여야 한다.
public class KillerAnimationEventReceiver : MonoBehaviour
{
    [Header("부모 참조")]
    [SerializeField] private KillerMove killerMove;
    [SerializeField] private KillerCombat killerCombat;

    private void Awake()
    {
        // 인스펙터에 직접 넣지 않아도 부모에서 자동으로 찾는다.
        if (killerMove == null)
            killerMove = GetComponentInParent<KillerMove>();

        if (killerCombat == null)
            killerCombat = GetComponentInParent<KillerCombat>();
    }

    // Animation Event 함수
    // 걷기 / 달리기 클립의 발이 닿는 프레임에 넣는다.
    public void KillerFootstep()
    {
        if (killerMove == null)
            killerMove = GetComponentInParent<KillerMove>();

        if (killerMove == null)
            return;

        killerMove.PlayKillerFootstepByAnimationEvent();
    }

    // Animation Event 함수
    // 공격 애니메이션에서 무기가 공기를 가르는 프레임에 넣는다.
    public void KillerWeaponSwing()
    {
        if (killerCombat == null)
            killerCombat = GetComponentInParent<KillerCombat>();

        if (killerCombat == null)
            return;

        killerCombat.PlayKillerWeaponSwingByAnimationEvent();
    }
}