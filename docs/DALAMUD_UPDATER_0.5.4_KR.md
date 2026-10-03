# KR Dalamud Updater 0.5.4 검증 기록

## 기준

- 확인일: 2026-10-03 (KST)
- 공식 채널: Stable (`release`, `Control`)
- Dalamud: `15.0.3.6`
- 지원 게임 버전: `2026.09.15.0000.0000`
- 런타임: `.NET 10.0.0`
- Assets: `443`
- 공식 Hook SHA-256: `560D283B63D5D70DD5FA7EEB79E7A9CB5139B3DCC63E0CB01B466A57B020FCAA`
- 공식 FFXIVClientStructs: `7.56.2.9136`, source `313161e448e335adddd928f8a0212b2c328b5659`

## KR 호환 ClientStructs

공식 소스 커밋 `313161e4`을 기준으로 기존 7.56 KR 검증 차이를 다시 반영했다.

- 한국 클라이언트 전용 UI 구조 오프셋
- `AtkResNode.IsVisible` 단일 시그니처
- `Conditions.HasPermission` 단일 시그니처
- `AtkComponentDropDownList` 가상 테이블 변위

빌드 결과:

- 파일 및 어셈블리 버전: `7.56.2.9136`
- SHA-256: `DB706E92C08AB556F246C9248F9BD175CFFF86393445264DF9B5BB14D554942F`

## 오프라인 검증

- 한섭 `ffxiv_dx11.exe` SHA-256: `FDA9AA7228B88D729F3F4F77B76AA14A5F7B78979354F4B4E914153C7902780A`
- 주소 2,417개 중 2,414개 해석
- 핵심 검증 주소 4개 모두 단일 일치
- Dalamud 본체 ClientStructs 참조 오류: 0개
- 미해결 3개:
  - `ContentsFinderQueueInfo.QueueRoulette`
  - `CharaSelectCharacterEntry.IsInDifferentRegion`
  - `RaptureTextModule.SetGlobalTempEntity1`

미해결 3개는 기존 한섭 검증에서도 확인된 비핵심 글로벌 전용 패턴이다. 핵심 로더,
UI 주소 및 Dalamud 본체 참조 검증에는 영향을 주지 않는다.

## 배포 원칙

기존 `%APPDATA%\XIVLauncherKR` 설정과 플러그인을 보존한다. Hook은
`15.0.3.6` 버전 폴더에 새로 설치하고 Assets `443`은 동일 버전을 유지한다.
실제 프로필 반영 전 격리 Hook 패치와 반복 적용 검증을 통과해야 한다.
