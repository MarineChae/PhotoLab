# Persistent Vulkan float controls

`PhotoLab.Rendering`에 owned RGBA32F를 반환하는 Vulkan processor와 CPU fallback을 추가했다. Imaging의 `IFloatImageProcessor`와 `FloatAdjustments`는 CPU/GPU가 같은 validation, clamp와 gain 준비를 사용하게 한다. Core는 수정하지 않았다.

shader는 기존 linear light/WB → highlight/shadow → contrast → saturation/vibrance 순서를 따른다. 제어값은 CPU에서 float로 준비해 push constants로 전달하며 shader에는 불필요한 RGB clipping과 8비트 변환이 없다. neutral 상태는 모든 source sample을 그대로 복사한다. GLSL precise를 사용해 contraction을 제한했고, 실제 CPU/GPU float 오차는 별도로 검사한다.

## Buffers and lifetime

- pipeline/layout/descriptor/command/fence를 processor 수명 동안 유지한다.
- 두 host-coherent buffer는 capacity가 부족할 때만 교체한다. 작은 사진으로 전환해도 큰 capacity를 재사용한다.
- immutable FloatImageBuffer identity가 같으면 원본 업로드를 반복하지 않는다. 매 호출 source lifetime은 확인한다. GPU readback은 독립 owned float이므로 다음 frame과 충돌하지 않는다.
- gate로 모든 GPU 호출을 직렬화하고 host/shader barrier와 fence를 사용한다. 취소된 제출도 완료를 기다린 뒤 결과를 폐기한다. Vulkan API 오류 후 processor는 재사용하지 않는다.
- renderer가 processor를 소유하며 close는 현재 reader 종료 후 worker에서 device를 해제한다. 제어된 non-cooperative worker로 폐기/해제 순서를 검사했다.
- 초기화/driver failure는 device를 해제하고 해당 renderer에서 CPU를 선택한다. cancellation/잘못된 edit/수명 오류는 GPU 실패로 숨기지 않는다.

storage-buffer/dispatch 한도를 확인하고 2차원 dispatch를 지원한다. buffer capacity는 논리적 data 용량이며 Vulkan allocation alignment와 driver 전체 메모리 사용량은 별도다.

## Opt-in app integration

`PHOTOLAB_VULKAN_LIBRARY`가 지정된 preview만 GPU를 시도한다. 초기화는 rendering worker에서 수행한다. 상태 표시의 CPU/Vulkan은 실제 선택된 processor를 따른다. 내보내기는 기존 CPU float 경로를 사용한다. Windows 실행/전체 요구사항 검증이 남아 있어 기본 backend는 CPU다.

후속 [실제 RAW paired 측정](raw-gpu-parity.md)에 따라 현재 600×600 이하의 조작 preview는 GPU를 초기화하지 않고 CPU로 처리한다. 큰 품질 preview는 GPU를 유지·재사용한다. 아래 618개 검사는 도입 단계의 역사 기록이며 최신 649개 및 실제 RAW 다양한 controls 비교는 후속 보고서를 따른다.

```bash
PHOTOLAB_VULKAN_LIBRARY=/opt/homebrew/opt/molten-vk/lib/libMoltenVK.dylib dotnet run --project src/PhotoLab.Desktop -c Release
dotnet run --project tests/PhotoLab.Rendering.Tests -c Release -- /opt/homebrew/opt/molten-vk/lib/libMoltenVK.dylib
dotnet run --project tests/PhotoLab.Desktop.Tests -c Release -- --ui-vulkan /opt/homebrew/opt/molten-vk/lib/libMoltenVK.dylib
```

shader 재생성: `glslangValidator -V --target-env vulkan1.1 src/PhotoLab.Rendering/Shaders/develop.comp -o src/PhotoLab.Rendering/Shaders/develop.spv`. 생성 SPIR-V를 embedded resource로 배포해 runtime compiler를 필요로 하지 않는다. 바인딩/런타임 라이선스 검토는 [GPU probe 보고서](gpu-compute-probe.md)를 따른다.

## Evidence and limits

Apple M4에서 neutral, 각 control 양 끝값과 combined control 10개를 포함해 27개 설정을 검사했다. 허용치는 `abs(error) <= 2e-5 * max(1, abs(reference))`, alpha는 exact이며 최대 관찰 float 오차는 1.90735e-6였다. 음수/HDR RGB, alpha 0/0.25/0.5/0.75/1과 tail을 포함한다. neutral/hidden RGB는 exact copy였다.

같은 source의 반복 controls는 buffer allocation 2개 / source upload 1번이었다. A/B/A, 1800×1200 growth와 capacity reuse, 4.41MP 2차원 dispatch, disposal/pre-cancellation을 검사했다. 257×193의 warm 60개 controls는 약 33~37ms 관찰이었고 fence/readback/owned float를 포함한다. 큰 이미지 성능이나 UI FPS로 해석하지 않는다.

실제 editor headless 입력에서 Vulkan 선택을 직접 assert하고 frame/idle 품질·저장/Undo/Redo·실패·전환·닫힘을 검사한다. 실제 RAW 6종 native UI의 Vulkan neutral 렌더도 기존 CLI 픽셀과 비교한다. 활성 Khronos validation layer에서 GPU controls/resource 검사를 반복해 VUID/driver errors를 확인한다.

GPU precision, display/ICC, 장시간/다중 device, Windows, device loss/timeout 하드웨어 시나리오와 render 성능의 CPU/GPU paired 비교는 남아 있다. 현재 shader는 LibRaw의 센서 현상이나 CPU double area resize를 GPU로 옮기지 않으므로 DNG SDK 병목을 해결한 것으로 주장하지 않는다.

Core 16 / Imaging 57 / Rendering 4 / GPU 40 / Desktop 209 / CPU UI 47 / Vulkan UI 48 / NativeSession 80 / CachedLoader 45 / Vulkan NativeUI 72, 총 618개 검사 통과. CPU/GPU variant 반복을 포함한다. 9개 프로젝트 Release 빌드 (기존 CS0618 2개), Core/fixture hash, 두 native 환경을 지정한 앱 5초 시작도 통과했다. 활성 validation에서 40개 GPU/resource 검사를 반복했고 SPIR-V 검증도 통과했다.

[회귀 집계](verification/2026-10-11-gpu-controls-results.json) · [회귀 로그](verification/2026-10-11-gpu-controls-tests.txt) · [활성 validation](verification/2026-10-11-gpu-controls-validation.txt) · [빌드](verification/2026-10-11-gpu-controls-build.txt) · [시작/해시](verification/2026-10-11-gpu-controls-startup.json)
