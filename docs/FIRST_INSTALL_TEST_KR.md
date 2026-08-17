# KR Dalamud 최초 설치 테스트판

정식 업데이터와 분리된 프로필에서 빈 프로필 초기화, 공식 Hook/Assets 업데이트, 시스템 .NET 10 기반 인젝터 실행을 검증하는 배포판이다.

- 실행 파일: `KR.Dalamud.FirstInstall.Test.exe`
- 기본 프로필: `%APPDATA%\XIVLauncherKR-FirstInstallTest`
- 메인 프로필: `%APPDATA%\XIVLauncherKR` (사용 금지 보호 적용)
- 플러그인 및 커스텀 저장소: 기본 비활성

빌드:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Build-KrDalamudFirstInstallTest.ps1
```

테스트판 설정에는 `RequireIsolatedProfile=true`가 들어가므로 메인 프로필 경로를 지정하면 실행을 중단한다. 정식판은 배포 ZIP의 .NET 런타임을 포함하지 않으며 테스트판과 동일하게 시스템 Microsoft .NET 10 Desktop Runtime x64를 사용한다.
