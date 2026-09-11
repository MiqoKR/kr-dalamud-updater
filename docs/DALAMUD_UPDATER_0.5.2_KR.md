# KR Dalamud Updater 0.5.2 검증 기록

## 기준

- 확인일: 2026-09-11 (KST)
- 공식 채널: Stable (`release`, `Control`)
- Dalamud: `15.0.3.4`
- 지원 게임 버전: `2026.09.01.0000.0000`
- 런타임: `.NET 10.0.0`
- Assets: `440`
- 공식 Hook SHA-256: `2343AB848DEF4F749A8B7ECF9A139E82F38801A8F631F6B55CAF748C768AAB4C`
- 공식 FFXIVClientStructs: `7.55.1.9047`, source `694dbbf6c0bda544d18a8e2a7431e799c3662c16`

## KR 호환 ClientStructs

공식 소스 커밋 `694dbbf6`을 기준으로 기존 7.56 KR 검증 차이만 다시 반영했다.

- 한국 클라이언트 전용 UI 구조 오프셋
- `AtkResNode.IsVisible` 단일 시그니처
- `Conditions.HasPermission` 단일 시그니처
- `AtkComponentDropDownList` 가상 테이블 변위

빌드 결과:

- 파일 버전: `7.55.1.9047`
- SHA-256: `ABA32BA82042A536CD15F3780A8C8DAF560C6777BDA43B4FE32B36DAE7DF1804`

## 오프라인 검증

- 한섭 `ffxiv_dx11.exe` SHA-256: `914D05C55149A42FD1A34E63C8878AD3A1C27FB738F6711ABF523B2A695DA829`
- 주소 2,390개 중 2,387개 해석
- 핵심 검증 주소 4개 모두 단일 일치
- Dalamud 본체 ClientStructs 참조 오류: 0개
- 미해결 3개는 0.5.1과 동일:
  - `ContentsFinderQueueInfo.QueueRoulette`
  - `CharaSelectCharacterEntry.IsInDifferentRegion`
  - `RaptureTextModule.SetGlobalTempEntity1`

## 배포 원칙

기존 `%APPDATA%\XIVLauncherKR` 설정과 플러그인을 보존한다. Hook은
`15.0.3.4` 버전 폴더에 새로 설치하고 Assets `440`은 이미 검증된 동일 버전을
재사용한다. 실제 프로필 반영 전 격리 Hook 패치와 반복 적용 검증을 통과해야 한다.
