KR Dalamud Updater 0.4.1

사용 방법
1. 배포 ZIP 전체를 쓰기 가능한 일반 폴더에 압축 해제합니다.
2. 게임과 기존 Dalamud Updater를 종료합니다.
3. Dalamud.Updater.exe를 실행합니다.
4. 실행기는 GitHub의 최신 정식 Release를 확인하고 SHA-256 검증 후 설치합니다.
5. GitHub에 연결할 수 없으면 마지막 정상 버전 또는 내장 버전으로 실행합니다.
6. 최신 공식 Dalamud가 필요하면 게임을 종료한 상태에서 Check Update를 누릅니다.

주의
- UpdaterReleaseConfig.json을 Dalamud.Updater.exe 옆에 보관해야 자동 업데이트가 작동합니다.
- Program Files처럼 쓰기 권한이 제한된 폴더는 피하세요.
- 사용자 설정은 %APPDATA%\KrDalamudUpdater\settings.json에 보관됩니다.
- 설치된 버전은 %LOCALAPPDATA%\KrDalamudUpdater\versions 아래에 보관됩니다.
- 게임 패치 직후에는 KR 호환성 검증이 완료되기 전까지 업데이트를 적용하지 마세요.

관리자/문제 해결
- 네트워크 확인 없이 내장 또는 마지막 정상 버전을 실행하려면 --offline 옵션을 사용합니다.
- 진단 로그는 %LOCALAPPDATA%\KrDalamudUpdater\bootstrap.log에 기록됩니다.
