# Actual RAW GPU parity and measured backend selection

CR2/CR3/NEF/ARW/RAF/DNG의 동일한 as-shot linear float 입력에서 최대 1800px preview를 생성하고 CPU와 Vulkan에 같은 controls를 적용했다. neutral, exposure 1.5, 두 종류 combined controls의 24개 조합을 검사했다. 모든 float에서 기존 상대/절대 허용치 2e-5와 alpha exact를 확인한 뒤 같은 sRGB8 변환의 channel byte를 비교했다.

최대 관찰 float 오차는 2.38419e-7였다. neutral/exposure-only는 모든 샘플이 일치했다. combined controls 일부는 display channel 1단계 차이가 있었다: CR2 2 bytes, CR3 1, NEF 1, RAF 1; ARW/DNG는 차이 없었다. 모든 display 차이는 1 이하였고 완전히 일치했다고 표시하지 않는다. 현재 decoder/6개 camera-mode의 CPU 기준 비교이며 absolute color chart/ICC/sensor-float 정확도 증거는 아니다.

## Paired service performance

Apple M4 / Release, 실제 Pentax DNG의 같은 source/combined controls, warm CPU/GPU 후 순서를 번갈아 20쌍 처리했다.

| 입력 | CPU median / p95 | GPU median / p95 |
| --- | --- | --- |
| 600×399 | 1.593 / 1.714ms | 1.866 / 2.063ms |
| 1800×1198 | 15.609 / 16.821ms | 11.153 / 11.807ms |

GPU는 persistent source upload 상태에서 fence/readback/owned float를 포함한다. decoding/resize/sRGB 변환/Bitmap/UI는 제외한다. source별 warm upload, application peak memory와 다른 GPU의 crossover는 별도다. 측정 시 buffer capacity는 이전 source의 더 큰 capacity를 유지해 두 buffer의 논리적 합이 78,393,600 bytes였다; Vulkan/driver physical allocation과 같다고 가정하지 않는다.

600px는 CPU가 더 빠르고 1800px는 GPU가 더 빨랐다. opt-in `FallbackFloatProcessor`는 600×600 이하인 조작 preview를 CPU로 처리하며 초기 GPU 생성도 생략한다. 더 큰 품질 preview는 GPU를 시도한다. 이미 생성한 GPU는 CPU frame 중에도 유지해 다시 품질 처리에 사용한다. Name/status는 마지막 실제 processor를 나타낸다. 드라이버 초기화/실행 실패 이후에는 기존 영구 CPU fallback을 유지한다.

cutoff는 현재 앱의 600px 조작 크기를 기준으로 선택했다. 한 장비/한 DNG의 paired 측정만으로 모든 장비/크기의 crossover를 입증한 것은 아니다. 기본 backend는 계속 CPU이고 GPU는 PHOTOLAB_VULKAN_LIBRARY opt-in이다.

## Invariants

작은 preview가 GPU를 초기화하지 않는지, quality GPU→interactive CPU→quality GPU를 재개하는지 검사했다. 큰 source에서 missing driver / execution error를 재현해 fallback 및 해제를 검증한다. invalid NaN controls는 upload 전에 실패하고 float overflow는 CPU처럼 ArithmeticException으로 거부한다. cancellation, buffer/source 재사용·큰 dispatch·close reader ordering도 기존 회귀에 포함된다.

실제 device-loss/timeout/비정상 드라이버 종료, Windows/다른 GPU, 장시간 메모리, display/ICC와 큰 RAW modes는 미검증이다. native SDK/area resize 비용은 이 control shader의 범위 밖이므로 DNG 전체 WB FPS 문제를 해결했다고 주장하지 않는다.

```bash
dotnet run --project tests/PhotoLab.Rendering.Tests -c Release -- --raw /path/to/libMoltenVK.dylib /path/to/photo.dng
```

[집계](verification/2026-10-11-gpu-raw-results.json) · [실제 RAW 및 paired 비교 로그](verification/2026-10-11-gpu-raw-tests.txt) · [빌드](verification/2026-10-11-gpu-raw-build.txt) · [해시](verification/2026-10-11-gpu-raw-hashes.json)

9개 프로젝트 Release 빌드 및 총 649개 CPU/GPU/variant 검사, Core/RAW fixture 해시 일치를 확인했다. 활성 Khronos validation layer의 46개 반복 검사도 오류 없이 통과했다. [Validation 로그](verification/2026-10-11-gpu-raw-validation.txt)를 참조한다.
