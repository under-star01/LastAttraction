using Mirror;
using UnityEngine;

/// <summary>
/// 상호작용 물체 위에 원형 아이콘이 표시될 위치와 표시 조건을 관리합니다.
/// 실제 상호작용 범위와 아이콘 표시 범위를 분리하기 위한 스크립트입니다.
/// 
/// 이 스크립트는 네트워크 동기화가 필요 없습니다.
/// 각 클라이언트가 로컬 생존자 기준으로 아이콘 표시 여부를 판단합니다.
/// </summary>
public class InteractIconPoint : MonoBehaviour
{
    [Header("아이콘 표시 위치")]
    [SerializeField] private Transform iconPoint;

    [Header("표시 옵션")]
    [SerializeField] private bool canShowIcon = true;

    [Tooltip("켜두면 Hold 상호작용 물체만 아이콘 표시 대상이 됩니다.")]
    [SerializeField] private bool holdOnly = true;

    public bool CanShowIcon => canShowIcon;

    /// <summary>
    /// 아이콘이 표시될 월드 위치를 반환합니다.
    /// iconPoint가 없으면 현재 오브젝트 위쪽을 사용합니다.
    /// </summary>
    public Vector3 GetIconWorldPosition()
    {
        if (iconPoint != null)
            return iconPoint.position;

        return transform.position + Vector3.up * 1.5f;
    }

    /// <summary>
    /// 외부에서 강제로 아이콘 표시 여부를 끄고 싶을 때 사용합니다.
    /// 예: 특정 이벤트 후 임시 비활성화 등
    /// </summary>
    public void SetCanShowIcon(bool value)
    {
        canShowIcon = value;
    }

    /// <summary>
    /// 현재 로컬 생존자 기준으로 이 오브젝트 아이콘을 보여도 되는지 판단합니다.
    /// 실제 상호작용 범위가 아니라, 아이콘 표시 가능 상태만 검사합니다.
    /// </summary>
    public bool CanShowIconFor(SurvivorState viewer, IInteractable interactable)
    {
        if (!canShowIcon)
            return false;

        if (viewer == null)
            return false;

        if (interactable == null)
            return false;

        // 사망 / 다운 상태에서는 어떤 상호작용 안내도 보여주지 않습니다.
        if (viewer.IsDead || viewer.IsDowned)
            return false;

        // 원 아이콘은 기본적으로 Hold 상호작용용으로만 사용합니다.
        if (holdOnly && interactable.InteractType != InteractType.Hold)
            return false;

        // 증거 전용 조건
        if (interactable is EvidencePoint evidence)
            return CanShowEvidenceIcon(viewer, evidence);

        // 업로드 컴퓨터 전용 조건
        if (interactable is UploadComputer computer)
            return CanShowUploadComputerIcon(viewer, computer);

        // 감옥 전용 조건
        if (interactable is Prison prison)
            return CanShowPrisonIcon(viewer, prison);

        // 위에서 따로 처리하지 않은 Hold 상호작용은 기본 표시 허용
        return true;
    }

    /// <summary>
    /// 증거 아이콘 표시 조건입니다.
    /// 실제 상호작용 트리거보다 넓은 detectRadius 안에서 보이지만,
    /// 다른 생존자가 이미 조사 중이면 내 아이콘은 숨깁니다.
    /// 완료된 증거는 EvidencePoint에서 Collider를 끄기 때문에 탐지에서 자연스럽게 빠집니다.
    /// </summary>
    private bool CanShowEvidenceIcon(SurvivorState viewer, EvidencePoint evidence)
    {
        if (evidence == null)
            return false;

        // 감옥에 갇힌 상태에서는 증거 아이콘을 볼 필요가 없습니다.
        if (viewer.IsImprisoned)
            return false;

        // 다른 생존자가 조사 중이면 사용 불가 상태이므로 숨깁니다.
        if (evidence.IsInteractingForUI && evidence.CurrentInteractorNetId != viewer.netId)
            return false;

        return true;
    }

    /// <summary>
    /// 업로드 컴퓨터 아이콘 표시 조건입니다.
    /// 목표 완료 후 GameManager가 SetOpen(true)를 호출한 뒤부터 보입니다.
    /// 업로드 완료 후 문 대기 상태나 문이 열린 뒤에는 숨깁니다.
    /// </summary>
    private bool CanShowUploadComputerIcon(SurvivorState viewer, UploadComputer computer)
    {
        if (computer == null)
            return false;

        if (viewer.IsImprisoned)
            return false;

        // 목표 완료 전에는 컴퓨터 아이콘이 보이지 않습니다.
        if (!computer.IsOpen)
            return false;

        // 업로드 완료 후 탈출문 개방 대기 중이면 더 이상 업로드 상호작용 안내를 보여주지 않습니다.
        if (computer.GateTimerVisible)
            return false;

        // 탈출문이 열린 뒤에도 컴퓨터 아이콘은 숨깁니다.
        if (computer.GateOpened)
            return false;

        return true;
    }

    /// <summary>
    /// 감옥 아이콘 표시 조건입니다.
    /// 감옥에 생존자가 갇혀 있을 때만 보입니다.
    /// 갇힌 본인은 자기 감옥에만 보이고, 다른 생존자는 구조 가능한 감옥에 보입니다.
    /// </summary>
    private bool CanShowPrisonIcon(SurvivorState viewer, Prison prison)
    {
        if (prison == null)
            return false;

        // 폐쇄된 감옥은 표시하지 않습니다.
        if (prison.IsDisabled)
            return false;

        // 빈 감옥은 표시하지 않습니다.
        if (!prison.IsOccupied)
            return false;

        // 다른 생존자가 이미 감옥 상호작용 중이면 표시하지 않습니다.
        if (prison.IsInteractingForUI && prison.CurrentUserId != viewer.netId)
            return false;

        // 내가 감옥에 갇혀 있다면 내 감옥에만 표시합니다.
        if (viewer.IsImprisoned)
            return prison.netId == viewer.CurrentPrisonId;

        // 일반 생존자는 갇힌 생존자가 있는 감옥을 구조 대상으로 볼 수 있습니다.
        return true;
    }
}