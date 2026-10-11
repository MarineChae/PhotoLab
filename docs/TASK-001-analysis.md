# TASK-001 — 프로젝트 구조 분석

분석일: 2026-10-10 (Asia/Seoul)  
기준 문서: `Agent.md`, `Spec.md`, `Task.md`  
상태: 소스 분석 완료, 전체 빌드·런타임·플랫폼 검증 대기

## 범위와 판단 기준

`Task.md` 12절에 지정된 TASK-001을 수행했다. 솔루션, 모든 직접 작성한 C# 소스, 프로젝트 참조, 테스트, 실행 스크립트, CI 정의를 확인했다. 기능의 소스 존재 여부와 실제 실행 검증을 구분한다. 대규모 리팩터링이나 Core 파일 수정은 수행하지 않았다.

문서 내 `SPEC.md`, `AGENTS.md`, `TASKS.md` 참조는 이 작업에서 사용자가 지정한 실제 파일 `Spec.md`, `Agent.md`, `Task.md`에 대응시켰다.

## 구조와 의존성

```text
PhotoLab.slnx
├── src/PhotoLab.Desktop → PhotoLab.Core
│   ├── Program / App: Avalonia 앱 시작 및 테마
│   ├── EditorWindow: 화면, 이벤트, 편집 상태, 히스토리, 렌더링 요청
│   ├── PhotoImageLoader: 일반 이미지/RAW 로딩 경로 선택
│   ├── SkiaImageCodec: JPEG/PNG 읽기·쓰기 및 미리보기 축소
│   └── LibRawCommandDecoder: 외부 dcraw_emu 실행
├── src/PhotoLab.Core (외부 패키지 참조 없음)
│   ├── PixelBuffer / EditSettings / IImageProcessor
│   ├── CpuImageProcessor / Histogram
│   └── PhotoFormats / PpmImageCodec / SidecarStore
└── tests/PhotoLab.Core.Tests → PhotoLab.Core
    └── Program: 콘솔 스모크 테스트 러너
```

세 프로젝트 모두 `net10.0`, nullable 및 implicit usings를 사용한다. Desktop의 AssemblyName은 `PhotoLab`이다. Desktop 패키지는 Avalonia.Desktop / Avalonia.Themes.Fluent 11.3.22, SkiaSharp / SkiaSharp.NativeAssets.Win32 / SkiaSharp.NativeAssets.macOS 3.119.4다. LibRaw는 NuGet/PInvoke 통합이 아니라 별도 설치한 실행 파일이다.

Core는 UI 프레임워크에 의존하지 않는다. 그러나 이미지 알고리즘, 디코딩 파서, 파일 저장도 Core에 포함되어 있어 `Spec.md` 3절의 Imaging / Rendering / Infrastructure 책임 분리와 다르다. 이 프로젝트들은 현재 존재하지 않는다.

`EditorWindow`에는 ViewModel이나 DataContext 바인딩이 없다. UI 컨트롤 생성과 상태·히스토리·비동기 처리 조율을 함께 담당한다. 알고리즘 자체는 `CpuImageProcessor`로 분리되어 있으나, Window가 구체적인 CPU 구현을 직접 생성하므로 백엔드 교체 시 UI 수정이 필요하다.

## 실제 데이터 흐름

- 일반 이미지: Skia 디코딩 → 최장변 1800px 이하 미리보기 → RGBA8 `PixelBuffer`.
- RAW: `dcraw_emu -w -o 1 -6 -Z`로 카메라 WB/sRGB 16비트 PPM 요청 → `PpmImageCodec`에서 RGBA8로 양자화 → Skia 미리보기 축소. 디코더 출력 정밀도는 실제 RAW 파일로 검증하지 않았다.
- 보정: sRGB 디코딩 LUT → float 중간 연산으로 노출/WB → 하이라이트·그림자 → 대비 → 채도·생동감 → sRGB RGBA8 출력. float 중간 연산은 고정밀 입력 버퍼 지원을 의미하지 않는다.
- 미리보기: 캐시한 원본 미리보기와 보정값 스냅샷 → `Task.Run`에서 CPU 처리 및 PNG 인코딩 → UI에서 Bitmap 디코딩·교체.
- 내보내기: 원본 파일 재디코딩 → 전체 해상도 CPU 처리 → JPEG(기본 품질 93)/PNG 인코딩 → 선택한 파일에 기록.
- 편집 저장: 명시적 Save → 원본 경로 뒤에 `.photolab.json`을 붙여 보정값 저장. 사진을 다시 열 때 사이드카를 읽는다.

## 명세 대비 기능 목록

