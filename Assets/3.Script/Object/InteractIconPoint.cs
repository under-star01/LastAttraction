using UnityEngine;

public class InteractIconPoint : MonoBehaviour
{
    [Header("아이콘 표시 위치")]
    [SerializeField] private Transform iconPoint;

    [Header("표시 옵션")]
    [SerializeField] private bool canShowIcon = true;

    [Tooltip("켜두면 Hold 상호작용 물체만 아이콘 표시 대상이 됩니다.")]
    [SerializeField] private bool holdOnly = true;

    public bool CanShowIcon => canShowIcon;

    public Vector3 GetIconWorldPosition()
    {
        if (iconPoint != null)
            return iconPoint.position;

        return transform.position + Vector3.up * 1.5f;
    }

    public void SetCanShowIcon(bool value)
    {
        canShowIcon = value;
    }

    public bool CanShowIconFor(SurvivorState viewer, IInteractable interactable)
    {
        if (!canShowIcon)
            return false;

        if (viewer == null)
            return false;

        if (interactable == null)
            return false;

        if (viewer.IsDead || viewer.IsDowned)
            return false;

        if (holdOnly && interactable.InteractType != InteractType.Hold)
            return false;

        if (interactable is EvidencePoint evidence)
            return CanShowEvidenceIcon(viewer, evidence);

        if (interactable is UploadComputer computer)
            return CanShowUploadComputerIcon(viewer, computer);

        if (interactable is Prison prison)
            return CanShowPrisonIcon(viewer, prison);

        return true;
    }

    private bool CanShowEvidenceIcon(SurvivorState viewer, EvidencePoint evidence)
    {
        if (evidence == null)
            return false;

        if (viewer.IsImprisoned)
            return false;

        // 완료된 증거는 숨겨져서 탐지에서 빠지고,
        // 목표 완료로 상호작용만 막힌 증거는 여기서 원 아이콘을 숨긴다.
        if (!evidence.CanShowInteractIcon)
            return false;

        if (evidence.IsInteractingForUI && evidence.CurrentInteractorNetId != viewer.netId)
            return false;

        return true;
    }

    private bool CanShowUploadComputerIcon(SurvivorState viewer, UploadComputer computer)
    {
        if (computer == null)
            return false;

        if (viewer.IsImprisoned)
            return false;

        if (!computer.IsOpen)
            return false;

        if (computer.GateTimerVisible)
            return false;

        if (computer.GateOpened)
            return false;

        return true;
    }

    private bool CanShowPrisonIcon(SurvivorState viewer, Prison prison)
    {
        if (prison == null)
            return false;

        if (prison.IsDisabled)
            return false;

        if (!prison.IsOccupied)
            return false;

        if (prison.IsInteractingForUI && prison.CurrentUserId != viewer.netId)
            return false;

        if (viewer.IsImprisoned)
            return prison.netId == viewer.CurrentPrisonId;

        return true;
    }
}