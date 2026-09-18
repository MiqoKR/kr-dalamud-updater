# KR Dalamud Updater 0.5.3 검증 기록

## 기준

- 확인일: 2026-09-18 (KST)
- 공식 채널: Stable (`release`, `Control`)
- Dalamud: `15.0.3.5`
- 지원 게임 버전: `2026.09.15.0000.0000`
- 런타임: `.NET 10.0.0`
- Assets: `443`
- 공식 Hook SHA-256: `A6C716B81DAEB4FC348B076B63FE1CB584A731C781DBDAF3E6C58C1FBC33A827`
- 공식 FFXIVClientStructs: `7.56.2.9089`, source `b53cdf38b532a2ffdb711f13d9501db046bf7d0d`

## KR 호환 ClientStructs

공식 소스 커밋 `b53cdf38`을 기준으로 기존 7.56 KR 검증 차이만 다시 반영했다.

- 한국 클라이언트 전용 UI 구조 오프셋
- `AtkResNode.IsVisible` 단일 시그니처
- `Conditions.HasPermission` 단일 시그니처
- `AtkComponentDropDownList` 가상 테이블 변위

빌드 결과:

- 파일 및 어셈블리 버전: `7.56.2.9089`
- SHA-256: `E8EE688C069D5B95174EC030735A7AB9EBB86A6FB55A6BD9BDD5DA67AF2832A9`

## 오프라인 검증

- 한섭 `ffxiv_dx11.exe` SHA-256: `914D05C55149A42FD1A34E63C8878AD3A1C27FB738F6711ABF523B2A695DA829`
- 주소 2,404개 중 2,399개 해석
- 핵심 검증 주소 4개 모두 단일 일치
- Dalamud 본체 ClientStructs 참조 오류: 0개
- 미해결 5개:
  - `ContentsFinderQueueInfo.QueueRoulette`
  - `PacketDispatcher.HandleInventoryItemPacket`
  - `PacketDispatcher.HandleMapEffectPacket`
  - `CharaSelectCharacterEntry.IsInDifferentRegion`
  - `RaptureTextModule.SetGlobalTempEntity1`

네트워크 패킷 주소 2개는 공식 ClientStructs 7.56.2에서 새로 추가됐지만 현재 한섭
실행 파일에는 해당 글로벌 패턴이 없다. 핵심 로더 및 UI 주소와 Dalamud 본체 참조
검증에는 영향을 주지 않는다.

## 배포 원칙

기존 `%APPDATA%\XIVLauncherKR` 설정과 플러그인을 보존한다. Hook은
`15.0.3.5` 버전 폴더에 새로 설치하고 Assets `443`을 원자적으로 교체한다.
실제 프로필 반영 전 격리 Hook 패치와 반복 적용 검증을 통과해야 한다.
