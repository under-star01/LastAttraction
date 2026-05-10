using Mirror;
using UnityEngine;

// 로비 카메라용
// 플레이어가 아직 생성되기 전에는 이 카메라의 AudioListener를 켜두고,
// 로컬 플레이어가 생성되면 자동으로 꺼줍니다.
public class LobbyAudioListener : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private Camera lobbyCamera;
    [SerializeField] private AudioListener lobbyAudioListener;

    private void Awake()
    {
        if (lobbyCamera == null)
            lobbyCamera = GetComponent<Camera>();

        if (lobbyAudioListener == null)
            lobbyAudioListener = GetComponent<AudioListener>();

        // 시작할 때는 로비 카메라가 리스너 역할
        if (lobbyCamera != null)
            lobbyCamera.enabled = true;

        if (lobbyAudioListener != null)
            lobbyAudioListener.enabled = true;
    }

    private void Update()
    {
        // 아직 로컬 플레이어가 없으면 계속 유지
        if (NetworkClient.localPlayer == null)
        {
            if (lobbyCamera != null && !lobbyCamera.enabled)
                lobbyCamera.enabled = true;

            if (lobbyAudioListener != null && !lobbyAudioListener.enabled)
                lobbyAudioListener.enabled = true;

            return;
        }

        // 로컬 플레이어가 생성되면 로비 카메라 쪽 리스너 끄기
        if (lobbyAudioListener != null && lobbyAudioListener.enabled)
            lobbyAudioListener.enabled = false;

        enabled = false;
    }
}