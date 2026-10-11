# Direct preview display

미리보기 렌더러는 보정한 float 버퍼에서 표시용 sRGB RGBA8 `PixelBuffer`를 생성한다. 화면은 이를 immutable Bitmap으로 복사하며 PNG 인코딩/디코딩을 하지 않는다. Original도 같은 경로를 사용한다. 내보내기 JPEG/PNG, float 처리 순서, 최신 요청 판정과 원본 보존 규칙은 유지한다.

## Ownership and color

`PreviewBitmap.Create`는 RGBA8 배열을 생성자 호출 동안만 pin한다. Avalonia 11.3.22의 [Bitmap 생성자](https://github.com/AvaloniaUI/Avalonia/blob/11.3.22/src/Avalonia.Base/Media/Imaging/Bitmap.cs)와 [Skia ImmutableBitmap 구현](https://github.com/AvaloniaUI/Avalonia/blob/11.3.22/src/Skia/Avalonia.Skia/ImmutableBitmap.cs)을 확인했다. Skia는 입력을 복사하며 반환 뒤 pin을 해제해도 화면 데이터가 유효하다. 앱의 기존 Skia 백엔드를 사용하고 새 의존성을 추가하지 않았다.

표시 버퍼는 straight-alpha RGBA8, 96dpi다. 별도의 감마 변환을 추가하지 않는다. 헤드리스 테스트에서 홀수 폭/행 경계, RGBA 순서, alpha 0/1/128/254/255, 원본 배열 변경 및 GC 후 bitmap 소유권을 확인했다. 완전 투명 픽셀의 RGB는 Skia 복사에서 0이 된다. 화면에 기여하지 않는 값이며 float 처리 버퍼는 변경하지 않는다. 불투명 픽셀은 이전 PNG 표시 경로와 일치했다.

렌더가 무효화되면 managed 표시 배열은 참조가 해제되어 GC 대상이 된다. 기존 CTS·직렬 worker·source drain·Bitmap 분리 후 dispose 규칙을 유지한다. 전체 앱 장시간 메모리 누수 검증은 별도 작업이다.

## Measurements

macOS ARM64의 headless UI/Skia에서 합성 RGBA8 데이터로 양쪽을 warm-up하고 순서를 번갈아 20쌍 측정했다. PNG는 worker 인코딩 + UI Bitmap 디코딩을, 직접 경로는 pin + Bitmap 복사/해제를 포함한다.

| 크기 | PNG median / p95 | 직접 Bitmap median / p95 |
| --- | --- | --- |
| 600×400 | 4.527 / 4.783ms | 0.425 / 0.461ms |
| 1800×1200 | 45.650 / 46.288ms | 3.868 / 4.178ms |

float 보정/표시 변환과 Bitmap 생성을 포함한 60개 입력 측정에서 600×400은 입력 중 60개 bitmap 생성, 58.9회/초, accepted request p95 5.79ms였다. 실제 Image 배치/화면 합성은 포함하지 않으므로 GUI FPS로 해석하면 안 된다. 로그의 upload는 CPU bitmap 복사를 뜻하며 GPU 전송 측정이 아니다.

1800×1200은 16ms 간격의 새 요청이 기존 작업을 계속 무효화해 입력 중 accepted 결과가 0개였다. 입력 종료 뒤 최종 요청은 처리됐다 (accepted request p95 38.33ms). 따라서 현재 앱의 600px 상호작용/idle 후 1800px 품질 구조를 유지한다. 전체 해상도 실시간 보정 성능을 입증하지 않는다.

Core 16, Imaging 51, Desktop 206, UI 40, NativeSession 80, CachedLoader 45, NativeUI 42, 총 480개 검증을 통과했다. 실제 RAW 6종의 WB/Original/드래그/idle 품질 픽셀 일치 회귀도 새 표시 경로에서 통과했다. Core/fixture 해시 일치와 native opt-in 앱 5초 시작 (stderr 없음)을 확인했다. 다음은 실제 EditorWindow의 연속 슬라이더 입력과 frame 관찰, 프레임 단계별 지연/p95, SIMD/GPU 및 네이티브 OS 표시 검증이다.

## Reproduce

```bash
dotnet run --project tests/PhotoLab.Desktop.Tests -c Release -- --ui
dotnet run --project tests/PhotoLab.Desktop.Tests -c Release -- --display-benchmark
```

[벤치마크](verification/2026-10-11-direct-preview-benchmark.txt) · [회귀 집계](verification/2026-10-11-direct-preview-results.json) · [회귀 로그](verification/2026-10-11-direct-preview-tests.txt) · [빌드](verification/2026-10-11-direct-preview-build.txt) · [시작/해시](verification/2026-10-11-direct-preview-startup.json)
