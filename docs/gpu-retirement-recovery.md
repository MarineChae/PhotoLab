# Failed GPU retirement and CPU recovery

GPU Process가 fence timeout/API error를 반환한 뒤 fallback이 같은 rendering worker에서 GPU Dispose를 호출했다. Vulkan Dispose는 자원을 안전하게 해제하기 위해 DeviceWaitIdle을 호출하므로, 드라이버 작업이 여전히 멈춰 있으면 CPU recovery까지 멈출 수 있었다.

제어된 GPU는 Process에서 실패하고 Dispose에서 retirement 신호를 기다린다. 변경 전 CPU frame이 정리 이전에 반환되지 않는 것을 재현했다. 이 테스트는 native driver를 실제로 hang시킨 것이 아니라 기존 호출 순서의 blocking 문제를 재현한 것이다.

이제 fallback은 실패한 GPU를 renderer에서 분리하고, failed 상태를 유지한 채 background task가 GPU의 cleanup을 소유하게 한다. CPU frame과 fallback close는 cleanup task를 기다리지 않는다. Vulkan 자체의 fence/idle→unmap/destroy 순서는 그대로라 pending GPU 자원을 조기에 파괴하지 않는다.

`RetiredGpuCleanup`으로 정리의 완료/실패를 관찰할 수 있다. stalled cleanup이 나중에 풀리면 완료되고, cleanup 예외는 해당 task에 남으면서 recovered CPU frame은 반환된다. 정상적으로 동작하는 현재 GPU의 dispose와 renderer reader→device ordering은 유지한다. failed GPU는 재시도하지 않는다.

실제 드라이버가 영구적으로 돌아오지 않는다면 cleanup task와 해당 device resources는 프로세스 수명 동안 남을 수 있다. 무확인 free/강제 device 파괴 대신 안전한 lifetime을 유지하는 선택이다. native call 자체가 timeout을 무시하거나 process를 중단시키는 경우까지 managed code가 복구한다고 주장하지 않는다. 실제 device loss/timeout·Windows·장시간 memory pressure는 여전히 검증 대상이다.

## Verification

- blocked failed-device cleanup 동안 CPU recovery 반환
- fallback close가 failed device를 retirement까지 유지
- retirement 뒤 cleanup completion 관찰
- cleanup exception이 CPU frame을 버리지 않고 task로 관찰됨
- 기존 adaptive backend, driver fallback, CPU/GPU parity/리소스/실제 RAW/editor 회귀 유지

[재현](verification/2026-10-11-gpu-retire-repro.txt) · [회귀](verification/2026-10-11-gpu-retire-tests.txt) · [집계](verification/2026-10-11-gpu-retire-results.json) · [빌드](verification/2026-10-11-gpu-retire-build.txt) · [해시](verification/2026-10-11-gpu-retire-hashes.json)

9개 프로젝트 Release 빌드, 총 657개 CPU/GPU/variant 검사 및 Core/fixture 해시가 통과했다. 활성 Khronos validation layer에서 50개를 반복했고 오류가 없었다. [Validation 로그](verification/2026-10-11-gpu-retire-validation.txt)를 참조한다.

Vulkan cleanup 조건은 [vkDestroyDevice](https://docs.vulkan.org/refpages/latest/refpages/source/vkDestroyDevice.html), [vkWaitForFences](https://docs.vulkan.org/refpages/latest/refpages/source/vkWaitForFences.html)의 Khronos 문서를 참고했다.
