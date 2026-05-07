using System.Collections;
using Mirror;
using UnityEngine;

public class Window : NetworkBehaviour, IInteractable
{
    // 창틀은 한 번 누르면 넘는 Press 타입 상호작용이다.
    public InteractType InteractType => InteractType.Press;

    [Header("참조")]
    [SerializeField] private Transform leftPoint;              // 창틀 왼쪽 사용 위치
    [SerializeField] private Transform rightPoint;             // 창틀 오른쪽 사용 위치

    [Header("이동/연출 설정")]
    [SerializeField] private float moveToPointSpeed = 5f;      // 시작 위치로 이동하는 속도
    [SerializeField] private float survivorVaultSpeed = 4f;    // 생존자가 넘는 속도
    [SerializeField] private float killerVaultSpeed = 2.5f;    // 살인마가 넘는 속도
    [SerializeField] private float occupationRadius = 1.0f;    // 반대편 점유 검사 반경

    [Header("오디오")]
    [SerializeField] private AudioKey vaultSoundKey = AudioKey.ObjectVault; // 창틀 넘는 소리

    // 현재 창틀이 사용 중인지 서버에서 동기화한다.
    [SyncVar] private bool isBusy;

    // 실제 넘는 동작 중인지 서버에서 동기화한다.
    [SyncVar] private bool isVaulting;

    // 현재 창틀을 사용 중인 액터 netId다.
    [SyncVar] private uint currentActorNetId;

    // 로컬 플레이어의 SurvivorInteractor 참조다.
    private SurvivorInteractor localInteractor;

    // 로컬 플레이어가 창틀 트리거 안에 있는지 여부다.
    private bool isLocalInside;

    private void Update()
    {
        // 로컬 플레이어가 트리거 안에 있으면 상호작용 후보 상태를 계속 갱신한다.
        RefreshLocalAvailability();
    }

    public void BeginInteract(GameObject actor)
    {
        // 액터가 없으면 시작하지 않는다.
        if (actor == null)
            return;

        // 액터의 NetworkIdentity를 찾는다.
        NetworkIdentity actorIdentity = actor.GetComponent<NetworkIdentity>();

        // 콜라이더나 자식 구조일 수 있으므로 부모에서도 찾는다.
        if (actorIdentity == null)
            actorIdentity = actor.GetComponentInParent<NetworkIdentity>();

        // NetworkIdentity가 없으면 네트워크 상호작용을 할 수 없다.
        if (actorIdentity == null)
            return;

        // 이미 사용 중이면 시작하지 않는다.
        if (isBusy || isVaulting)
            return;

        // 서버라면 바로 시작 판정을 한다.
        if (isServer)
            TryBegin(actorIdentity);
        else
            CmdBegin(actorIdentity.netId);
    }

    public void EndInteract()
    {
        // Press 타입이라 종료 처리 없음
    }

    [Command(requiresAuthority = false)]
    private void CmdBegin(uint actorNetId)
    {
        // 클라이언트가 보낸 netId로 서버의 NetworkIdentity를 찾는다.
        if (!NetworkServer.spawned.TryGetValue(actorNetId, out NetworkIdentity actorIdentity))
            return;

        // 서버에서 실제 시작 판정을 한다.
        TryBegin(actorIdentity);
    }

    // 서버에서 실제 사용 가능 여부 확인 후 시작한다.
    [Server]
    private void TryBegin(NetworkIdentity actorIdentity)
    {
        // 액터 정보가 없으면 중단한다.
        if (actorIdentity == null)
            return;

        // 이미 사용 중이면 중단한다.
        if (isBusy || isVaulting)
            return;

        // 실제 액터 GameObject를 가져온다.
        GameObject actor = actorIdentity.gameObject;

        // 액터가 없으면 중단한다.
        if (actor == null)
            return;

        // 생존자인지 확인한다.
        bool isSurvivor = actor.CompareTag("Survivor");

        // 살인마인지 확인한다.
        bool isKiller = actor.CompareTag("Killer");

        // 생존자도 살인마도 아니면 사용할 수 없다.
        if (!isSurvivor && !isKiller)
            return;

        // 액터가 현재 어느 쪽에 있는지 확인한다.
        Transform sidePoint = GetSide(actor.transform);

        // 사용 위치를 못 찾으면 중단한다.
        if (sidePoint == null)
            return;

        // 생존자는 반대편 살인마를 검사하고, 살인마는 반대편 생존자를 검사한다.
        string opponentTag = isSurvivor ? "Killer" : "Survivor";

        // 같은 사용 위치에 상대가 있으면 겹침 방지를 위해 사용하지 않는다.
        if (IsOpponentAtPoint(sidePoint, opponentTag))
            return;

        // 서버 기준 거리 검사에서 멀면 사용하지 않는다.
        if (!CanUse(actor.transform))
            return;

        // 창틀을 사용 중 상태로 변경한다.
        isBusy = true;

        // 현재 사용자를 저장한다.
        currentActorNetId = actorIdentity.netId;

        // 생존자면 생존자 넘기 루틴을 실행한다.
        if (isSurvivor)
            StartCoroutine(SurvivorVault(actorIdentity));
        else
            StartCoroutine(KillerVault(actorIdentity));
    }

