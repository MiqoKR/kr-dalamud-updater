# KR Dalamud Updater 0.5.0 프로필 안전 정책

0.5.0은 `0.5.0-test.2`에서 검증한 최초 설치 기능을 정식 GitHub 연동 업데이터에 통합한다.

## 프로필 상태

- `Missing`: 프로필 폴더가 없음
- `EmptyUnmanaged`: 프로필 폴더가 비어 있음
- `ExistingUnmanaged`: 기존 파일이 있으나 업데이터 소유 표식이 없음
- `OwnedIncomplete`: 업데이터가 만든 프로필이지만 최소 항목이 일부 없음
- `OwnedReady`: 업데이터가 만든 최소 프로필이 준비됨

`Missing`과 `EmptyUnmanaged`만 새 프로필로 초기화한다. `ExistingUnmanaged`는 기존 사용자 프로필로 간주하며 초기화하거나 소유권을 주장하지 않는다.

## 최초 설치 시 생성하는 항목

- `addon/Hooks`
- `dalamudAssets`
- `installedPlugins`
- `devPlugins`
- 파일이 없을 때만 `dalamudConfig.json` (`{}`)
- `KR.Dalamud.Updater.Profile.json`

## 기존 프로필에서 보존하는 항목

- `dalamudConfig.json` 전체 내용
- `installedPlugins`
- `devPlugins`
- 플러그인 설정과 커스텀 저장소 설정
- 그 밖의 XIVLauncherKR 사용자 파일

정식 업데이트가 직접 변경하는 프로필 범위는 대상 버전의 `addon/Hooks/<버전>`, `dalamudAssets/<버전>`, `dalamudAssets/asset.ver`, `kr-dalamud-backups`뿐이다.

## Hook/Assets 교체 절차

1. 시스템 임시 폴더에 공식 패키지를 다운로드하고 압축을 해제한다.
2. 공식 해시와 KR 호환 패치를 검증한다.
3. 프로필의 대상 상위 폴더에 `.krdu-staging-*` 복사본을 만든다.
4. 스테이징 복사본을 다시 검증한다.
5. 같은 버전의 기존 대상이 있으면 `kr-dalamud-backups`로 이동한다.
6. 스테이징 폴더를 대상 버전으로 전환하고 다시 검증한다.
7. 어느 단계에서든 실패하면 기존 대상을 자동 복구한다.

다른 버전의 Hook과 Assets는 삭제하지 않는다. Assets의 `asset.ver`와 업데이터 설정은 같은 폴더의 임시 파일을 거쳐 교체하며 이전 파일을 백업한다.

## 수동 복구

게임이 종료된 상태에서 화면의 `마지막 업데이트 복구` 또는 트레이 메뉴의 같은 항목을 선택한다. 마지막 성공 업데이트 직전의 Hook과 Assets 버전이 모두 남아 있을 때 포인터만 이전 버전으로 되돌린다. 설정과 플러그인 파일은 변경하지 않는다.

최초 설치는 이전 Hook/Assets가 없으므로 수동 복구 대상이 없을 수 있다.

## 런타임

배포 ZIP에는 .NET을 포함하지 않는다. 시스템에 설치된 Microsoft .NET 10 Desktop Runtime x64의 `dotnet.exe`로 `Dalamud.Injector.dll`을 실행한다.

## 검증 시나리오

- 없는 프로필의 최소 초기화
- 기존 비관리 프로필의 설정·플러그인 보존
- 업데이터 소유 불완전 프로필의 비파괴 복구
- 최종 검증 실패 시 기존 Hook/Assets 자동 원복
- 정상 교체 중 무관한 플러그인 파일 보존
- 원자적 파일 교체와 백업 생성

검증 명령:

```powershell
dotnet run --project launcher/KrDalamudUpdaterSafetyTests/KrDalamudUpdaterSafetyTests.csproj -c Release
```
