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
// 코드에서는 이 enum 값으로 소리를 찾는다.
public enum AudioKey
{
    None,

    // 생존자 기본 상호작용
    SurvivorFootstep,       // 발소리
    SurvivorHealLoop,       // 힐
    SurvivorEvidenceLoop,   // 증거 조사 루프
    SurvivorUploadLoop,     // 업로드 루프
    SurvivorPrisonLoop,     // 감옥 상호작용 루프

    // 생존자 피격 / 다운 / 신음
    SurvivorMaleHit,        // 남자 생존자가 맞아서 다칠 때 / 죽을 때 소리
    SurvivorFemaleHit,      // 여자 생존자가 맞아서 다칠 때 / 죽을 때 소리
    SurvivorMaleDownHit,    // 남자 생존자가 맞아서 다운될 때 소리
    SurvivorFemaleDownHit,  // 여자 생존자가 맞아서 다운될 때 소리
    SurvivorMaleGroan,      // 남자 생존자 신음소리
    SurvivorFemaleGroan,    // 여자 생존자 신음소리

    // 오브젝트
    ObjectVault,            // 판자 / 창틀 넘는 소리
    PalletDrop,             // 판자 내리는 소리
    PalletBreak,            // 판자 부수는 소리

    // UI / 목표 진행
    QTEAppear,              // QTE가 나타날 때 소리
    QTESuccess,             // QTE 성공 소리
    EscapeGateOpen,         // 탈출 문 열리는 소리

    // 살인마
    KillerFootstep,         // 살인마 발소리, 생존자에게만 들림

    // 카메라
    CameraSkillOn,          // 카메라 스킬 켤 때 소리
    CameraSkillLoop,        // 카메라 스킬 사용 중 노이즈 루프

    // 생존자 스턴 / 놀람
    SurvivorMaleStun,       // 남자 생존자 스턴 / 놀람 소리
    SurvivorFemaleStun,     // 여자 생존자 스턴 / 놀람 소리

    TrapTrigger,            // 트랩 발동 소리

    // 살인마 공격 / 레이지
    KillerWeaponSwing,      // 살인마 무기 휘두르는 소리
    KillerAttackHit,        // 살인마 공격 성공 타격 소리
    KillerRageStart,        // 살인마 Rage 상태 진입 소리

    // 감옥
    KillerIncage,           // 살인마가 생존자를 감옥에 넣을 때 소리

    // 목표 / 업로드
    UploadComputerReady,    // 목표 완료 후 업로드 컴퓨터 활성화 경고음

    // 타이틀 / 로비 / UI
    TitleBGM,                // 타이틀 씬 배경음악
    LobbyBGM,                // 로비 씬 배경음악
    UIButtonClick,           // UI 버튼 클릭음
    UIButtonHover,           // UI 버튼 호버음
    GameStart                // 게임 시작 확정 사운드
}