    // 생존자 창틀 넘기 루틴이다.
    [Server]
    private IEnumerator SurvivorVault(NetworkIdentity actorIdentity)
    {
        // 액터 정보가 없으면 상태를 정리하고 종료한다.
        if (actorIdentity == null)
        {
            StopVault();
            yield break;
        }

        // 실제 액터 GameObject를 가져온다.
        GameObject actor = actorIdentity.gameObject;

        // 액터가 없으면 상태를 정리하고 종료한다.
        if (actor == null)
        {
            StopVault();
            yield break;
        }

        // 생존자 이동 컴포넌트를 가져온다.
        SurvivorMove move = actor.GetComponent<SurvivorMove>();

        // 생존자 행동 상태 컴포넌트를 가져온다.
        SurvivorActionState act = actor.GetComponent<SurvivorActionState>();

        // CharacterController를 가져온다.
        CharacterController controller = actor.GetComponent<CharacterController>();

        // 현재 액터가 있는 쪽 포인트를 구한다.
        Transform sidePoint = GetSide(actor.transform);

        // 반대편 포인트를 구한다.
        Transform oppositePoint = GetOpposite(sidePoint);

        // 양쪽 포인트 중 하나라도 없으면 종료한다.
        if (sidePoint == null || oppositePoint == null)
        {
            StopVault();
            yield break;
        }

        // 넘기 시작 전에 이동 잠금과 방향 정렬을 한다.
        if (move != null)
        {
            // 상호작용 중 플레이어 이동을 막는다.
            move.SetMoveLock(true);

            // 창틀을 바라볼 방향을 구한다.
            Vector3 lookDir = GetLook(sidePoint);

            // 방향이 유효하면 모델을 창틀 방향으로 돌린다.
            if (lookDir.sqrMagnitude > 0.001f)
                move.FaceDirection(lookDir.normalized);

            // 이동 애니메이션을 idle 쪽으로 정리한다.
            move.StopAnimation();

            // 카메라 스킬 애니메이션이 켜져 있으면 끈다.
            move.SetCamAnim(false);
        }

        // 카메라 스킬 상태를 끈다.
        if (act != null)
            act.SetCam(false);

        // 직접 위치를 이동시킬 것이므로 CharacterController를 잠시 끈다.
        if (controller != null)
            controller.enabled = false;

        // CharacterController 비활성화가 반영될 시간을 한 프레임 준다.
        yield return null;

        // 먼저 자기 쪽 시작 위치로 이동한다.
        yield return MoveTo(actor.transform, sidePoint.position, moveToPointSpeed);

        // 실제 넘기 상태를 켠다.
        isVaulting = true;

        // 창틀 넘기 소리를 모든 클라이언트에게 3D 사운드로 재생한다.
        PlayOneShotSound(vaultSoundKey, transform.position);

        // 행동 상태를 Vault로 바꿔 다른 행동을 막는다.
        if (act != null)
        {
            act.SetCam(false);
            act.SetAct(SurvivorAction.Vault);
        }

        // 넘기 애니메이션을 재생한다.
        if (move != null)
        {
            // Animator Bool을 켠다.
            move.SetVaulting(true);

            // 왼쪽/오른쪽 방향에 맞는 애니메이션 Trigger를 사용한다.
            if (sidePoint == leftPoint)
                move.PlayAnimation("LeftVault");
            else
                move.PlayAnimation("RightVault");
        }

        // 반대편 위치로 이동한다.
        yield return MoveTo(actor.transform, oppositePoint.position, survivorVaultSpeed);

        // 이동이 끝났으므로 CharacterController를 다시 켠다.
        if (controller != null)
            controller.enabled = true;

        // 이동 잠금과 볼트 애니메이션 Bool을 해제한다.
        if (move != null)
        {
            move.SetVaulting(false);
            move.SetMoveLock(false);
        }

        // Vault 행동 상태를 해제한다.
        if (act != null)
            act.ClearAct(SurvivorAction.Vault);

        // 창틀 사용 상태를 정리한다.
        StopVault();

        // 중요:
        // 넘은 뒤에도 로컬 플레이어가 창틀 트리거 안에 남아 있으면 다시 후보로 잡히도록 클라이언트 쪽에서 보정한다.
        RpcRefreshLocalUse();
    }

