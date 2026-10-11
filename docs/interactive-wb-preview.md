# TASK-011 — interactive WB와 idle 품질 갱신

검증일: 2026-10-11. 전문 RAW 편집기 전체 목표는 진행 중이다.

## 구현

IRawPreviewDeveloper는 전체 출력 치수를 가진 metadata와 작은 owned preview를 반환한다. SessionRawDecoder는 같은 unpack 세션에서 half-size 현상과 전체 현상을 선택하고, PhotoImageLoader는 600px interactive entry point를 제공한다. 반환한 화면 버퍼의 크기와 원본 출력 크기는 구분한다. 기존 디코더는 기존 품질 경로를 유지한다.

native opt-in 상태에서 같은 사진의 custom WB를 적용하면 half-size→600px 결과를 먼저 표시하고 편집 상태·히스토리를 한 번 갱신한다. 180ms idle 후 캡처한 WB로 전체 현상→1800px 품질 preview를 만든다. tone 입력은 별도로 소유한 600px 버퍼를 계속 사용하고 기존 timer와 최신 renderer 검사를 유지한다.

품질 작업은 사진 세대와 취소 토큰을 캡처한다. 새 WB/사진/닫힘은 이전 작업을 무효화한다. 늦게 반환된 preview는 해제하고 화면에 적용하지 않는다. 소스 stamp가 바뀌면 품질 승격을 거부하고 다시 열기를 요구한다. 교체 전에는 renderer를 drain해 이전 source reader가 끝난 뒤 버퍼를 해제한다.

이전 품질 작업 때문에 새 WB의 idle tick이 처리되지 못했으면 이전 작업 종료 후 최신 품질을 다시 예약한다. 새 WB나 사진을 읽는 작업이 실패하면 기존 사진의 pending 품질 상태를 유지하고 다시 예약한다. 품질 승격은 WB 값과 히스토리를 추가로 변경하지 않는다.

## 검증

[빌드](verification/2026-10-11-fast-wb-build.txt), [전체 로그](verification/2026-10-11-fast-wb-tests.txt), [집계](verification/2026-10-11-fast-wb-results.json): Core 16 + Imaging 51 + Desktop 206 + UI 26 + NativeSession 80 + CachedLoader 45 + NativeUI 24 = 448개 검사 통과. 기존 CS0618 경고 2개, 오류 0.

- 제어 가능한 비협조적 디코더로 이전 full 품질을 보류한 채 새 WB 입력/fast 화면 표시.
- 오래된 full 결과의 해제와 화면 보존, 최신 품질의 반영, 품질 승격의 히스토리 비변경성.
- 새 WB 실패 시 이전 fast 화면·WB 저장 상태 유지 및 기존 품질 재개.
- 품질 작업 도중 창 닫힘과 늦은 픽셀 해제.
- 실제 RAW 6종의 interactive loader가 독립 LibRaw -h reference와 일치하며 full metadata 치수/WB 보존.
- 실제 RAW 6종을 헤드리스 native opt-in 화면에 열고 WB 적용→600px 이하 첫 프레임→전체 품질 픽셀 일치→Original→Undo 확인.
- 기존 저장·취소·cache 수명·full/half native 현상과 원본 해시 회귀 유지.
- [앱 시작/해시](verification/2026-10-11-fast-wb-startup.json): native opt-in 앱 5초 정상 시작, Core 8개·fixture 7개 해시 일치. Core 소스는 수정하지 않았다.

## 범위와 다음 작업

빠른 경로는 PHOTOLAB_NATIVE_RAW_BRIDGE를 명시한 세션에서 사용한다. [설정/수명](native-cache-loader.md), [bridge 빌드/ABI/라이선스](native-raw-session.md)를 따른다. 현재 WB는 입력 후 Apply 버튼으로 적용하며 연속 WB 슬라이더/중립점/Kelvin은 미구현이다.

첫 half-size 프레임과 전체 품질 승격의 동작을 검증했지만 GUI 30~60FPS나 50ms 반응을 측정·증명한 것은 아니다. 다음은 연속 WB 제어와 요청 중복 제거, 화면 표시까지의 반응 시간·FPS 및 메모리 측정이다. 네이티브 OS 입력/Windows, sensor-float/ICC 색 정확도, GPU와 상용 배포는 남아 있다. TASK-011은 진행 상태를 유지한다.
