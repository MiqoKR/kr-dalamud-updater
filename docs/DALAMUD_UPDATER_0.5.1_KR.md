# KR Dalamud Updater 0.5.1 검증 기록

## 기준

- 확인일: 2026-09-09 (KST)
- 공식 채널: Stable (`release`, `Control`)
- Dalamud: `15.0.3.3`
- 지원 게임 버전: `2026.09.01.0000.0000`
- 런타임: `.NET 10.0.0`
- Assets: `440`
- 공식 패키지 SHA-256: `C947E8965C2EEA491F657D2A21A4ED61DA6CC0913A68D4804A744DBA391C6661`
- 공식 FFXIVClientStructs: `7.55.1.9032`, source `21898bf815f0e56e02b7dc08f0a3e24822c759d0`

## 적용 방침

공식 Stable Hook을 원본으로 사용하고, 한국 클라이언트에서 확인된 차이만 패치한다.
전체 `%APPDATA%\XIVLauncherKR` 프로필은 교체하지 않으며 Hook과 Assets만 별도 버전
폴더에 설치한다. 기존 설정과 `installedPlugins`는 유지한다.

KR ClientStructs는 공식 `21898bf` 소스에 다음 차이만 반영했다.

- 한국 클라이언트 전용 UI 구조 오프셋
- `AtkResNode.IsVisible` 단일 시그니처
- `Conditions.HasPermission` 단일 시그니처
- `AtkComponentDropDownList` 가상 테이블 변위

## 검증 결과

- 한섭 `ffxiv_dx11.exe` SHA-256: `914D05C55149A42FD1A34E63C8878AD3A1C27FB738F6711ABF523B2A695DA829`
- 주소 2,386개 중 2,383개 해석
- 핵심 검증 주소 4개 모두 단일 일치
- Dalamud 본체 ClientStructs 참조 오류: 0개
- 공식 Hook의 KR 언어 패치 및 7.56 KeyState 패치: 통과
- `hashes.json`의 Dalamud, Dalamud.Common, FFXIVClientStructs MD5: 모두 일치
- 동일 Hook에 재적용: 통과
- 프로필 보존·교체 실패 롤백·설정 백업 테스트: 통과

오프라인에서 해석되지 않은 3개 주소는 아래와 같다. 실제 게임 호출 여부는 별도
실행 검증 대상으로 남긴다.

- `ContentsFinderQueueInfo.QueueRoulette`
- `CharaSelectCharacterEntry.IsInDifferentRegion`
- `RaptureTextModule.SetGlobalTempEntity1`

## 배포 전 확인

실제 게임에서 로그인, UI 플러그인, 커스텀 저장소 플러그인과 마수사 관련 UI를
확인한 뒤 태그 `updater-v0.5.1`을 생성한다.