    // 살인마 창틀 넘기 루틴이다.
    [Server]
    private IEnumerator KillerVault(NetworkIdentity actorIdentity)
    {
        // 액터 정보가 없으면 상태를 정리하고 종료한다.
        if (actorIdentity == null)
        {
            StopVault();
            yield break;
        }

        // 실제 액터 GameObject를 가져온다.
        GameObject actor = actorIdentity.gameObject;

        // 액터가 없으면 상태를 정리하고 종료한다.
        if (actor == null)
        {
            StopVault();
            yield break;
        }

        // 살인마 상태 컴포넌트를 가져온다.
        KillerState killerState = actor.GetComponent<KillerState>();

        // CharacterController를 가져온다.
        CharacterController controller = actor.GetComponent<CharacterController>();

        // 애니메이터를 가져온다.
        Animator animator = actor.GetComponentInChildren<Animator>();

        // 현재 액터가 있는 쪽 포인트를 구한다.
        Transform sidePoint = GetSide(actor.transform);

        // 반대편 포인트를 구한다.
        Transform oppositePoint = GetOpposite(sidePoint);

        // 양쪽 포인트 중 하나라도 없으면 종료한다.
        if (sidePoint == null || oppositePoint == null)
        {
            StopVault();
            yield break;
        }

        // 살인마 상태를 Vaulting으로 바꾼다.
        if (killerState != null)
            killerState.ChangeState(KillerCondition.Vaulting);

        // 직접 위치 이동을 위해 CharacterController를 잠시 끈다.
        if (controller != null)
            controller.enabled = false;

        // CharacterController 비활성화가 반영될 시간을 한 프레임 준다.
        yield return null;

        // 먼저 자기 쪽 시작 위치로 이동한다.
        yield return MoveTo(actor.transform, sidePoint.position, moveToPointSpeed);

        // 실제 넘기 상태를 켠다.
        isVaulting = true;

        // 창틀 넘기 소리를 모든 클라이언트에게 3D 사운드로 재생한다.
        PlayOneShotSound(vaultSoundKey, transform.position);

        // 창틀을 바라보는 방향을 구한다.
        Vector3 lookDir = GetLook(sidePoint);

        // 방향이 유효하면 살인마를 창틀 방향으로 돌린다.
        if (lookDir.sqrMagnitude > 0.001f)
            actor.transform.rotation = Quaternion.LookRotation(lookDir.normalized);

        // 살인마 넘기 애니메이션을 재생한다.
        if (animator != null)
            animator.SetTrigger("Vault");

        // 반대편 위치로 이동한다.
        yield return MoveTo(actor.transform, oppositePoint.position, killerVaultSpeed);

        // 이동이 끝났으므로 CharacterController를 다시 켠다.
        if (controller != null)
            controller.enabled = true;

        // 살인마 상태를 Idle로 되돌린다.
        if (killerState != null)
            killerState.ChangeState(KillerCondition.Idle);

        // 창틀 사용 상태를 정리한다.
        StopVault();
    }

    // 서버에서 창틀 사용 상태를 초기화한다.
    [Server]
    private void StopVault()
    {
        // 사용 중 상태를 해제한다.
        isBusy = false;

        // 넘기 중 상태를 해제한다.
        isVaulting = false;

        // 현재 사용자 정보를 초기화한다.
        currentActorNetId = 0;
    }