아래의 ‘소스 있음’은 정상 실행이나 품질 검증 완료를 의미하지 않는다.

| 명세 | 소스 기준 구현 | 차이 또는 검증 필요 |
|---|---|---|
| VIEW-001, VIEW-004 | JPEG/PNG/RAW 선택, `Stretch.Uniform` 화면 맞춤 | 손상 파일, 실제 카메라 파일 실행 검증 필요 |
| VIEW-002, VIEW-003, VIEW-005 | 미구현 | 확대·축소, 이동, 100% 보기 없음 |
| VIEW-006, VIEW-007 | Original 토글 소스 있음 | 별도의 분할 Before/After 비교 없음 |
| VIEW-008, TASK-019 | RGB 히스토그램 계산 Core만 있음 | UI 호출, 휘도 및 클리핑 표시 없음 |
| EDIT-001~004, EDIT-007~010 | 8개 슬라이더 및 CPU 보정 소스 있음 | 색온도는 상대값 -100~100, Kelvin/RAW WB 조절 아님 |
| EDIT-005~006, EDIT-011~016 | 미구현 | 화이트/블랙, HSL, 커브, 그레이딩, 선명도, 텍스처, 디헤이즈 없음 |
| HIST-001~003 | Undo/Redo, 전체 초기화 소스 있음 | 드래그 묶음 처리와 히스토리 크기 제한 없음 |
| HIST-008, TASK-023 | 파일별 JSON 저장 및 사진 열기 시 복원 소스 있음 | 자동 저장, 마지막 작업 자동 재개, 데이터 버전/마이그레이션 없음 |
| HIST-004~007 | 미구현 | 프리셋, 보정값 복사, 일괄 적용 없음 |
| LIB-001~008 | 미구현 | Library 패널은 현재 사진 정보/안내만 표시 |
| 명세 4.2, TASK-005~007 | RGBA8 버퍼, CPU 처리 인터페이스 소스 있음 | 16비트/float 버퍼, Stride·색 공간 메타데이터, 재사용 없음 |
| 명세 5.2, TASK-008~011 | 공유 RAW 확장자 분류 및 LibRaw 실행 경로 있음 | IRawDecoder, 촬영 메타데이터, 제조사별 실파일 검증 없음 |
| 명세 7절, TASK-004 | 90ms 디바운스, 작업 취소, 현재 CTS 동일성 검사 있음 | 연속 입력 실시간 갱신 요구와 차이, 조작 종료 고품질 렌더 없음 |
| 명세 8절, TASK-012~016 | CPU만 구현 | SIMD/병렬 픽셀 처리, GPU 및 GPU 오류 시 Fallback 없음 |
| 명세 9절 | 원본과 편집 JSON 분리 소스 있음 | 내보내기 원본 덮어쓰기 방지, 안전한 사이드카 저장 보완 필요 |
| 명세 10절 | 어두운 테마, O/S/Z/Shift-Z 단축키 소스 있음 | 보정값 직접 입력, 개별 초기화, 패널 접기, 썸네일 스트립 없음 |
| 명세 11절 | 일부 Core 스모크 테스트 및 Windows/macOS CI 정의 있음 | 색상 정확도, RAW, 경계값, 취소/경합, 성능 및 실기기 검증 없음 |

ICC/LittleCMS, SQLite, 렌즈 보정, 노이즈 제거, 마스킹, AI 기능은 구현되지 않았다. 입력 ICC/EXIF를 추출·유지하는 명시적 경로가 없으며 출력 보존은 보장할 수 없다.

## 발견한 문제와 후속 작업

