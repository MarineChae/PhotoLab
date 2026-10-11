# PhotoLab — 선형 float RAW 편집 프로토타입 (Unreleased)

## 현재 검증 상태 (2026-10-11)

[현재 개발 보고서](docs/interactive-wb-preview.md)와 [PROGRESS.md](PROGRESS.md)에 실제 구현 및 검증 근거를 기록합니다. macOS ARM64 / .NET SDK 10.0.102에서 전체 Release 빌드, 앱 시작 및 Core/Imaging/Desktop 회귀 테스트를 확인했습니다. 실제 CR2/CR3/NEF/ARW/RAF/DNG 샘플 6개의 float 디코딩과 전체 해상도 내보내기를 검증했습니다. Windows 실행, 전체 UI 조작, GPU 및 색상 정확도 검증은 남아 있습니다. 작업 목록은 [TASKS.md](TASKS.md), 개발 지침은 [AGENTS.md](AGENTS.md)에 있습니다.

> **개발 상태**: 개발 중인 프로토타입입니다. Spec.md의 기준 버전은 0.1.2이며 이번 변경은 미출시 상태입니다. Lightroom 수준의 색상 재현·RAW 하이라이트 복원·상용 품질은 검증하지 않았습니다.

## 목표

C#으로 Lightroom 대체 앱을 개발합니다. Windows/macOS 공통 Avalonia UI, 순수 C# 편집 상태 및 고정밀 Imaging 모듈을 사용합니다. LibRaw 외부 디코더(`dcraw_emu`)의 선형 16비트 출력을 float 버퍼에 전달하며, ICC 관리와 GPU 백엔드는 후속 작업입니다.

## 기술