    // 서버에서 넘기 완료 후 클라이언트에게 후보 갱신을 요청한다.
    [ClientRpc]
    private void RpcRefreshLocalUse()
    {
        // 로컬 플레이어가 창틀 안에 있다면 다시 후보로 등록한다.
        RefreshLocalAvailability();
    }

    // 로컬 플레이어 기준으로 창틀 후보 등록 상태를 갱신한다.
    private void RefreshLocalAvailability()
    {
        // 로컬 플레이어가 창틀 트리거 안에 없으면 처리하지 않는다.
        if (!isLocalInside)
            return;

        // 로컬 상호작용 컴포넌트가 없으면 처리하지 않는다.
        if (localInteractor == null)
            return;

        // 창틀이 사용 중이면 후보에서 제거한다.
        if (isBusy || isVaulting)
        {
            localInteractor.ClearInteractable(this);
            return;
        }

        // 창틀이 사용 가능하면 후보로 다시 등록한다.
        localInteractor.SetInteractable(this);
    }

    // 액터가 현재 왼쪽/오른쪽 중 어느 쪽에 있는지 구한다.
    private Transform GetSide(Transform actor)
    {
        // 액터가 없으면 null을 반환한다.
        if (actor == null)
            return null;

        // 액터 위치를 창틀 기준 로컬 좌표로 변환한다.
        Vector3 localPos = transform.InverseTransformPoint(actor.position);

        // 로컬 x가 0보다 작으면 왼쪽이다.
        if (localPos.x < 0f)
            return leftPoint;
        else
            return rightPoint;
    }

    // 현재 포인트의 반대편 포인트를 구한다.
    private Transform GetOpposite(Transform sidePoint)
    {
        // 현재 왼쪽이면 오른쪽을 반환한다.
        if (sidePoint == leftPoint)
            return rightPoint;

        // 현재 오른쪽이면 왼쪽을 반환한다.
        if (sidePoint == rightPoint)
            return leftPoint;

        // 둘 다 아니면 null을 반환한다.
        return null;
    }

    // 각 포인트에서 창틀을 바라볼 방향을 구한다.
    private Vector3 GetLook(Transform sidePoint)
    {
        // 왼쪽에서는 transform.right 방향을 바라본다.
        if (sidePoint == leftPoint)
            return transform.right;

        // 오른쪽에서는 -transform.right 방향을 바라본다.
        if (sidePoint == rightPoint)
            return -transform.right;

        // 포인트가 잘못되면 zero를 반환한다.
        return Vector3.zero;
    }

    // 서버에서 액터를 목표 위치까지 이동시킨다.
    [Server]
    private IEnumerator MoveTo(Transform actor, Vector3 targetPos, float speed)
    {
        // 액터가 없으면 종료한다.
        if (actor == null)
            yield break;

        // 목표 위치에 충분히 가까워질 때까지 이동한다.
        while ((actor.position - targetPos).sqrMagnitude > 0.0001f)
        {
            // 지정된 속도로 목표 위치까지 이동한다.
            actor.position = Vector3.MoveTowards(actor.position, targetPos, speed * Time.deltaTime);

            // 다음 프레임까지 대기한다.
            yield return null;
        }

        // 마지막 위치 오차를 제거한다.
        actor.position = targetPos;
    }

    // 사용 위치에 상대 진영이 있는지 검사한다.
    private bool IsOpponentAtPoint(Transform targetPoint, string opponentTag)
    {
        // 검사 위치가 없으면 점유되지 않은 것으로 처리한다.
        if (targetPoint == null)
            return false;

        // 지정 반경 안의 콜라이더를 찾는다.
        Collider[] hits = Physics.OverlapSphere(targetPoint.position, occupationRadius);

        // 감지된 콜라이더를 순회한다.
        for (int i = 0; i < hits.Length; i++)
        {
            // 상대 태그가 있으면 점유된 것으로 처리한다.
            if (hits[i].CompareTag(opponentTag))
                return true;
        }

        // 상대가 없으면 사용 가능하다.
        return false;
    }

