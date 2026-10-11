# Area coverage optimization and native profiling

area 축소의 x/y footprint를 한 번씩 계산해 모든 row/column에 공유한다. 기존 float 정규화, double 가중치 곱과 합산 순서를 유지한다. 반복적인 bounds/Ceiling/overlap 계산을 줄이는 대신 source 축 길이에 비례하는 작은 scratch 배열을 할당한다. 동일 크기 복사는 기존 빠른 경로를 유지한다.

## Comparison

변경 전 Release 테스트 바이너리와 변경 후 바이너리를 별도 프로세스에서 순서를 번갈아 각각 두 번 실행했다. 각 실행은 warm-up 후 10쌍의 변환 측정이다. 마지막 비교 초반에 일부 Core/Imaging/Desktop 회귀도 실행되어 CPU/메모리 경합을 완전히 배제한 벤치마크는 아니다.

| 입력→600 | 변경 전 직접 축소 median (두 실행) | 변경 후 median | warm 관리 할당 전→후 |
| --- | --- | --- | --- |
| 2154×1434 | 9.176 / 9.824ms | 8.276 / 8.588ms | 40→44,816 bytes |
| 3000×2000 | 10.241 / 10.848ms | 9.760 / 9.776ms | 40→48,200 bytes |

전체 half RGBA32F/managed RGB16 복사는 다시 도입하지 않았다. 약 45KB의 footprint 배열과 처리 시간 사이의 tradeoff를 채택했다. 입력/출력 자체의 footprint, native 현상 및 UI는 위 표에 포함하지 않는다.

행 캐시와 SIMD 정규화도 실험했다. 모든 float/16비트 값의 기준 일치는 통과했지만 별도 비교에서 3.1MP 9.870ms→10.270ms, 6MP 10.907ms→12.611ms로 느려져 해당 코드는 제거했다. [실험 로그](verification/2026-10-11-area-coverage-rejected-simd.txt)는 유지한다. 성능을 추정해 채택하지 않고 실제 측정으로 선택했다.

## Stage evidence

NativeRawSession의 `DevelopmentTiming`은 serialized gate로 보호한 마지막 성공 현상의 SDK/변환 시간을 제공한다. 열기/unpack, gate 대기, native 이미지 해제는 제외한다. benchmark는 한 session에 10개 같은-WB warm 요청을 보내 스냅샷과 awaited total을 확인한다. UI와 native 메모리 footprint는 제외한다.

| RAW | SDK median | validation/coverage/conversion | awaited total |
| --- | --- | --- | --- |
| CR2 | 9.766ms | 7.612ms | 17.384ms |
| CR3 | 16.100ms | 6.455ms | 22.595ms |
| NEF | 5.360ms | 4.444ms | 9.783ms |
| ARW | 10.932ms | 5.130ms | 16.165ms |
| RAF | 14.424ms | 4.002ms | 18.426ms |
| DNG | 25.794ms | 8.802ms | 34.584ms |

DNG는 UI 전에 이미 약 34.6ms이므로 축소만으로 30~60FPS를 보장할 수 없다. 실제 연속 WB의 headless 관찰은 25.8회/초, photo-region hash 27개/Bitmap frame 32개, inter-frame gap p95 45.48ms였다. 중간 요청 합치기·16ms polling·capture·0 확인 대기를 포함하며 display FPS가 아니다.

설치된 LibRaw의 OpenMP 런타임 참조를 확인하고 새 프로세스에서 OMP_NUM_THREADS를 1/2/4/8로 비교했다. DNG SDK median은 각각 39.974/31.057/26.761/25.182ms였다. 스레드 수 축소를 채택하지 않았고 앱/전역 환경은 변경하지 않았다. [OpenMP 실험](verification/2026-10-11-area-coverage-omp.txt)은 해당 장비/SDK만의 결과다.

다음은 GPU 백엔드의 SDK/플랫폼/라이선스 기술 검증과 RGB16 축소/float 보정 kernel의 CPU 기준 오차 비교다. SDK 현상 자체의 비용, Windows/네이티브 화면, ICC/sensor-float 및 큰 RAW/장시간 메모리도 남아 있다.

## Verification

150개 geometry의 모든 float와 독립 area integral, 모든 65,536개 RGB16 값의 float 정규화 일치 검사를 유지했다. 새 native stage snapshot 6개와 실제 RAW full/half/수명/취소/연속 WB/0/품질 회귀를 포함해 529개 검사가 통과했다. Core와 RAW fixture hash가 일치하고 Release 빌드/앱 5초 시작이 통과했다.

```bash
dotnet run --project tests/PhotoLab.Imaging.Tests -c Release -- --rgb-preview-benchmark
dotnet run --project tests/PhotoLab.Desktop.Tests -c Release -- --native-preview-benchmark /path/to/bridge.dylib /path/to/photo.dng
```

[회귀 집계](verification/2026-10-11-area-coverage-results.json) · [회귀 및 단계 로그](verification/2026-10-11-area-coverage-tests.txt) · [변경 전/후 비교](verification/2026-10-11-area-coverage-benchmark.txt) · [빌드](verification/2026-10-11-area-coverage-build.txt) · [시작/해시](verification/2026-10-11-area-coverage-startup.json)
