# Vulkan compute probe

TASK-012의 기술 검증을 위해 독립 C# 도구 `tools/PhotoLab.GpuProbe`와 GLSL exposure compute shader를 추가했다. 앱의 기본 CPU 경로와 solution 의존성은 변경하지 않는다. Vulkan은 Windows 드라이버와 macOS MoltenVK 경로를 사용할 후보이며 아직 최종 backend 선택/완료는 아니다.

## Actual execution

macOS ARM64 26.5.2, Apple M4 IntegratedGpu, MoltenVK 1.4.2에서 실제 compute queue를 실행했다. 소프트웨어 CPU device는 선택하지 않는다. host-coherent 입력/출력 buffer, descriptor set, push constants, host→shader / shader→host barrier, fence 완료 후 readback과 핸들 해제를 구현했다.

1 / 37 / 240,000 pixels와 gain 0.0625 / 1 / sqrt(2) / 16의 12개 조합이 명시적 CPU exposure 계산과 최대 관찰 오차 0으로 일치했다. 음수 RGB와 1 초과 RGB, 작은 값, alpha 0/0.25/0.5/0.75/1, workgroup tail을 포함한다. relative/absolute 허용치는 1e-6이고 alpha는 정확히 비교한다. 현재 8개 전체 FloatImageProcessor control과의 비교는 아니다.

일반 실행의 240,000픽셀 전체 시간은 약 2.5~2.8ms였다. 할당/업로드/descriptor/submit/fence/readback/해제를 포함한 단일 관찰이며 pure GPU kernel 시간이나 CPU 대비 속도 개선을 뜻하지 않는다. pipeline/buffer 재사용과 앱 미리보기 연결은 다음 작업이다.

Vulkan loader 1.4.363.0 및 실제 VK_LAYER_KHRONOS_validation instance/device layer를 활성화해 12개 검사를 다시 실행했다. loader 로그에서 삽입을 확인했고 VUID/validation error가 없었다. SPIR-V도 `spirv-val --target-env vulkan1.1`을 통과했다.

이 장비는 shaderFloat64를 지원하지 않았다. 현재 CPU area resizer는 double weight/sum을 사용하므로 이를 그대로 GPU로 옮겼다고 주장할 수 없다. float GPU 축소의 허용 오차/누적 정밀도를 별도로 검증해야 한다.

macOS portability device 열거와 subset extension을 처리한다. 해당 device를 사용하는 경우 필요한 조건은 [Khronos portability enumeration 문서](https://docs.vulkan.org/refpages/latest/refpages/source/VK_KHR_portability_enumeration.html)를 기준으로 했다.

## Dependencies and license review

- Silk.NET.Vulkan 2.23.0: generated C# binding, [MIT license](https://github.com/dotnet/Silk.NET/blob/main/LICENSE.md). SDK struct를 직접 정의하는 위험을 줄인다.
- MoltenVK 1.4.2: macOS Vulkan→Metal runtime, [Apache-2.0](https://github.com/KhronosGroup/MoltenVK/blob/main/LICENSE).
- [Vulkan Loader](https://github.com/KhronosGroup/Vulkan-Loader/blob/main/LICENSE.txt) / [Validation Layers](https://github.com/KhronosGroup/Vulkan-ValidationLayers/blob/main/LICENSE.txt): Apache-2.0 및 명시된 더 permissive한 예외. loader/layer는 개발 검증용으로 사용했다.
- glslang 16.6.0 및 SPIR-V tools는 shader 빌드/검증 도구이며 앱에 링크/동봉하지 않는다. [glslang license 파일](https://github.com/KhronosGroup/glslang/blob/main/LICENSE.txt)은 구성요소별 BSD/MIT/Apache/GPL 내용을 포함한다. runtime distribution의 최종 notices/패키징은 아직 하지 않았다.

Homebrew prebuilt bottles로 개발 도구를 설치했다. 시스템/사용자 전역 GPU 환경 변수를 수정하지 않았고 validation 환경은 해당 실행 프로세스에만 지정했다. 런타임 C# binding은 독립 probe 프로젝트에만 추가했다.

## Reproduce

```bash
glslangValidator -V --target-env vulkan1.1 tools/PhotoLab.GpuProbe/exposure.comp -o tools/PhotoLab.GpuProbe/exposure.spv
spirv-val --target-env vulkan1.1 tools/PhotoLab.GpuProbe/exposure.spv
dotnet run --project tools/PhotoLab.GpuProbe -c Release -- /opt/homebrew/opt/molten-vk/lib/libMoltenVK.dylib
```

Windows에서는 설치된 `vulkan-1.dll`을 인자로 사용할 수 있도록 작성했지만 실제 Windows 빌드/실행은 미검증이다.

Validation 실행에 사용한 변수: DYLD_LIBRARY_PATH=/opt/homebrew/opt/vulkan-validationlayers/lib, VK_DRIVER_FILES=/opt/homebrew/opt/molten-vk/etc/vulkan/icd.d/MoltenVK_icd.json, VK_LAYER_PATH=/opt/homebrew/opt/vulkan-validationlayers/share/vulkan/explicit_layer.d, VK_INSTANCE_LAYERS=VK_LAYER_KHRONOS_validation. loader 인자는 /opt/homebrew/opt/vulkan-loader/lib/libvulkan.dylib이다.

[검증 집계](verification/2026-10-11-gpu-probe-results.json) · [일반 실행](verification/2026-10-11-gpu-probe-run.txt) · [활성 validation 실행](verification/2026-10-11-gpu-probe-validation.txt) · [빌드](verification/2026-10-11-gpu-probe-build.txt)