    // 서버 기준으로 액터가 창틀을 사용할 수 있는 거리인지 검사한다.
    private bool CanUse(Transform actorTransform)
    {
        // 액터가 없으면 사용할 수 없다.
        if (actorTransform == null)
            return false;

        // 창틀 루트의 Collider를 찾는다.
        Collider col = GetComponent<Collider>();

        // 루트에 없으면 자식에서 찾는다.
        if (col == null)
            col = GetComponentInChildren<Collider>();

        // Collider가 없으면 거리 판정을 할 수 없다.
        if (col == null)
            return false;

        // 창틀 Collider에서 액터와 가장 가까운 지점을 구한다.
        Vector3 closest = col.ClosestPoint(actorTransform.position);

        // 액터와 가장 가까운 지점 사이의 제곱 거리를 구한다.
        float sqrDist = (closest - actorTransform.position).sqrMagnitude;

        // 4m 이내면 사용할 수 있다.
        return sqrDist <= 4f;
    }

    // 로컬 생존자가 창틀 트리거에 들어오면 호출된다.
    private void OnTriggerEnter(Collider other)
    {
        // 생존자만 처리한다.
        if (!other.CompareTag("Survivor"))
            return;

        // 들어온 오브젝트에서 SurvivorInteractor를 찾는다.
        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();

        // 자식 콜라이더일 수 있으므로 부모에서도 찾는다.
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        // 상호작용 컴포넌트가 없으면 처리하지 않는다.
        if (interactor == null)
            return;

        // 로컬 플레이어가 아니면 후보 등록하지 않는다.
        if (!interactor.isLocalPlayer)
            return;

        // 로컬 상호작용 컴포넌트를 저장한다.
        localInteractor = interactor;

        // 로컬 플레이어가 창틀 안에 있다고 저장한다.
        isLocalInside = true;

        // 바로 후보 등록 상태를 갱신한다.
        RefreshLocalAvailability();
    }

    // 트리거 안에 계속 머무르는 동안 후보 등록을 보정한다.
    private void OnTriggerStay(Collider other)
    {
        // 생존자만 처리한다.
        if (!other.CompareTag("Survivor"))
            return;

        // 들어와 있는 오브젝트에서 SurvivorInteractor를 찾는다.
        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();

        // 자식 콜라이더일 수 있으므로 부모에서도 찾는다.
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        // 상호작용 컴포넌트가 없으면 처리하지 않는다.
        if (interactor == null)
            return;

        // 로컬 플레이어가 아니면 처리하지 않는다.
        if (!interactor.isLocalPlayer)
            return;

        // 로컬 상호작용 컴포넌트를 다시 저장한다.
        localInteractor = interactor;

        // 트리거 안에 있다고 보정한다.
        isLocalInside = true;

        // 창틀 후보 등록을 계속 보정한다.
        RefreshLocalAvailability();
    }

    // 로컬 생존자가 창틀 트리거에서 나가면 호출된다.
    private void OnTriggerExit(Collider other)
    {
        // 생존자만 처리한다.
        if (!other.CompareTag("Survivor"))
            return;

        // 나간 오브젝트에서 SurvivorInteractor를 찾는다.
        SurvivorInteractor interactor = other.GetComponent<SurvivorInteractor>();

        // 자식 콜라이더일 수 있으므로 부모에서도 찾는다.
        if (interactor == null)
            interactor = other.GetComponentInParent<SurvivorInteractor>();

        // 상호작용 컴포넌트가 없으면 처리하지 않는다.
        if (interactor == null)
            return;

        // 로컬 플레이어가 아니면 처리하지 않는다.
        if (!interactor.isLocalPlayer)
            return;

        // 이 창틀을 상호작용 후보에서 제거한다.
        interactor.ClearInteractable(this);

        // 로컬 플레이어가 창틀 밖에 있다고 저장한다.
        isLocalInside = false;

        // 저장된 로컬 플레이어가 나간 플레이어와 같으면 참조를 정리한다.
        if (localInteractor == interactor)
            localInteractor = null;
    }

    // 서버에서 일회성 3D 사운드를 모든 클라이언트에게 재생한다.
    [Server]
    private void PlayOneShotSound(AudioKey key, Vector3 position)
    {
        if (key == AudioKey.None)
            return;

        NetworkAudioManager.PlayAudioForEveryone(
            key,
            AudioDimension.Sound3D,
            position
        );
    }

    private void OnDrawGizmosSelected()
    {
        // 왼쪽 포인트 점유 검사 범위를 표시한다.
        if (leftPoint != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(leftPoint.position, occupationRadius);
        }

        // 오른쪽 포인트 점유 검사 범위를 표시한다.
        if (rightPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(rightPoint.position, occupationRadius);
        }
    }
}