# KR Dalamud Updater 0.5.6 검증 기록

## 기준

- 확인일: 2026-11-11 (KST)
- 공식 채널: Stable (`release`, `Control`)
- Dalamud: `15.0.4.0`
- 지원 게임 버전: `2026.11.03.0000.0000`
- 필수 런타임: `.NET 10.0.14`
- Assets: `448`
- 공식 Hook ZIP SHA-256: `E94D993BF91C8A43CE0959942602EBD18C831B788A133700150CEB928283129F`
- 공식 FFXIVClientStructs: `7.57.0.9480`, source `80cb4a7b7e11a6702e2eb8839afd301a918c638e`
- 공식 FFXIVClientStructs SHA-256: `28488F18AD05FDCD12373A83756EAB275C291B09C6AF061EF7F846AA06A36E29`

## KR 호환 ClientStructs

공식 소스 커밋 `80cb4a7b`을 기준으로 한섭 실행 파일을 다시 검사했다. 기존 KR UI
레이아웃 보정과 `AtkResNode.IsVisible` 패치를 유지하고, 7.57 신규 항목 중 한섭에서
중복되던 전투 거리·Beastmaster 펫 목록 시그니처 및 초상화 머리 방향 시그니처를
한섭 코드에 맞게 좁혔다.

- 한섭 게임 실행 파일: `2026.11.03.0000.0000`
- 한섭 실행 파일 SHA-256: `A416573B4FF07C6ECD02AE639346AC534B266D3CCF4370E84A1CAAD60C385ED6`
- 한섭 호환 ClientStructs SHA-256: `7D3B7E7499B525AC5B23282188782ADDBACFEF91700B78B3A95BD229D56B11ED`
- 재현용 소스 패치: `docs/clientstructs-7.57-kr-2026.11.03.patch`

## 오프라인 검증

- 전체 주소 2,654개 중 2,648개 해석
- Dalamud 본체 ClientStructs 형식·멤버 참조 오류: 0개
- 검증 가능한 `UIModule` getter 73개 전부 필드 오프셋 일치
- 해석된 주소 중 실행 파일 이미지 범위를 벗어난 주소: 0개
- KR KeyState, KeyState 인덱스, `AtkResNode.IsVisible`: 각각 단일 일치
- 남은 중복 시그니처 4개는 각 후보가 동일 함수 주소를 가리킴

미해결 6개:

- `ContentsFinderQueueInfo.QueueRoulette`
- `CharaSelectCharacterEntry.IsInDifferentRegion`
- `RaptureTextModule.SetGlobalTempEntity1`
- `AtkFontManager.GameFontTable`
- `AtkFontManager.LobbyFontTableStandardResolution`
- `AtkFontManager.LobbyFontTableHighResolution`

이 항목들은 이전 한섭 검증에서도 남아 있던 글로벌 전용 또는 네이티브 구조 차이
항목이다. 주소를 추측해 넣지 않는다. 정적 검증 결과는 개별 플러그인의 모든 네이티브
호출까지 보장하지 않으므로 실제 게임 실행 확인을 별도로 수행한다.

## 배포 원칙

기존 `%APPDATA%\XIVLauncherKR` 설정과 플러그인은 덮어쓰지 않는다. Hook은
`15.0.4.0` 버전 폴더에 설치하고 Assets `448`을 사용한다. 게임 실행 중에는
Hook/Assets를 교체하지 않으며, 스테이징 검증 실패 시 기존 버전으로 복구한다.

공식 Hook은 `.NET 10.0.14` 이상을 요구한다. 업데이터는 설치된
`Microsoft.NETCore.App`이 이보다 낮으면 Hook 설치를 중단하고 Desktop Runtime x64
업데이트를 안내한다.
