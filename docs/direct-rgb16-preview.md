# Direct RGB16 preview

native half-size 출력의 RGB16 데이터를 입력 크기의 RGBA32F로 먼저 확장하지 않고, 선형광 area 평균으로 표시 크기의 float 버퍼를 바로 만든다. `ImageResize.PreviewRgb16`은 기존 RGBA 경로와 footprint/coverage 계산을 공유한다. 각 RGB16 값을 먼저 float로 나눠 기존 확장의 반올림을 재현한 뒤 double로 area 합을 계산한다. 표시 이전에 8비트로 변환하지 않는다.

## Native ownership

기존 `Marshal.Copy`용 대형 short 배열도 제거했다. NativeRawSession은 SDK 출력의 양수 치수, RGB16 payload 크기와 포인터를 검증하고 serialized gate 안에서 읽기 전용 native span을 잠시 빌린다. 반환 결과는 독립 소유 float 버퍼다. native 이미지 해제는 변환 뒤 `finally`에서 수행하며 span은 밖으로 반환하지 않는다. session disposal도 같은 gate를 기다리므로 변환 중 handle/module을 해제하지 않는다.

이 경로를 위해 Infrastructure 프로젝트에 unsafe blocks를 허용했고 포인터 span 생성은 NativeRawSession의 작은 범위로 제한했다. Imaging의 처리는 safe C#이다. 추가 패키지나 bridge ABI 변경은 없다. full 현상도 동일한 borrowed RGB16 입력에서 owned float를 생성한다.

## Correctness

150개 합성 geometry에서 RGB16 직접 축소와 기존 float 확장/축소의 모든 float 샘플이 일치했다. 별도의 전체 입력 area integral도 동일했다. 1px, 홀수 폭/높이, 분수 footprint, 극단적인 비율, 축소 없는 복사, 0/1/65535 샘플, 입력 불변성을 포함한다. 잘못된 치수/데이터/edge와 pre-cancellation을 검사했다. 기존 alpha-weighted RGBA/동일 크기/float 처리 회귀도 유지했다.

실제 RAW 6종의 full/half CLI 기준, 취소·닫힘·출력 수명, cached loader, 연속 WB/0/idle 품질 회귀가 통과했다. 이 최적화는 LibRaw 16비트 출력 이후의 변환이며 sensor-float 색 정확도나 decoder 이전 clipping 문제를 해결한 것으로 해석하지 않는다.

## Paired cost measurement

macOS ARM64 / Release, 두 경로 warm-up 후 순서를 번갈아 10쌍 측정했다. 입력은 이미 managed RGB16이며 output pool은 warm 상태다. 기존 비교 경로는 RGB16→RGBA32F 확장 후 현재 RGBA area resizer를 호출한다.

| 입력 → 최대 600px | 확장+축소 median | 직접 축소 median | 확장+축소 할당 median | 직접 할당 median |
| --- | --- | --- | --- | --- |
| 2154×1434 | 14.646ms | 9.493ms | 49,421,480 bytes | 40 bytes |
| 3000×2000 | 21.829ms | 10.521ms | 96,000,104 bytes | 40 bytes |

40 bytes는 warm pool에서 새 float-buffer 객체를 만드는 측정값이며 buffer의 실제 메모리 크기가 40 bytes라는 뜻은 아니다. 첫 rent/메모리 footprint/앱 전체 메모리는 별도다. 위 비용은 native RAW 현상/포인터 접근/UI를 제외한다. 실제 native 경로에서는 input-sized managed RGB16 복사도 없어졌으므로 별도의 할당 원인을 줄인다.

## Actual editor observation

6종의 warm-cache 연속 WB/Skia 관찰률은 CR2 34.9, CR3 35.5, NEF 39.6, ARW 37.4, RAF 33.7, DNG 24.1회/초였다. DNG의 frame 간격 p95는 44.62ms였다. 이전 별도 실행은 21.0회/초/p95 63.08ms였다. UI 수치는 paired 벤치마크가 아니므로 이 차이 전체를 구현 효과로 단정하지 않는다. 중간 WB 합치기·16ms polling·frame capture·0 확인 대기를 포함하고 네이티브 OS FPS를 제외한다. DNG는 아직 30회/초에 못 미친다.

전체 522개 검사 (Core 16, Imaging 56, Desktop 206, UI 47, NativeSession 80, CachedLoader 45, NativeUI 72), Release 빌드 (기존 CS0618 2개), Core/fixture 해시와 앱 5초 시작을 통과했다. 다음은 native 현상/area 축소/UI 단계의 시간과 할당을 분리 측정하고 반복 footprint 계산·SIMD/GPU를 검토하는 것이다. Windows·ICC·색 기준·20MP 이상·장시간 메모리는 남아 있다.

```bash
dotnet run --project tests/PhotoLab.Imaging.Tests -c Release
dotnet run --project tests/PhotoLab.Imaging.Tests -c Release -- --rgb-preview-benchmark
```

[검증 집계](verification/2026-10-11-rgb-preview-results.json) · [회귀 로그](verification/2026-10-11-rgb-preview-tests.txt) · [변환 벤치마크](verification/2026-10-11-rgb-preview-benchmark.txt) · [빌드](verification/2026-10-11-rgb-preview-build.txt) · [시작/해시](verification/2026-10-11-rgb-preview-startup.json)
