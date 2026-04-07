using UnityEngine;

// 상호작용 종류
public enum InteractType
{
    Hold,   // 누르고 있는 동안 진행되는 상호작용 (증거, 구출)
    Press   // 버튼 1번 누르면 바로 실행되는 상호작용 (판자, 창틀)
}

// 모든 상호작용 오브젝트가 따라야 하는 규칙
public interface IInteractable
{
    // 이 오브젝트가 Hold 타입인지 Press 타입인지 반환
    InteractType InteractType { get; }

    // 상호작용 시작
    void BeginInteract(GameObject actor);

    // 상호작용 종료 Press는 안씀
    void EndInteract();
}