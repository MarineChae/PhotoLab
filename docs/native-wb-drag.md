# Continuous actual-RAW WB input

CR2/CR3/NEF/ARW/RAF/DNG의 native opt-in 편집기에서 warm unpack cache를 유지한 채 60개 마우스 이동을 보냈다. 사진 영역 중앙 32×32 Skia frame hash와 Image Bitmap 교체를 관찰했다. UI label/thumb 변화는 사진 영역 hash에 포함하지 않는다. 조작 종료 뒤 전체 품질을 독립 LibRaw CLI reference와 byte 비교하고 단일 Undo도 확인했다.

## Reproduced fixes

1. 같은 사진의 WB 갱신이 `_afterBitmap`을 먼저 비워 새 렌더를 기다리는 동안 Original을 표시했다. 제어된 Image.Source 관찰로 재현했다. 이제 직전 accepted Bitmap을 유지하고 새 프레임이 준비됐을 때 기존 안전한 교체/해제 순서로 바꾼다.
2. 드래그 중 Warmth/Tint가 0이 되면 default RAW를 전체 재디코딩하고 1800px를 표시했다. native-preview 경로에 camera-default 600px float 캐시 하나를 유지해 즉시 복원한다. 조작 종료 후 CLI default 경로로 전체 품질을 갱신하므로 custom as-shot gains가 default 출력과 같다고 가정하지 않는다.
3. 현상 pump가 잠시 idle이면 포인터를 누른 상태에서도 WB 계수를 역산해 thumb를 다시 설정했다. tiny Tint 오차가 다음 입력에 섞여 Warmth 0에서 as-shot 복원이 실패했다. 실제 NEF 연속 입력에서 재현했고, 포인터를 누른 동안 정확한 입력값을 유지하도록 수정했다. release 시 accepted 상태를 동기화한다.

600px cache는 native-preview RAW에만 추가되며, 독립 float 소유권을 가진다. 새 사진/동일 경로의 파일 stamp 변경 시 성공한 로딩과 함께 교체하고 창 닫힘에서 해제한다. 기존 cache를 읽는 복사는 UI thread에서 동일 크기의 bounded memcpy로 수행해 close/switch와 pool 반환이 경합하지 않게 한다. RAW 현상과 resize는 worker에서 수행한다. 소스 파일과 Core는 변경하지 않는다.

## Observations

| RAW | 관찰 Bitmap/Skia frames | 사진 영역 hash 수 | 관찰률 / 초 | frame 간격 p95 |
| --- | --- | --- | --- | --- |
| CR2 | 49 | 34 | 36.6 | 45.46ms |
| CR3 | 39 | 28 | 30.6 | 44.80ms |
| NEF | 59 | 59 | 39.0 | 27.97ms |
| ARW | 58 | 48 | 40.0 | 31.70ms |
| RAF | 57 | 54 | 35.6 | 46.85ms |
| DNG | 26 | 19 | 21.0 | 63.08ms |

각 파일의 60개 입력 모두 slider 값이 바뀌었다. 실행 중 요청은 최신 값으로 합쳐져 모든 중간 WB를 현상하지 않는다. Bitmap 교체마다 Skia 프레임을 캡처하지만 quantization/사진 내용 때문에 중앙 영역 hash가 같은 경우도 있다. 관찰률은 서로 다른 색 프레임 수나 display FPS를 뜻하지 않는다.

16ms polling·frame capture 비용과 0 복원 확인 대기, 파일별 한 번의 실행을 포함한다. 입력-to-frame latency p95와 frame 간격 p95를 혼동하지 않는다. unpack cache는 이전 WB 검사에서 warm-up했다. 일부 최초 실행은 제어된 UI 회귀와 동시에 수행됐다. 네이티브 OS 화면 합성/FPS 및 지속적인 장시간 성능 증거가 아니다.

모든 파일에서 조작 중 600px 유지, 0에서 cached as-shot 표시, 종료 후 최신 custom WB CLI 픽셀 일치, 단일 Undo, 0으로 끝난 별도 드래그의 default CLI 전체 품질 일치를 통과했다. DNG의 관찰률은 30회/초에 못 미쳤다. 다음 최적화는 half RGB16→full half RGBA32F→600px 경로의 큰 중간 float 할당과 변환/resize 비용을 측정하고 줄이는 것이다. pixel 기준 일치를 유지한 뒤 SIMD/GPU 검증을 진행한다.

[검증 집계](verification/2026-10-11-native-drag-results.json) · [회귀 로그](verification/2026-10-11-native-drag-tests.txt) · [Original flash 재현](verification/2026-10-11-native-drag-flash-repro.txt) · [빌드](verification/2026-10-11-native-drag-build.txt) · [시작/해시](verification/2026-10-11-native-drag-startup.json)

전체 회귀: Core 16, Imaging 51, Desktop 206, UI 47, NativeSession 80, CachedLoader 45, NativeUI 72, 총 517개 통과. Release 빌드 성공 (기존 CS0618 2개), Core/fixture 해시 일치, native opt-in 앱 5초 시작 (stderr 없음)을 확인했다. Windows/GPU/ICC/장시간 메모리는 미검증이다.
