# LastAttraction

<p align="center">
  <img height="240" alt="Last Attraction Screenshot 1" src="https://github.com/user-attachments/assets/e2015ad4-d228-4e1c-af80-1a516d1afaeb" />
  <img height="240" alt="Last Attraction Screenshot 2" src="https://github.com/user-attachments/assets/96b35bb0-2813-49d1-a1ab-03a707c5b3f4" />
</p>

**Last Attraction**은 폐쇄된 놀이공원을 배경으로 한  
**1vs4 비대칭 멀티플레이 호러 서바이벌 프로젝트**입니다.

수년 전 의문의 사고로 폐쇄된 놀이공원 **Last Attraction**에  
사건의 진실을 쫓는 기자들이 잠입합니다.

하지만 침입자를 알아챈 **살인마 광대**는 출입구를 닫아버리고,  
기자들은 광대의 추격을 피해 **카메라 촬영과 범행 도구 수집**을 통해 증거를 확보해야 합니다.

생존자는 확보한 증거를 송출하고 출입구를 열어 탈출해야 하며,  
살인마는 기자들을 추적해 공격하고 철창에 감금하여 탈출을 방해하는 것을 목표로 합니다.

현재 저장소의 빌드는 **최종 프로젝트 버전** 기준으로 정리되어 있습니다.

---

## 프로젝트 개요

- **프로젝트명** : Last Attraction
- **장르** : 1vs4 비대칭 멀티플레이 호러 서바이벌
- **개발 인원** : 5인 팀 프로젝트
- **개발 기간** : 2026.03.18 ~ 2026.06.01
- **개발 환경** : Unity 6.2, C#
- **네트워크** : AWS EC2, Mirror
- **데이터베이스** : AWS EC2, MariaDB

---

## 주요 플레이 요소

- 생존자 4인 vs 살인마 1인의 비대칭 구조
- 증거 수집, 촬영, 업로드로 이어지는 생존자 탈출 루프
- 추적, 공격, 다운, 철창 감금으로 이어지는 살인마 추격 루프
- QTE 기반 증거 상호작용
- 카메라 촬영을 통한 살인마 증거 확보
- 트랩, 감금, 광폭화 등 살인마 방해 요소
- UI, Lighting, SFX를 활용한 공포 분위기 연출

---

## 최종 구현 상태

- AWS EC2 기반 Dedicated Server 구축
- DB 서버 연동 및 로그인 / 회원가입
- Killer / Survivor 역할 기반 매칭 시스템
- Lobby에서 InGame으로 이어지는 게임 진행 흐름
- 생존자 / 살인마 기본 플레이 구현
- 증거 상호작용 QTE 구현
- 카메라 촬영 HUD 및 촬영 상태 구현
- 살인마 공격 / 감금 / 트랩 / 광폭화 구현
- 생존자 목표 UI, 상태 UI, 결과 UI 구현
- 시야 제한 UI, Lighting, SFX 적용
- 감옥 이동 연출 및 탈출 흐름 구현

---

## 실행 방법

프로젝트 실행 전 **멀티플레이 서버를 먼저 실행해야 합니다.**

- LastAttraction_Final_Build.zip 다운로드
- 압축 해제
- LastAttraction.exe 실행
- 로그인 또는 회원가입 진행
- Killer / Survivor 역할 선택 후 Lobby 입장
- Survivor 준비 완료 후 Killer가 게임 시작
