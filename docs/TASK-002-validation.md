# TASK-002 — 빌드 환경 검증

검증일: 2026-10-10 (Asia/Seoul)  
상태: [~] 제한된 검증 통과, 플랫폼별 앱 빌드·실행 대기

## 변경 내용

`tests/PhotoLab.Core.Tests/Program.cs`의 PGM 스트림 지역 변수 `mono`를 `pgm`으로 변경했다. 앞서 선언된 탈채도 결과 `mono`와 이름이 충돌해 발생하던 CS0136을 해결했다. 테스트 입력과 검증 내용은 유지했다. Core 및 Desktop 소스는 수정하지 않았다.

## 검증 결과

| 항목 | 결과 |
|---|---|
| SDK / 런타임 | .NET SDK 10.0.102 / 런타임 10.0.2, macOS osx-arm64 |
| 프로젝트 구조 | 3개 프로젝트가 net10.0을 대상으로 하며 모든 프로젝트 참조 경로 존재 |
| 패키지 캐시 | Desktop 복원 정보의 패키지 23개가 로컬 캐시에 존재 |
| NuGet 복원 | 솔루션의 3개 프로젝트를 로컬 캐시로 복원, 종료 코드 0 |
| Core 테스트 | Release 구성 테스트 프로젝트 컴파일 및 스모크 테스트 16개 통과, 종료 코드 0 |
| macOS 스크립트 | `bash -n run_mac.sh` 통과 |
| Windows 스크립트 | 경로와 오류 처리 소스 검토만 수행, Windows 실행 미검증 |
| CI | Windows/macOS restore → build → Core 테스트 정의 확인, CI 실행 결과 미확인 |
| Desktop / 전체 빌드 | 사용자 지침에 따라 미수행 |
| 앱 실행 / RAW 실파일 | 미수행. `/opt/homebrew/bin/dcraw_emu` 파일 존재만 확인 |
| Windows / macOS Intel | 해당 환경에서 빌드·실행 미검증 |

Core 테스트 실행 명령:

```sh
dotnet run --project tests/PhotoLab.Core.Tests/PhotoLab.Core.Tests.csproj -c Release --no-restore
```

복원은 빈 임시 로컬 소스를 `--source`로 지정하고 기존 전역 패키지 캐시를 사용했다. `-p:NuGetAudit=false --disable-parallel -m:1` 및 `MSBUILDDISABLENODEREUSE=1`을 적용했다. 일반 복원 시도는 출력 없이 지연되어 취소했고, 단일 MSBuild 노드로 재시도한 복원은 성공했다. NuGet 네트워크 접근, 취약점 감사, 캐시가 없는 새 환경의 복원은 검증하지 않았다. 복원 과정에 `CSSM_ModuleLoad()` 메시지가 있었으나 복원은 종료 코드 0으로 끝났다.

## 남은 검증과 다음 작업

TASK-002 완료에는 Windows/macOS 앱 빌드와 실행 성공이 필요하다. 현재 Core 테스트 통과만으로 Desktop 컴파일이나 이미지 편집 기능의 정상 실행을 보장할 수 없으므로 `[~]`를 유지한다. 전체 프로젝트 빌드는 기존 사용자 지침상 별도 명시적 요청이 필요하다.

허용된 범위의 검증은 수행했다. 다음 단계는 플랫폼별 앱 빌드·실행이며, 그 이후 TASK-003의 JPEG/PNG/RAW 로딩, 편집, Undo/Redo, 내보내기를 검증한다. TASK-001에서 발견한 원본 덮어쓰기와 연속 슬라이더 렌더링 문제는 아직 수정하지 않았다.
