using UnityEngine;
using System.Reflection; // 리플렉션을 사용하기 위해 필요합니다.

public class PalletTester : MonoBehaviour
{
    [Header("테스트 대상 판자")]
    public Pallet targetPallet;

    [Header("테스트 키")]
    public KeyCode dropKey = KeyCode.G;

    void Update()
    {
        // 지정한 키(G)를 누르면 판자의 private 기능을 강제로 실행합니다.
        if (Input.GetKeyDown(dropKey))
        {
            if (targetPallet == null)
            {
                Debug.LogWarning("테스트할 Pallet를 인스펙터에서 연결해주세요!");
                return;
            }

            ExecutePrivateDrop();
        }
    }

    private void ExecutePrivateDrop()
    {
        // 1. Pallet 스크립트에서 private으로 선언된 'Drop' 메서드 정보를 가져옵니다.
        MethodInfo dropMethod = typeof(Pallet).GetMethod("Drop", BindingFlags.Instance | BindingFlags.NonPublic);

        // 2. Pallet 스크립트에서 private으로 선언된 'animator' 변수 정보를 가져옵니다.
        FieldInfo animatorField = typeof(Pallet).GetField("animator", BindingFlags.Instance | BindingFlags.NonPublic);

        if (animatorField != null)
        {
            // 판자의 실제 애니메이터를 가져와서 "Drop" 트리거를 작동시킵니다.
            Animator anim = (Animator)animatorField.GetValue(targetPallet);
            if (anim != null)
            {
                anim.SetTrigger("Drop");
                Debug.Log("판자 애니메이션 실행!");
            }
        }

        if (dropMethod != null)
        {
            // 실제 판자의 상태(콜라이더 교체 등)를 변경하는 Drop() 함수를 강제로 실행합니다.
            dropMethod.Invoke(targetPallet, null);
            Debug.Log("판자 상태 변경(콜라이더 교체) 완료!");
        }
    }
}