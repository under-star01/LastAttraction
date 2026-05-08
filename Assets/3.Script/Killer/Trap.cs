using UnityEngine;
using Mirror;
using System.Collections;

public class Trap : NetworkBehaviour
{
    [Header("설정")]
    [SerializeField] private float stunDuration = 3.0f;   // 생존자 스턴 시간
    [SerializeField] private float destroyDelay = 3.0f;   // 발동 후 제거까지 시간
    [SerializeField] private Animator animator;

    [Header("오디오")]
    [SerializeField] private AudioKey triggerSoundKey = AudioKey.TrapTrigger; // 트랩 발동 소리
    [SerializeField] private Vector3 soundOffset = new Vector3(0f, 0.15f, 0f); // 바닥보다 살짝 위에서 재생

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

        // 트랩 발동 순간 모든 클라이언트에게 3D 사운드를 한 번 재생한다.
        PlayTriggerSound();

        // 생존자에게 공통 스턴 적용
        // SurvivorState.ApplyStun 안에서 성별 놀람 소리도 같이 재생된다.
        survivor.ApplyStun(stunDuration);

        // 트랩 자체 발동 애니메이션 동기화
        RpcPlayTriggerEffects();

        // 발동 후 네트워크 오브젝트 제거
        StartCoroutine(DestroyAfterDelay(destroyDelay));
    }

    // 서버에서 트랩 발동 사운드 재생
    [Server]
    private void PlayTriggerSound()
    {
        if (triggerSoundKey == AudioKey.None)
            return;

        if (NetworkAudioManager.Instance == null)
            return;

        NetworkAudioManager.PlayAudioForEveryone(
            triggerSoundKey,
            AudioDimension.Sound3D,
            transform.position + soundOffset
        );
    }

    // 모든 클라이언트에서 트랩 발동 연출 실행
    [ClientRpc]
    private void RpcPlayTriggerEffects()
    {
        if (animator != null)
            animator.SetTrigger("Snap");
    }

    // 서버에서 트랩 제거
    [Server]
    private IEnumerator DestroyAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        NetworkServer.Destroy(gameObject);
    }
}