using UnityEngine;

// 이 소리를 누가 들을 수 있는지
public enum AudioListenerTarget
{
    LocalOnly,      // 로컬 사운드
    Everyone,       // 모든 플레이어
    KillerOnly,     // 킬러만
    SurvivorOnly    // 생존자만
}

// 2D/3D
public enum AudioDimension
{
    Sound2D,
    Sound3D
}

// 오디오 이름
public enum AudioKey
{
    None,
    ButtonClick,
    PalletDrop,
    WindowVault,
    KillerAttack,
    EvidenceSearch,
    PrisonOpen,
    PrisonClose
}