- C# / .NET 10
- Avalonia UI 11.3.22 (MIT)
- SkiaSharp 3.119.4 (MIT, 이미지 JPEG/PNG 코덱과 크기 변경에 사용)
- UI와 독립적인 `PhotoLab.Core` (순수 C#, 패키지 종속성 없음)
- 선형 float 처리 `PhotoLab.Imaging`, 안전한 파일 저장 `PhotoLab.Infrastructure` (추가 외부 패키지 없음)
- macOS ARM64에서 빌드·제한된 실행 검증, macOS x64 및 Windows 실기기 검증 대기

## 구현된 기능 (소스 기준)

- JPEG/PNG 및 여러 제조사의 RAW 파일 열기, 원본 미리보기 (RAW는 LibRaw 별도 설치 필요)
- 노출 EV, 대비, 하이라이트, 그림자, 상대 색온도, 틴트, 채도, 생동감
- 슬라이더 변경 시 즉시 렌더 요청, 이전 작업 취소·버전 검사·직렬 CPU 처리
- 실제 편집기의 Exposure 드래그/Skia 사진 프레임·idle 품질·저장·단일 Undo/Redo 헤드리스 검증 (네이티브 OS FPS는 별도)
- 미리보기 RGBA8를 Bitmap으로 직접 복사하여 PNG 왕복 비용 제거 (float 원본은 유지)
- 원본/보정본 전환
- Open folder로 같은 폴더의 JPEG/PNG/RAW 후보 목록을 열고 선택·← Prev·Next →로 이동. 창 안에서 돌아오면 저장 전 보정값/WB 복원 (영구 보관은 Save edits 필요, 사진별 Undo history는 새로 시작). 썸네일/catalog/native folder picker 검증은 미완료 ([검증 보고서](docs/photo-folder-navigation.md))
- BASIC PRESETS: 이름 입력 후 Save new, 선택 후 Preview preset 또는 Apply, Delete. 8개 기본 보정값 저장·다른 사진 적용·단일 Undo/Redo 지원. Camera RAW WB는 현재 사진에 유지하며 미리보기 중 저장/내보내기는 적용 또는 취소 후 가능 ([프리셋 검증](docs/basic-presets.md))
- 드래그 단위 Undo / Redo, 128개 상태 제한, 모든 보정값 초기화
- 원본을 바꾸지 않는 버전 1 JSON 보정값 저장·불러오기 (`사진.jpg.photolab.json`), 기존 파일 호환 및 취소 시 이전 JSON 보존
- 원본·링크 경로를 보호하는 전체 해상도 JPEG/PNG 내보내기
- 선형 sRGB RGBA32F 처리, RAW 16비트 정밀도 유지, 표시/인코딩 시 RGBA8 변환
- JPEG/PNG 내장 source profile을 linear sRGB float로 변환해 작업 gamut 밖 RGB 유지, 출력에 sRGB 태그 선언. PHOTOLAB_LCMS_LIBRARY를 지정하면 원본 ICC를 LittleCMS로 처리 (monitor·LUT 기준 검증은 미완료)
- 앱 옆에 배포된 LCMS/PNG16 라이브러리 자동 선택, 환경 변수로 경로 override. PNG16 RGB/gray/alpha·tRNS·Adam7 정밀도 및 float 편집 연결. native 파일이 없는 개발 실행은 기존 Skia 입력 경로 사용
- "16-bit PNG"를 체크하고 Export에서 PNG를 선택하면 float 결과를 sRGB RGBA16으로 저장 (최신 PNG bridge 필요). 기본 PNG/JPEG는 8비트. 실제 RAW 6종 전체 출력·원본 보존을 검증했고 OS 저장 dialog·Windows는 별도 확인 필요
- PNG16 저장은 한 행 변환과 압축 chunk를 임시 파일에 직접 쓰고 완성 후 교체하여 전체 RGBA16/압축 결과 복사본 할당을 피함. 실패/취소 시 기존 출력 보존 ([20MP 비교](docs/png16-streaming.md))
- 교체 가능한 RAW 디코더와 카메라 제조사·모델·ISO·셔터·조리개 표시 (`raw-identify`가 같은 LibRaw 설치에 있을 때)
- LibRaw 공유 라이브러리의 C getter로 WB·카메라 색행렬을 float 정밀도로 추출·보존 (픽셀에 중복 적용하지 않음)
- 개발 엔진/로더 API에서 원본 카메라 공간 사용자 WB 현상 지원 (현재 UI Temperature/Tint는 기존 상대 보정)
- RAW 전용 상대 Warmth/Tint·Apply WB/As shot, version 2 파일 복원·저장·전체 해상도 출력·초기화/Undo/Redo 연결 (헤드리스 입력 검증, 네이티브 OS 입력은 미검증)
- PHOTOLAB_NATIVE_RAW_BRIDGE를 명시한 native session 경로: unpack 캐시·연속 WB 슬라이더/최신 요청 합치기·600px 우선 표시/idle 품질 승격·전체/내보내기 공유 (별도 SDK 연결 계층 빌드 필요, 네이티브 OS 입력과 UI FPS는 미검증)
- WB 갱신 중 accepted Bitmap 유지, 드래그 입력 역산 오차 방지 및 0에서 cached as-shot float 복원/idle 품질 연결 (실제 RAW 6종 헤드리스 연속 입력 검증, DNG 성능 개선은 진행 중)
- native RGB16을 직접 area 축소해 큰 중간 RGBA32F 및 managed RGB16 복사를 제거 (native 이미지 수명 안에서만 읽고 owned float 반환)
- 보정된 float preview의 실시간 RGB/Y 256-bin histogram, log-count 곡선과 RGB ≤0/≥1 비율 표시. 최신 이미지와 함께 갱신하며 Original 비교 중에도 "Edited preview" 집계를 유지 (full-resolution/Windows/OS 표시 검증은 남아 있음)
- 순수 C# 코어 스모크 테스트 / GitHub Actions 빌드 정의
- 별도 Vulkan 초기 compute 검증 도구: Apple M4/MoltenVK 노출·전송·HDR/alpha·CPU 비교 통과
- PHOTOLAB_VULKAN_LIBRARY를 지정한 opt-in Vulkan 미리보기: 기본 float controls·persistent buffers·worker 초기화·CPU fallback 연결 (Apple M4 검증, Windows/다른 GPU/큰 실제 RAW 성능 검증은 진행 중)
- opt-in에서도 600×600 이하 조작 preview는 CPU, 큰 품질 preview는 GPU 선택 (실제 DNG paired 측정 기준; 다른 장비의 crossover는 미검증)

## 아직 **구현되지 않은** 기능

- 전문가용 RAW 현상 정확도 및 하이라이트 재구성 (LibRaw 색 변환·16비트 출력 이전의 정보까지 보존하는 엔진은 아님)
- GPU 연산 (현재 보정은 **CPU만** 사용; Skia/Avalonia의 화면 렌더링 가속과 구분)
- 16비트 일반 이미지 디코딩/내보내기, ICC 색상관리, 정확한 화이트밸런스(Kelvin)
- 고품질 하이라이트 재구성, 노이즈 제거, 렌즈 프로파일, HSL, 커브, 마스킹
- 프로 사진 라이브러리·일괄 현상·다중 사진 관리
- 전체 EXIF/렌즈 메타데이터, 색 정확도 검증과 ICC 유지
- 상용 QA, 설치 패키징/코드 서명, 색상 재현 테스트

**앱 보정은 선형 sRGB float, 화면 표시와 JPEG/PNG 인코딩은 RGBA8입니다.** 일반 JPEG/PNG 입력은 현재 Skia의 RGBA8 디코딩을 사용합니다. RAW 일부 촬영 정보는 표시하지만 원본의 ICC 프로파일/EXIF를 출력 파일에 보존하지 않습니다.

## 멀티 카메라 RAW 지원

RAW 파일은 제조사별 전용 분기를 만들지 않고 **LibRaw**를 공통 디코더로 사용합니다.

| 제조사/표준 | 검색 가능한 확장자 예시 |
|---|---|
| Canon | CR2, CR3, CRW |
| Nikon | NEF, NRW |
| Sony | ARW, SRF, SR2 |
| Fujifilm | RAF |
| Olympus / OM System | ORF |
| Panasonic | RW2, RAW |
| Adobe / 모바일 | DNG |
| Pentax / Leica | PEF, PTX, RWL |
| 기타 | SRW, 3FR, FFF, IIQ, EIP, ERF, KDC, DCR, MRW, MOS, X3F 등 |

**확장자를 파일 선택기에 표시하는 것과 실제로 그 RAW를 성공적으로 디코딩하는 것은 다릅니다.** 카메라 모델, 압축 포맷, 설치된 LibRaw 빌드·버전에 따라 파일을 열지 못할 수 있습니다. 예를 들어 특정 최신 Nikon/Canon 파일이나 DNG 압축 방식은 별도 옵션으로 빌드한 LibRaw가 필요할 수 있습니다.

### macOS LibRaw 설치

```bash
brew install libraw
which dcraw_emu
```

Homebrew의 LibRaw formula는 `dcraw_emu` 실행 파일을 제공합니다. Apple Silicon은 보통 `/opt/homebrew/bin/dcraw_emu`, Intel은 `/usr/local/bin/dcraw_emu`이며 앱은 두 경로를 자동 검사합니다.

### Windows LibRaw 설치

LibRaw의 `dcraw_emu.exe`가 포함된 신뢰할 수 있는 Windows 빌드를 준비하고, 실행 파일 위치를 환경 변수로 지정합니다.

PowerShell 예시:

```powershell
$env:PHOTOLAB_DCRAW_EMU = 'C:\Tools\LibRaw\dcraw_emu.exe'
dotnet run --project .\src\PhotoLab.Desktop\PhotoLab.Desktop.csproj -c Release
```

macOS에서도 환경 변수 `PHOTOLAB_DCRAW_EMU`로 실행 파일을 직접 지정할 수 있습니다.

### 이 버전의 RAW 처리 흐름

```text
CR2 / CR3 / NEF / ARW / RAF / DNG / ...
       ↓
LibRaw dcraw_emu -w -o 1 -4 (카메라 WB · 선형 sRGB · 16-bit PPM)
       ↓
선형 16비트 PPM 디코더 (PhotoLab.Imaging)
       ↓
RGBA32F 편집 버퍼 → CPU 보정
       ↓
표시/인코딩 시 sRGB RGBA8 → 프리뷰 / JPEG·PNG 내보내기
```

- RAW 원본은 변경되지 않으며, 원본 옆의 `.photolab.json`에 보정값을 저장합니다.
- RAW는 미리보기를 열 때 한 번, 원본 해상도로 내보낼 때 한 번 디코딩합니다. 슬라이더 이동마다 RAW 재디코딩하지 않습니다.
- `dcraw_emu`가 생성한 PPM은 임시 디렉터리에서 읽고 삭제합니다.
- 전문가용 현상을 위한 원본 RAW WB 재조정, ICC 및 카메라 프로파일 처리·색 정확도 검증은 남아 있습니다.
- 이 소스에는 LibRaw 네이티브 바이너리가 동봉되어 있지 않습니다. 배포 시 플랫폼별 패키징과 라이선스 의무를 별도로 검토해야 합니다.

## 실행법

### macOS (Apple Silicon/Intel)

1. [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 설치
2. 저장소 최상위 폴더에서 터미널 열기
3. 다음 명령 실행

```bash
dotnet restore PhotoLab.slnx
dotnet build PhotoLab.slnx -c Release
dotnet run --project src/PhotoLab.Desktop/PhotoLab.Desktop.csproj -c Release
```

### Windows (PowerShell)

```powershell
dotnet restore .\PhotoLab.slnx
dotnet build .\PhotoLab.slnx -c Release
dotnet run --project .\src\PhotoLab.Desktop\PhotoLab.Desktop.csproj -c Release
```

### 테스트

LittleCMS 입력 경로를 macOS에서 선택하려면 `brew install little-cms2` 후 다음처럼 실행합니다. 라이브러리를 지정하지 않으면 기존 Skia 입력 경로를 사용합니다. Windows에서는 해당 아키텍처의 LCMS 2 DLL 경로를 지정할 수 있으나 실제 실행·배포 검증은 남아 있습니다.

```bash
PHOTOLAB_LCMS_LIBRARY="$(brew --prefix little-cms2)/lib/liblcms2.dylib" dotnet run --project src/PhotoLab.Desktop -c Release
dotnet run --project tests/PhotoLab.Desktop.Tests -c Release -- --lcms "$(brew --prefix little-cms2)/lib/liblcms2.dylib"
```

PNG16 입력을 개발 실행에서 사용하려면 libpng 연결 계층을 빌드하고 경로를 지정합니다. 배포 폴더의 라이브러리는 환경 변수 없이 자동 선택합니다. 자세한 검증/제약은 [PNG16 보고서](docs/png16-input.md)와 [배포 연결 보고서](docs/color-runtime-package.md)를 참고하세요.

```bash
brew install libpng little-cms2
python3 scripts/build_native_png.py --prefix "$(brew --prefix libpng)" --output /tmp/photolab-native/libPhotoLab.NativePng.dylib
PHOTOLAB_PNG16_BRIDGE=/tmp/photolab-native/libPhotoLab.NativePng.dylib PHOTOLAB_LCMS_LIBRARY="$(brew --prefix little-cms2)/lib/liblcms2.dylib" dotnet run --project src/PhotoLab.Desktop -c Release
dotnet run --project tests/PhotoLab.Desktop.Tests -c Release -- --png16 /tmp/photolab-native/libPhotoLab.NativePng.dylib "$(brew --prefix little-cms2)/lib/liblcms2.dylib"
dotnet run --project tests/PhotoLab.Desktop.Tests -c Release -- --png16-export /tmp/photolab-native/libPhotoLab.NativePng.dylib
```

```bash
dotnet run --project tests/PhotoLab.Core.Tests/PhotoLab.Core.Tests.csproj
dotnet run --project tests/PhotoLab.Imaging.Tests/PhotoLab.Imaging.Tests.csproj
dotnet run --project tests/PhotoLab.Rendering.Tests/PhotoLab.Rendering.Tests.csproj -c Release
dotnet run --project tests/PhotoLab.Imaging.Tests/PhotoLab.Imaging.Tests.csproj -c Release -- --rgb-preview-benchmark
dotnet run --project tests/PhotoLab.Desktop.Tests/PhotoLab.Desktop.Tests.csproj -- --benchmark
dotnet run --project tests/PhotoLab.Desktop.Tests/PhotoLab.Desktop.Tests.csproj -c Release -- --ui
dotnet run --project tests/PhotoLab.Desktop.Tests/PhotoLab.Desktop.Tests.csproj -c Release -- --display-benchmark
```

실제 RAW 경로를 Desktop 테스트의 `--` 뒤에 추가하면 촬영 정보·WB/행렬·전체 해상도 출력·원본 해시를 검증합니다. 이 검증에는 `dcraw_emu`, `raw-identify`와 같은 설치의 LibRaw 공유 라이브러리가 필요합니다.

외부 패키지 다운로드를 위해 최초 `dotnet restore` 시 인터넷 연결이 필요합니다.

## 폴더 구조

```text
PhotoLab.slnx
src/
  PhotoLab.Core/       PixelBuffer, PpmImageCodec, PhotoFormats, EditSettings, CpuImageProcessor, SidecarStore
  PhotoLab.Desktop/    Avalonia UI, SkiaSharp 코덱, LibRawCommandDecoder, async preview / export
  PhotoLab.Imaging/    선형 float 버퍼, CPU 보정, 선형 PPM, 미리보기 축소
  PhotoLab.Infrastructure/ 원본 보호, 원자적 파일 저장, 버전 JSON
tests/
  PhotoLab.Core.Tests/ 제로 의존성 테스트 러너
  PhotoLab.Imaging.Tests/ 정밀도·색 전달·버퍼 수명 테스트
  PhotoLab.Desktop.Tests/ 코덱·렌더 경합·파일 저장·실제 RAW 테스트
  fixtures/raw/manifest.json  CC0 실제 파일 출처 및 SHA-256
.github/workflows/build.yml  Windows + macOS 빌드 정의
```

## 주요 설계 원칙

- **비파괴 편집**: `EditSettings`는 불변 레코드입니다. 원본 파일을 덮어쓰지 않습니다.
- **처리 분리**: 이미지 코어는 Avalonia·SkiaSharp를 알지 못합니다.
- **레이턴시**: 600px 상호작용 미리보기와 최대 1800px 미리보기를 캐시합니다. 입력이 멈춘 뒤 180ms 유휴 타이머가 큰 미리보기를 요청합니다. 슬라이더 조작 시 RAW/원본 재디코딩을 하지 않습니다.
- **내보내기**: 원본 파일을 다시 디코딩하고 최신 설정을 적용합니다. RAM 사용량은 20MP에서 클 수 있습니다.
- **기준 엔진**: CPU 엔진은 앞으로 GPU 결과를 비교 검증할 기준이지만, 현재는 전문 사진 현상 정확도 수준의 레퍼런스는 아닙니다.

## 다음 단계 우선순위

1. Windows 빌드·실행 및 macOS 전체 UI/메모리 검증
2. 연속 WB 제어·요청 중복 제거·UI 지연/FPS, 중립점/Kelvin·네이티브 GUI 입력·색행렬 정확도·추가 RAW 모드 검증
3. 카메라 색 변환 정확도와 모니터 ICC 적용
4. 동일 엔진 API를 사용하는 Windows/macOS GPU 백엔드 실험
5. 히스토그램 UI, 확대/축소, 톤 커브, HSL, 프리셋, 색상 정확도 자동 테스트

## 알려진 주의 사항

- macOS 빌드에서 기존 ToggleButton.Checked/Unchecked 사용에 대한 CS0618 경고가 있습니다. Windows 실행은 미검증입니다.
- UI 드래그 이벤트 연결은 빌드 확인했으며 실제 입력 장치로 Undo 통합 동작을 확인해야 합니다.
- LibRaw의 초기 카메라 화이트밸런스·색 변환은 디코딩 단계에서 이미 적용됩니다. 기존 RAW 편집값은 예전 자동 밝기/감마 경로와 시각적으로 달라질 수 있습니다.
- JPEG는 투명도를 지원하지 않으며, 기존 투명 픽셀은 흰색 합성이 아니라 알파를 255로 변경합니다.
- 사이드카는 사진과 같은 디렉터리에 저장하므로 읽기 전용 폴더에서는 저장할 수 없습니다.

### 빠른 실행

- macOS: `chmod +x run_mac.sh && ./run_mac.sh`
- Windows: `run_windows.bat` 실행

macOS ARM64용으로 색상 라이브러리를 함께 배치하려면 다음 명령을 사용합니다. 현재 산출물은 .NET 10 런타임이 필요한 개발용 배포 폴더입니다. RAW의 LibRaw 도구는 별도 설치가 필요하며 Windows 패키징·설치 프로그램·정식 서명/공증은 남아 있습니다.

```bash
dotnet publish src/PhotoLab.Desktop -c Release -r osx-arm64 --self-contained false -o artifacts/macos-arm64
python3 scripts/package_color_mac.py --png-prefix "$(brew --prefix libpng)" --lcms-prefix "$(brew --prefix little-cms2)" --directory artifacts/macos-arm64
./artifacts/macos-arm64/PhotoLab
```
