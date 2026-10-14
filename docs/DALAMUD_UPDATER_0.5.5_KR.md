# KR Dalamud Updater 0.5.5 검증 기록

## 기준

- 확인일: 2026-10-14 (KST)
- 공식 채널: Stable (`release`, `Control`)
- Dalamud: `15.0.3.7`
- 지원 게임 버전: `2026.10.06.0000.0000`
- 런타임: `.NET 10.0.0`
- Assets: `445`
- 공식 Hook ZIP SHA-256: `82D3EFAD4615D4B40FDABD458132473D6B69E341CD700293A6BB3FF2B5B77547`
- 공식 FFXIVClientStructs: `7.57.0.9378`, source `78975d8306c2766250ee4dca1925ef81f5dfd4f7`
- 공식 FFXIVClientStructs SHA-256: `A47750CE4BE1910C8783231F398559BCCE43E4A6AE8D255351B3BBA8247D6F9D`

## KR 호환 ClientStructs

공식 소스 커밋 `78975d83`을 기준으로 한섭 실행 파일을 다시 검사했다. 기존 KR
UI 레이아웃 차이 외에 7.57에서 확인된 `TextService`, `AtkTextInput`,
`SteamGamepadSoftKeyboard`, UI 장면 문자열 및 화면 상태 필드 위치를 반영했다.

- 한섭 게임 실행 파일: `2026.10.06.0000.0000`
- 한섭 실행 파일 SHA-256: `DB9824EFAC6D5370AB64B02353E882931DDA447E160EE1A5D9E5DCBA43695728`
- 한섭 호환 ClientStructs SHA-256: `717FEEEE0F4708E3F45913526AE1D727E046BD1CA8D73241BA9EC7E4B34A21B5`
- 재현용 소스 패치: `docs/clientstructs-7.57-kr.patch`

## 오프라인 검증

- 전체 주소 2,571개 중 2,565개 해석
- Dalamud 본체 ClientStructs 형식·멤버 참조 오류: 0개
- 검증 가능한 `UIModule` getter 73개 전부 필드 오프셋 일치
- 해석된 주소 중 실행 파일 이미지 범위를 벗어난 주소: 0개
- KR KeyState, KeyState 인덱스, `AtkResNode.IsVisible`: 각각 단일 일치
- 중복 시그니처 4개는 각 후보가 동일 함수 주소를 가리킴

미해결 6개:

- `ContentsFinderQueueInfo.QueueRoulette`
- `CharaSelectCharacterEntry.IsInDifferentRegion`
- `RaptureTextModule.SetGlobalTempEntity1`
- `AtkFontManager.GameFontTable`
- `AtkFontManager.LobbyFontTableStandardResolution`
- `AtkFontManager.LobbyFontTableHighResolution`

앞의 3개는 이전 한섭 검증에서도 남아 있던 글로벌 전용 패턴이다. 새 글꼴 테이블
3개는 글로벌과 한섭의 네이티브 테이블 구조가 달라 주소를 추측해 넣지 않았다.
공식 Dalamud 본체가 새 글꼴 테이블 3개를 직접 참조하지 않는 것은 별도로 확인했다.
이 결과는 정적 검증이며 개별 플러그인의 모든 네이티브 호출까지 보장하지 않는다.

## 패키지 검증

- 공식 Hook 격리 복사본에 KR 호환 패치 적용: 통과
- 동일 Hook에 반복 적용: 결과 해시 유지
- 패치 후 `hashes.json`의 `Dalamud.dll`/`FFXIVClientStructs.dll`: 일치
- 지원하지 않는 게임 버전 차단: 통과
- 변조된 공식 ClientStructs 차단 및 원본 보존: 통과
- 기존 프로필 설정·플러그인 보존 테스트: 통과
- Hook/Assets 교체 실패 시 롤백 테스트: 통과

## 배포 원칙

기존 `%APPDATA%\XIVLauncherKR` 설정과 플러그인은 덮어쓰지 않는다. Hook은
`15.0.3.7` 버전 폴더에 설치하며 Assets는 공식 최신 `445`를 사용한다. 게임 실행
중에는 Hook/Assets를 교체하지 않으며, 스테이징 검증에 실패하면 기존 버전으로
돌아간다. 실제 게임 내 플러그인 동작은 릴리스 설치 후 최종 확인이 필요하다.
