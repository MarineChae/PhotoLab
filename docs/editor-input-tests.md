# TASK-003/011 — 편집기 입력 회귀와 비트맵 수명 수정

검증일: 2026-10-11. 전체 전문 RAW 편집기 목표는 진행 중이다.

## 발견한 오류와 수정

WB 적용→저장→재개→As shot 마우스 입력에서 Avalonia Image.Render가 해제된 Bitmap의 Size를 읽어 NullReferenceException을 발생시켰다. 다음 미리보기를 기다리는 동안 Image.Source가 이전 after bitmap을 계속 참조했는데, 사진 재로딩 코드가 이를 먼저 Dispose했기 때문이다.

새 before bitmap을 할당하고 Image.Source를 동기적으로 교체한 뒤 이전 before/after를 해제하도록 순서를 바꿨다. 일반 미리보기 교체에서도 참조를 바꾼 뒤 이전 bitmap을 해제하고, 창을 닫을 때 Image.Source를 null로 분리한다. Core 소스나 색 보정 수식은 변경하지 않았다.

## 테스트 구성

Desktop 테스트 프로젝트에만 Avalonia.Headless 11.3.22를 추가했다. 앱의 Avalonia와 같은 버전이고 새 앱 배포 의존성은 없다. [공식 라이선스](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/11.3.22/licence.md)는 MIT다. 기존 Skia와 함께 UseHeadlessDrawing=false로 실제 bitmap/프레임 렌더링을 사용한다. [공식 입력·프레임 API](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/11.3.22/src/Headless/Avalonia.Headless/HeadlessWindowExtensions.cs), [UI session API](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/11.3.22/src/Headless/Avalonia.Headless/HeadlessUnitTestSession.cs)를 기준으로 구현했다.

EditorWindow에 PhotoImageLoader를 주입하는 생성자와 파일 선택기 없이 로컬 사진을 여는 OpenPhotoAsync를 추가했다. 기본 생성자와 파일 선택기는 기존 동작을 유지한다. 제어에는 안정적인 Name을 부여해 테스트가 reflection이나 private handler 호출 없이 논리 트리에서 컨트롤을 찾는다. UI thread에서 KeyTextInput과 MouseDown/MouseUp을 보내고 실제 화면 Bitmap의 픽셀을 비교한다.

디코더는 작은 synthetic UI fixture로 대체했다. 이것을 실제 RAW 파일이나 네이티브 디코딩 검증으로 주장하지 않는다. 실제 카메라 6종의 기존 테스트는 별도로 유지한다. OS 파일 선택기·물리 입력 장치·네이티브 창 표시까지 검증하지 않는다.

테스트 하네스에서는 headless Dispatch 완료가 session worker에서 continuation을 실행할 수 있으므로 Dispose를 다른 worker에 보내고 await한다. Dispose가 자신이 실행 중인 session worker 종료를 기다리는 대기를 피한다. 이는 테스트 수명 관리이며 앱 런타임 변경은 아니다.

## 검증

[빌드](verification/2026-10-11-ui-build.txt), [로그](verification/2026-10-11-ui-tests.txt), [집계](verification/2026-10-11-ui-results.json): Core 16 + Imaging 48 + Desktop 206 + UI 17 = 287개 검사 통과. Release 오류 0, 기존 CS0618 경고 2개 유지.

헤드리스 입력으로 확인한 범위:

- WB 입력 활성화, NaN 거부, Apply의 현상/화면 변경, 입력값 복원, 변경 없는 Apply의 비변경성.
- Original의 카메라 기본 픽셀, Save의 전체 WB 파일, 재개의 입력/히스토리 초기화.
- As shot, Undo/Redo의 픽셀·입력값·히스토리 복원.
- 현상 실패 시 이전 화면·히스토리 보존, 실패 후 Undo 대상 유지.
- JPEG/PNG의 RAW 블록 숨김, metadata 누락 시 새 WB 금지와 기존 WB 초기화 허용.
- 작업 중 창 닫힘 및 늦게 반환된 비협조적 디코더의 픽셀 해제.
- Skia의 전체 editor frame 생성. [렌더링한 화면](verification/2026-10-11-headless-editor.png)은 synthetic 2×2 fixture이며 실제 사진 품질 검증과 구분한다.

실제 RAW 6종의 WB/독립 reference·저장/복원·전체 해상도 내보내기·원본 해시 회귀도 통과했다. [앱 시작/해시 기록](verification/2026-10-11-ui-startup.json): native 앱 5초 정상 시작, stdout/stderr 없음, Core 8개와 fixture 7개 해시 일치.

Windows/macOS CI 정의에 --ui 실행을 추가했지만 여기서 원격 CI나 Windows 실행 성공을 확인한 것은 아니다.

## 실행과 남은 작업

```bash
dotnet run --project tests/PhotoLab.Desktop.Tests/PhotoLab.Desktop.Tests.csproj -c Release -- --ui
```

카메라 데이터 캐시/WB 실시간 미리보기, tone slider의 드래그 입력·FPS, 네이티브 OS 파일 선택기/창/입력, Windows/macOS Intel, 중립점/Kelvin, ICC/색 정확도와 GPU는 남아 있다. 헤드리스 17개 검사만으로 전체 GUI·플랫폼 검증을 완료 표시하지 않는다.