| 우선순위 / 작업 | 근거 | 영향 및 필요한 조치 |
|---|---|---|
| P0 / TASK-002 | 테스트 `Program.cs:18`과 `:58`에 `mono` 재선언 | CS0136으로 스모크 테스트 컴파일 실패. 테스트 지역 변수 이름 수정 후 재실행 필요 |
| P0 / TASK-003 | `EditorWindow.ExportAsync`에 원본 경로 비교 없음, `OpenWriteAsync` 후 `SetLength(0)` | 원본을 출력 대상으로 선택하면 원본을 덮어쓸 수 있음. 원본 보존 검사와 회귀 검증 필요 |
| P0 / TASK-004 | `ApplyEdit`가 매 입력마다 `_debounce.Stop/Start` | 90ms보다 짧은 간격으로 입력이 계속되면 렌더가 지연됨. 연속 갱신 요구를 충족하는 스케줄링 필요 |
| P0 / TASK-004 | 취소는 `RenderPreviewAsync` 시작 시 수행 | 새 값 입력부터 다음 렌더 시작까지 이전 작업이 표시될 수 있음. 현재 CTS 검사는 새 편집값 버전을 검증하지 않음 |
| P0 / TASK-003~004 | `OpenAsync`에 로딩 취소/버전 관리 없음; UI가 중복 열기를 막지 않음 | 겹친 로딩의 완료 순서에 따라 이전 선택이 새 선택을 덮을 가능성. 닫힘·이미지 전환·타이머 경합 실행 검증 필요 |
| P0 / TASK-023 | `SidecarStore.SaveAsync`가 대상 JSON을 직접 `File.Create` | 취소/실패 중 기존 JSON 손실 가능. 원자적 저장, 버전 및 값 유효성 정책 필요 |
| P0 / TASK-005~011 | `PixelBuffer.Data`는 수정 가능한 byte[], PPM은 8비트로 축소 | 읽기 전용 원본 소유권 보장 부족, RAW 정보 손실. Core 수정 금지 규칙을 유지한 채 변경 범위 결정 필요 |
| P1 / TASK-024 | `_history`에 값 변경마다 추가, 상한 없음 | 긴 편집 시 히스토리 증가. 드래그 통합과 크기 제한 필요 |
| P1 / TASK-015, TASK-034 | RGBA/Skia 색 배열 복사, PNG encode/decode, UI Bitmap 생성 | 할당·UI 처리 비용 측정 필요. 현재 FPS/메모리 성능은 미측정 |

`SkiaImageCodec.LoadPreview`와 `ResizePreview`는 축소 계산/그리기를 반복한다. RGBA ↔ SKColor 변환 루프도 반복하지만 즉시 공통화할 필요성은 측정과 후속 변경 범위에 따라 판단한다. `AddSlider`의 `read` 인자는 사용되지 않는다. `Histogram.Compute`는 테스트에서만 사용되며 후속 히스토그램 기능의 기반이므로 삭제하지 않는다.

명시적으로 불필요하다고 확정할 패키지는 발견하지 않았다. 플랫폼 네이티브 패키지는 대상 OS 배포를 위한 참조다. 전이 의존성 전체, 배포 파일 최소화 및 라이선스 검토는 미수행이다.

버전은 Spec/Task가 0.1.2, README/CHANGELOG가 0.1.1이며 프로젝트에 명시적 Version 설정이 없다. README의 SDK 부재 설명은 현재 환경과 다르다. 기존 `bin/obj` 산출물은 이번 소스의 빌드 성공 근거로 사용하지 않았다. `.git` 저장소가 없어 변경 이력/CI 실행 결과를 확인할 수 없다.

## 수행한 검증

| 항목 | 결과 |
|---|---|
| 소스/프로젝트 구조 | 솔루션 3개 프로젝트와 참조 확인, 모든 작성 C# 소스 검토 |
| SDK | `dotnet --info`: SDK 10.0.102, runtime 10.0.2, macOS osx-arm64 |
| macOS 실행 스크립트 문법 | `bash -n run_mac.sh` 성공 (앱 실행은 하지 않음) |
| Core 스모크 테스트 | `dotnet run --project tests/PhotoLab.Core.Tests/PhotoLab.Core.Tests.csproj -c Release --no-restore` 실패: Program.cs(58,12), CS0136. 테스트 본문 실행 전 실패 |
| 전체 솔루션/Desktop 빌드 | 사용자 지침에 따라 수행하지 않음 |
| NuGet 복원 | 수행하지 않음; 테스트 실행에는 기존 복원 파일 사용 |
| Runtime / 이미지 품질 / 성능 | 미수행 |
| Windows / macOS Intel | 사용 가능한 해당 환경에서 검증하지 못함 |
| macOS Apple Silicon 앱 실행 | 미수행; SDK 및 제한된 테스트 컴파일 확인만 수행 |

## TASK-002 진행 판단

분석 결과는 준비되었고, .NET 10 SDK와 Windows/macOS CI 정의가 존재하므로 TASK-002의 환경 점검과 테스트 컴파일 오류 수정부터 진행할 수 있다. 다만 전체 프로젝트 빌드는 사용자 지침상 별도 명시적 요청이 필요하며, Windows/macOS 빌드·실행 결과가 확보되기 전 TASK-002를 완료로 처리할 수 없다.

TASK-001도 `Task.md` 13절의 공통 완료 기준에 따라 `[~]`로 유지한다. 소스 분석 산출물은 작성했지만 전체 빌드, 테스트 통과, 플랫폼 검증은 미충족이다. 다음 우선 작업은 TASK-002이며, TASK-003의 원본 덮어쓰기 방지와 TASK-004의 연속 렌더링 문제를 우선 확인할 필요가 있다.
