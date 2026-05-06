using Mirror;
using UnityEngine;

public class EscapeTrigger : MonoBehaviour
{
    [Header("탈출 이동 목표 지점")]
    [SerializeField] private Transform escapeTarget;

    private void OnTriggerEnter(Collider other)
    {
        // 서버에서만 탈출 처리
        if (!NetworkServer.active)
            return;

        if (escapeTarget == null)
            return;

        SurvivorMove survivorMove = other.GetComponent<SurvivorMove>();

        if (survivorMove == null)
            survivorMove = other.GetComponentInParent<SurvivorMove>();

        if (survivorMove == null)
            return;

        survivorMove.BeginEscape(escapeTarget);
    }
}