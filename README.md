# KR Dalamud Updater

한국 서버용 Dalamud 실행기와 호환성 패치를 관리하는 개인 배포 프로젝트입니다.

## 배포 흐름

1. `updater-v0.4.0` 형식의 Git 태그를 만듭니다.
2. GitHub Actions가 Windows 배포 파일을 자동으로 빌드합니다.
3. GitHub Releases에 최초 설치 ZIP과 업데이트 payload ZIP이 게시됩니다.
4. 사용자의 `Dalamud.Updater.exe`가 최신 Release를 확인합니다.
5. 다운로드한 ZIP의 GitHub SHA-256 digest를 확인한 후 버전별 폴더에 설치합니다.
6. 네트워크 또는 업데이트 오류가 발생하면 마지막 정상 버전이나 내장 버전으로 실행합니다.

## 사용자 요구 사항

- Windows x64
- Microsoft .NET 10 Desktop Runtime x64

배포 파일에는 .NET 런타임을 포함하지 않습니다. 런타임이 없으면 부트스트랩이 Microsoft 다운로드 페이지를 안내합니다.

## 현재 상태

- GitHub Release 기반 업데이트 부트스트랩
- SHA-256 검증과 ZIP 경로 이탈 방지
- 실패 시 마지막 정상 버전/내장 버전 fallback
- 태그 기반 GitHub Actions 자동 Release

GitHub를 처음 설정하는 절차는 [docs/GITHUB_SETUP_KR.md](docs/GITHUB_SETUP_KR.md)를 참고하세요.

## 주의

백업, 로그, 게임 런타임, 크래시 덤프, 다운로드한 외부 바이너리는 저장소에 올리지 않습니다. 외부 프로젝트의 코드나 바이너리를 배포하기 전에는 해당 라이선스와 재배포 조건을 별도로 확인해야 합니다.
