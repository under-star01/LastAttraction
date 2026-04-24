using UnityEngine;
using Mirror;
using System.Collections;

public class Trap : NetworkBehaviour
{
    [Header("설정")]
    [SerializeField] private float stunDuration = 3.0f;   // 생존자 스턴 시간
    [SerializeField] private float destroyDelay = 3.0f;   // 발동 후 제거까지 시간
    [SerializeField] private Animator animator;

    [SyncVar]
    private bool isTriggered = false; // 중복 발동 방지

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    // 서버에서만 트랩 충돌을 감지한다.
    // 그래야 멀티에서 한 번만 발동하고 모든 클라이언트에 같은 결과가 보인다.
    [ServerCallback]
    private void OnTriggerEnter(Collider other)
    {
        if (isTriggered)
            return;

        if (!other.CompareTag("Survivor"))
            return;

        SurvivorState survivor = other.GetComponentInParent<SurvivorState>();
        if (survivor == null)
            return;

        // 다운 / 사망 / 감옥 상태에서는 트랩 스턴을 적용하지 않는다.
        if (survivor.IsDowned || survivor.IsDead || survivor.IsImprisoned)
            return;

        TriggerTrap(survivor);
    }

    // 서버에서 실제 트랩 발동 처리
    [Server]
    private void TriggerTrap(SurvivorState survivor)
    {
        if (survivor == null)
            return;

        isTriggered = true;

        // 생존자에게 공통 스턴 적용
        survivor.ApplyStun(stunDuration);

        // 트랩 자체 발동 애니메이션 동기화
        RpcPlayTriggerEffects();

        // 발동 후 네트워크 오브젝트 제거
        StartCoroutine(DestroyAfterDelay(destroyDelay));
    }

    // 모든 클라이언트에서 트랩 발동 연출 실행
    [ClientRpc]
    private void RpcPlayTriggerEffects()
    {
        if (animator != null)
            animator.SetTrigger("Snap");

        // 필요하면 여기에 발동 사운드 추가 가능
    }

    // 서버에서 트랩 제거
    [Server]
    private IEnumerator DestroyAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        NetworkServer.Destroy(gameObject);
    }
}