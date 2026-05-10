using Mirror;
using UnityEngine;

// 플레이어 UI에 표시할 정보를 네트워크로 동기화한다.
// - 닉네임은 DB 로그인 데이터에서 가져온다.
// - 초상화는 생존자 프리팹마다 인스펙터에서 직접 설정한다.
public class PlayerUIProfile : NetworkBehaviour
{
    [Header("프리팹별 UI 이미지")]
    [SerializeField] private Sprite portrait;

    [Header("DB 유저 정보")]
    [SyncVar] private int accountId;
    [SyncVar] private string loginId;
    [SyncVar] private string nickname;
    [SyncVar] private int exp;
    [SyncVar] private int level;

    public Sprite Portrait => portrait;

    public int AccountId => accountId;
    public string LoginId => loginId;
    public string Nickname => nickname;
    public int Exp => exp;
    public int Level => level;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(nickname))
                return nickname;

            return "NickName";
        }
    }

    // 서버에서 플레이어 생성 직후 DB 유저 정보를 넣어준다.
    [Server]
    public void SetUserData(int newAccountId, string newLoginId, string newNickname, int newExp, int newLevel)
    {
        accountId = newAccountId;
        loginId = newLoginId;
        nickname = newNickname;
        exp = newExp;
        level = newLevel;
    }
}