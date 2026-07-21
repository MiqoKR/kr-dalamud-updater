# GitHub 처음 설정하기

이 문서는 `kr-dalamud-updater` 저장소를 처음 만드는 관리자를 위한 절차입니다.

## 1. 저장소 만들기

1. GitHub에 로그인합니다.
2. 새 저장소 이름을 `kr-dalamud-updater`로 지정합니다.
3. 처음에는 **Private**으로 생성해 파일 목록을 점검합니다.
4. GitHub에서 README, `.gitignore`, 라이선스를 자동 생성하지 않습니다. 로컬 프로젝트에 이미 준비되어 있습니다.

## 2. 공개 전 점검

다음 폴더와 파일은 GitHub에 올라가면 안 됩니다.

- `backups`, `known-good`, `legacy-working`, `downloads`, `temp`
- `*-lab` 실험 폴더와 크래시 보고서
- `bin`, `obj`, `artifacts`, `dist`
- 개인 설정, 로그, 덤프 파일
- 재배포 허가를 확인하지 않은 외부 DLL과 ZIP

최초 업로드 후 GitHub의 파일 목록에서 위 항목이 없는지 확인한 다음 공개 전환합니다. 일반 사용자에게 인증 없이 자동 업데이트를 제공하려면 Release가 있는 저장소가 Public이어야 합니다.

## 3. 버전 배포하기

배포 태그 규칙은 `updater-v<버전>`입니다.

예시:

```text
updater-v0.4.0
updater-v0.4.1
```

태그를 GitHub에 올리면 `.github/workflows/release-updater.yml`이 다음 파일을 자동 생성합니다.

- `KR-Dalamud-Updater-<버전>-Portable.zip`: 최초 사용자용
- `KR-Dalamud-Updater-Payload.zip`: 기존 설치자의 자동 업데이트용

GitHub Release가 완료될 때까지 업데이터는 해당 버전을 받지 않습니다.

## 4. 사용자 배포

신규 사용자에게는 Portable ZIP만 전달합니다. 사용자는 ZIP을 일반 폴더에 풀고 `Dalamud.Updater.exe`를 실행합니다. 이후 버전부터는 실행기가 GitHub 최신 Release를 확인합니다.

## 5. 보안 원칙

- GitHub 비밀번호나 Personal Access Token을 프로그램에 넣지 않습니다.
- 자동 업데이트는 공개 Release만 사용합니다.
- Release asset의 SHA-256 digest가 없거나 일치하지 않으면 설치하지 않습니다.
- 문제가 있는 Release는 삭제하기보다 새 수정 버전을 배포합니다.
