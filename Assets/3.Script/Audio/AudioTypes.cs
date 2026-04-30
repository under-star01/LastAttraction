using UnityEngine;

// 이 소리를 누가 들을 수 있는지
public enum AudioListenerTarget
{
    LocalOnly,      // 이 클라이언트에서만 들림
    Everyone,       // 모든 플레이어가 들음
    KillerOnly,     // 킬러만 들음
    SurvivorOnly    // 생존자만 들음
}

// 2D / 3D 재생 방식
public enum AudioDimension
{
    Sound2D,    // 거리와 상관없이 바로 들리는 소리
    Sound3D     // 월드 위치 기준으로 들리는 소리
}

// 오디오 종류 이름
// 코드에서는 이 enum 값으로 소리를 찾는다
public enum AudioKey
{
    None,

    //생존자
    SurvivorFootstep,       // 발소리
    SurvivorHealLoop,       // 힐
    SurvivorEvidenceLoop,   // 증거
    SurvivorUploadLoop,     // 업로드
    SurvivorPrisonLoop      // 감옥
}