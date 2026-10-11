# Editor frame input verification

실제 `EditorWindow`를 열고 Exposure thumb를 누른 상태에서 60개 MouseMove를 보낸다. 독립 렌더 서비스 테스트와 달리 Slider 이벤트 → 편집 상태/히스토리 → 비동기 float 보정 → Bitmap 교체 → Avalonia Skia 창 프레임을 관찰한다. tone 슬라이더에 안정적인 control name을 추가했고 기존 UI 입력 helpers를 공유한다.

## Checks

테스트가 소유한 1800×1200의 일정한 회색 PNG를 사용한다. 원본 파일과 임시 디렉터리는 종료 후 정리한다.

- 드래그 중 여러 다른 이미지 픽셀이 표시되는지 확인한다. Slider label/손잡이의 변화만을 frame 갱신으로 세지 않는다.
- Image의 Bitmap 참조가 바뀐 뒤 Skia 창 프레임을 캡처한다. 사진 영역 중앙 픽셀과 실제 preview 픽셀을 비교한다. 일정한 회색 입력으로 resize/interpolation 및 RGBA/BGRA 차이에 영향을 받지 않고 사진 표시를 검증한다.
- pointer release 이전 preview가 600×400인지 매번 확인한다.
- release 뒤 1800px 품질 결과의 최종 Exposure를 독립 float 처리 결과와 비교한다.
- 실제 Save 버튼으로 sidecar의 최종 상태를 확인한다.
- 단일 Undo가 드래그 이전 이미지/상태까지 돌아가는지, Redo가 마지막 값/픽셀을 복원하는지 확인한다.

## Observation

macOS ARM64 / Avalonia.Headless + 실제 Skia 렌더링: 60개 마우스 이동 모두 다른 slider 값으로 바뀌었다. 1.285초 동안 서로 다른 사진 영역 프레임 60개, 관찰 갱신률 46.7회/초, 관찰 p95 24.04ms였다.

각 입력 뒤 16ms를 기다린 뒤 캡처하므로 p95에는 polling·UI event 처리·frame capture 비용이 포함된다. 정확한 display latency나 OS 화면 합성 FPS가 아니다. 입력 간 처리된 모든 프레임을 관찰할 수도 없으므로 관찰률을 최대 처리율로 해석하지 않는다. 성능은 환경 의존적이며 테스트는 시간/FPS 기준으로 실패시키지 않는다. 기능 검사는 frame의 이미지 픽셀, 크기, 최종 상태와 히스토리를 기준으로 실패시킨다.

이 결과는 일정한 합성 이미지/Exposure/600px 조작의 헤드리스 범위다. 20MP 이상의 실제 RAW, 여러 보정의 동시 사용, 카메라 WB 연속 현상, Windows/네이티브 OS 입력, GPU·ICC는 별도 검증이 필요하다. TASK-004는 진행 중으로 유지한다.

전체 회귀는 Core 16, Imaging 51, Desktop 206, UI 45, NativeSession 80, CachedLoader 45, NativeUI 42, 총 485개를 통과했다. 실제 RAW 6종의 기존 픽셀 비교 및 source/session 수명 검사를 유지했고 Core/fixture 해시 일치와 앱 5초 시작 (stderr 없음)을 확인했다.

```bash
dotnet run --project tests/PhotoLab.Desktop.Tests -c Release -- --ui
```

[회귀 집계](verification/2026-10-11-editor-frames-results.json) · [입력 및 회귀 로그](verification/2026-10-11-editor-frames-tests.txt) · [빌드](verification/2026-10-11-editor-frames-build.txt) · [시작/해시](verification/2026-10-11-editor-frames-startup.json)
