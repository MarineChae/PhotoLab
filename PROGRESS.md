# PhotoLab Development Progress

업데이트: 2026-10-11 (Asia/Seoul)

## Current Goal

Windows/macOS 전문 RAW 편집기: 고정밀·비파괴 처리, CPU/GPU 실시간 보정, 색상 관리 및 사진 관리. 최종 목표는 아직 달성하지 않았다.

## Current Task

TASK-026 폴더 탐색: worker의 candidate discovery와 Open folder/list/이전/다음 이동을 연결했다. 빠른 연속 선택·손상 파일·빈/없는 폴더·obsolete scan을 검증했고, accepted 보정값/WB를 source-stamped in-window draft로 보관한다. 썸네일/SQLite catalog, Windows/clean-machine 배포, 고급 보정·모니터/LUT, native picker와 GPU 장시간/전체 앱 메모리/성능은 남아 있다.

## Verified Work

- Folder 관련 Core 16, Imaging 66, Rendering 9, Desktop 104, Presets 21, PhotoFolder 7, CPU UI 68, Vulkan UI 69, 합계 360개. 2,000개 filename-only discovery와 실제 PNG headless 첫 사진/다음/빠른 선택/손상 실패/보정 복원/폴더 실패·빈 폴더·supersession 검증. 업데이트 macOS ARM64 app startup 및 Core 소스 8개 해시 유지.
- Preset 관련 Core 16, Imaging 66, Rendering 9, Desktop 104, Presets 21, ColorProfile 9, CPU UI 60, Vulkan UI 61, 합계 346개. Unicode/path-safe identity, strict schema/version/range·중복/취소/삭제, 실제 UI save/preview/cancel/apply/Undo/Redo/다른 사진/delete와 WB 유지 검증. macOS ARM64 app publish/startup 확인, Core 소스 8개 해시 유지.
- Streaming 관련 Core 16, Imaging 66, Rendering 9, Desktop 104, ColorProfile 9, LittleCMS 32, PNG16Input 32, PNG16Export 33, UI 52, 합계 353개. byte/sidecar atomic store와 실제 RAW 6종 회귀, 동일 PNG bytes, partial-write failure·mid-stream cancellation·임시 파일 정리 통과. macOS ARM64 app/native package의 streaming entry/signature/startup 확인.
- 20MP 별도 process 비교 B/S/S/B 4회: 모두 PNG 136,403,465 bytes 및 같은 SHA-256. managed encode 할당 296,404,568→66,920 bytes, 시간 buffered 7.764–7.935s / streamed 7.823–7.867s. process peak RSS 860.9–861.4MB / 740.0–743.2MB이며 입력 생성·runtime/native overhead를 포함한다. full editor/RAW decode 메모리 또는 JPEG 목표 검증이 아니다.
- Histogram 관련 Core 16, Imaging 66, Rendering 9, Desktop 104, ColorProfile 9, CPU UI 52, Vulkan UI 53, 합계 309개 통과. full Release 빌드 성공, 기존 CS0618 경고 2 유지. captured Skia histogram 영역의 RGB 곡선 픽셀·accepted WB frame 갱신을 확인했다. 600×400 float+histogram+RGBA8 service는 60개 요청 모두 입력 중 수락, 55.6회/초 및 accepted p95 7.28ms였다. OS 화면 FPS와 구분한다.
- PNG16 출력 검증: Core 16, Imaging 57, Rendering 9, Desktop 102, ColorProfile 9, LittleCMS 32, PNG16Input 32, PNG16Export 28, UI 49, 합계 334개. 출력 묶음은 실제 RAW 6종과 원본 hash를 포함한다. 모든 65,536 RGB/alpha 코드의 정확한 파일 왕복과 보정 후 double 기준 1코드 이내를 확인했다. 네이티브 encode 행 처리 중 controlled 취소와 갱신 package의 encoder entry/signature/startup도 별도 확인했다. OS 저장 dialog·Windows·20MP memory/performance는 아직 미검증이다.
- 배포 연결 검증: relocated published host의 Bundled 9, ColorProfile 9, LittleCMS 32, PNG16 32, Desktop 102, UI 47, 합계 231개. DYLD 실제 로드가 모두 옮긴 package 내부 경로임을 확인했고, staged dylib 3개의 아키텍처/서명·상대 의존성·license/hash 및 실제 publish 앱 시작을 확인했다. macOS ARM64/.NET 10, framework-dependent이며 Windows·깨끗한 장비·상용 installer를 검증한 것은 아니다.
- PNG16 변경 관련 검증: Core 16, Imaging 57, Rendering 9, Desktop 102, ColorProfile 9, LittleCMS 32, PNG16 32, UI 47, 두 native 설정의 UI 47, 합계 351개. 네이티브 행 처리 중 controlled 취소는 별도 확인했다. libpng 1.6.59/macOS ARM64, C bridge -Wall/-Wextra/-Werror 빌드와 전체 .NET Release 빌드 성공. Core 소스 8개 해시 유지. RAW/GPU native 묶음은 코드 변경이 없어 재실행하지 않았다.
- LittleCMS 초기 연결 검증 (이전 기록): Core 16, Imaging 57, Rendering 9, Desktop 102, 기존 ColorProfile 9, LittleCMS 32, UI 47, LittleCMS UI 47, 합계 319개. 기존 666개 집계와 다른 범위이며 RAW/GPU native 묶음은 코드 변경이 없어 재실행하지 않았다. LittleCMS 2.19.1/macOS ARM64, 전체 Release 빌드 오류 0·기존 경고 2. Core 소스 해시 유지.
- macOS ARM64 / .NET SDK 10.0.102에서 9개 프로젝트 Release 빌드 통과. 기존 CS0618 경고 2개 유지.
- Core 16, Imaging 57, Rendering 9, Rendering GPU 50, Raw GPU 24, ColorProfile 9, Desktop 209, CPU UI 47, Vulkan UI 48, NativeSession 80, CachedLoader 45, adaptive NativeUI 72, 합계 666개 검증 통과. CPU/GPU variant 반복을 포함한다.
- 활성 Khronos validation layer에서 50개 반복 통과 (VUID/driver errors 없음). timeout retirement는 controlled GPU로 검증했으며 실제 hardware hang 시험과 구분한다.
- 앱 시작 후 5초 동안 프로세스 유지, stdout/stderr 오류 없음. 전체 UI 입력 검증과 구분한다.
- 원본/심볼릭 링크 보호, 하드 링크 목적지 교체 시 원본 보존, 취소 시 기존 출력 보존.
- 버전 1 JSON 저장·복원, 기존 reader/레거시 호환, 취소 시 기존 JSON 보존, 잘못된 값/버전 거부.
- 최신 렌더 버전 검사·직렬 CPU 처리·캐시 미리보기·드래그 히스토리 통합 및 128개 상태 제한.
- 선형 sRGB RGBA32F 처리와 RAW 16비트 입력을 앱 미리보기/내보내기에 연결. Core 소스 해시 유지.
- 처리 단계 분리 후 기존 결과 유지, 비가환 처리 순서, 10회 float 결과 재현성 통과.
- RAW 인터페이스 교체, 소유권 이전/해제, 늦은 디코더 취소, 기존 API 호환 테스트 통과.
- raw-identify 제조사·모델·ISO·셔터·조리개 추출과 UI 표시 연결 구현. 보조 도구 누락/timeout은 경고로 기록하고 편집을 유지한다. 실제 UI 조작 확인은 남아 있다.
- LibRaw C getter로 WB 4채널 계수와 3×4 색행렬을 float 정밀도로 추출. 배열 소유권·유한값·치수 검증 및 Nikon 기준값·Sony 미상 조리개·DNG 네 번째 채널 부재 검증 통과. 기존 sRGB 픽셀에 재적용하지 않는다.
- 사용자 WB는 -r/+M을 사용해 원본 RAW 현상에 적용. 실제 6종에서 독립 LibRaw reference와 모든 float 샘플의 차이 0, 앱 WB 미리보기·전체 해상도 출력·원본 해시 보존 통과. UI의 상대 Temperature/Tint는 아직 새 경로와 연결하지 않았다.
- WB 포함 version 2 저장·복원과 전체 상태 히스토리 구현. 구형 API의 WB 손실 방지·동시 저장 20회·실제 RAW 6종 재개 픽셀 일치 통과. 헤드리스 Original/저장/재개·Undo/Redo 입력을 확인했으며 네이티브 OS 입력은 남아 있다.
- 상대 카메라 Warmth/Tint 및 Apply WB/As shot UI 구현. Imaging에서 gain 계산·저장 비율 역산·미표현 값 보존. 실제 RAW 6종에서 50/20 조절값 복원과 독립 native 출력 일치 통과.
- 헤드리스 실제 TextBox/마우스 이벤트로 WB·Original·저장/재개·As shot·Undo/Redo·실패/닫힘 17개 검증 통과. Skia frame 생성 및 UI 배치 검토. WB refresh에서 화면이 disposed bitmap을 참조하는 오류를 재현·수정했다. 네이티브 OS 입력과 구분한다.
- native SDK 연결 계층과 NativeRawSession: 원본 unpack 데이터 유지, source stream 해제, 전체/half WB 현상 및 RGB16→float. 실제 6종에서 소스 복사본 삭제 후 반복과 full/half CLI 기준 차이 0 검증.
- bridge를 명시하면 앱 로더의 custom WB/전체 로딩에 세션을 공유. Original bitmap 재사용, stamp 변경/다시 열기/닫힘 및 늦은 session/pixel 해제 검증. 실제 RAW 6종의 native opt-in 헤드리스 화면 픽셀이 CLI와 일치했다.
- 같은 사진의 custom WB는 half-size→600px 먼저 표시, 180ms idle 후 full→1800px 승격. 이전 품질 폐기·새 요청 실패 후 기존 품질 재개·히스토리 유지·닫힘을 제어 검증했다. 실제 RAW 6종의 fast loader/첫 화면/품질 결과 일치 통과.
- native 경로의 카메라 WB 슬라이더는 현상 중 입력을 받고 최신 대기 값 하나만 유지한다. 드래그 프레임·단일 Undo/Redo·사진 전환/닫힘의 늦은 출력 해제·미표현 WB 보존을 검증했다. 실제 6종에서 드래그 중 표시/idle 품질 CLI 일치/단일 Undo 통과. 헤드리스 첫 프레임 관찰은 103~162ms이며 네이티브 OS 표시/FPS와 구분한다.
- 미리보기/Original은 PNG 왕복 대신 표시 RGBA8를 immutable Skia Bitmap으로 복사한다. RGBA/odd stride/alpha 및 배열 변경·GC 후 소유권, 이전 불투명 PNG 픽셀 일치 검증을 추가했다. 실제 RAW 6종의 WB/Original/드래그/idle 픽셀 회귀도 통과했다.
- 실제 Exposure mouse drag에서 60개 changed input과 사진 영역 프레임, 600px 조작, idle 최종 품질/저장/단일 Undo/Redo를 검증했다. Image 참조 교체와 캡처한 Skia 창의 사진 영역 픽셀을 함께 확인하므로 label/thumb 변화만을 세지 않는다.
- 실제 6종에서 연속 WB 60개 입력, 조작 중 600px·사진 영역 hash 변화, 최신 CLI 전체 품질, 단일 Undo와 0에서 cached as-shot/idle default 품질을 검증했다. Original을 잠깐 표시하던 refresh를 제어 재현해 accepted frame 유지로 수정했고 idle pump가 포인터 입력값을 역산 오차로 바꾸지 않게 했다.
- native RGB16을 직접 area 축소하고 입력 포인터는 native 이미지 수명 내에서만 빌려 읽는다. 큰 half RGBA32F 중간 버퍼와 RGB16 managed 복사 없이 owned float를 반환한다. 150 geometry의 모든 float와 독립 area reference 일치, 실제 6종의 full/half·수명/취소·cached loader·연속 WB 픽셀 회귀 통과.

## Real RAW Verification

CC0 출처/모드/SHA-256은 tests/fixtures/raw/manifest.json을 따른다. 각 파일의 256개를 넘는 red 채널 레벨, float 미리보기, 전체 해상도 JPEG 재디코딩, 원본 해시 보존을 확인했다. 아래 시간은 기본/사용자 WB 현상, 앱 미리보기, 독립 reference 및 두 경로의 전체 해상도 내보내기를 포함한 테스트 묶음이다. 단일 RAW 로딩 시간과 구분한다.

- RAW: cr2.cr2 1944x1296, metadata/preview/full-export 1.04s
- RAW: cr3.cr3 3407x2271, metadata/preview/full-export 2.01s
- RAW: nef.nef 2482x1648, metadata/preview/full-export 1.44s
- RAW: arw.arw 2784x1872, metadata/preview/full-export 1.60s
- RAW: raf.raf 2012x1521, metadata/preview/full-export 0.96s
- RAW: dng.dng 4309x2868, metadata/preview/full-export 3.56s

카메라 식별 결과:

- CAMERA: cr2.cr2 Canon EOS 40D
- CAMERA: cr3.cr3 Canon EOS R6
- CAMERA: nef.nef Nikon D2H
- CAMERA: arw.arw Sony ILCE-7S
- CAMERA: raf.raf Fujifilm S5000
- CAMERA: dng.dng Pentax K-r

추가 Samsung .dng 샘플은 실제 JPEG 페이로드라 거부됐다. 카메라 전체의 미지원 판정이 아니라 잘못된 확장자 파일에 대한 음성 테스트로 보존했다.

## Performance Evidence

실제 DNG 20쌍 같은 combined controls 비교: 600×399 CPU median/p95 1.593/1.714ms, GPU 1.866/2.063ms; 1800×1198 CPU 15.609/16.821ms, GPU 11.153/11.807ms. source upload는 cached, GPU fence/readback/owned float 포함, decode/resize/display는 제외한다. 현재 600×600 이하 조작은 CPU를 사용하고 큰 품질 preview만 opt-in GPU를 시도한다. 한 장비/한 source의 결과이며 SDK WB 비용과 분리한다.

RGB16 직접 변환/축소 10쌍 비교: 2154×1434→600 median 14.646ms/49,421,480 allocated bytes → 9.493ms/40 bytes; 3000×2000 21.829ms/96,000,104 → 10.521ms/40. 관리 입력과 warm output pool을 사용했으며 native 현상/입력 복사/UI는 제외한다. buffer footprint가 40 bytes라는 뜻은 아니다.

현재 실제 RAW WB warm-cache 관찰률: CR2 34.9, CR3 35.5, NEF 39.6, ARW 37.4, RAF 33.7, DNG 24.1회/초. DNG frame 간격 p95 44.62ms. 이전 아래 기록과 paired 비교가 아니며 아직 30회/초에 못 미친다.

실제 RAW WB warm-cache 헤드리스 관찰: CR2 36.6, CR3 30.6, NEF 39.0, ARW 40.0, RAF 35.6, DNG 21.0회/초. Bitmap 교체+Skia 프레임 캡처 횟수이며 서로 다른 중앙 사진 영역 hash 수는 각각 34/28/59/48/54/19였다. 16ms polling/capture/0 확인 대기를 포함하며 OS FPS나 입력 latency가 아니다. DNG의 frame 간격 p95는 63.08ms로 최적화가 필요하다.

실제 EditorWindow + 합성 1800×1200 회색 PNG / 600×400 조작: 60개 changed input / 60개 서로 다른 사진 프레임을 1.285초 동안 관찰, 46.7회/초, observation p95 24.04ms. 마우스 입력·Image 교체·Skia 창 캡처를 포함하며 16ms polling/capture overhead가 있다. 네이티브 OS 합성 FPS/정확한 display latency 및 실제 RAW scene 성능과 구분한다.

표시 비용 20쌍의 합성 RGBA8 비교: 600×400 PNG encode/decode median 4.527ms → 직접 Bitmap 0.425ms, 1800×1200 45.650ms → 3.868ms. float 처리와 OS 표시는 제외한다.

600×400 float 보정/표시 변환/Bitmap 생성 60개 요청: 입력 중 60개 bitmap, 58.9회/초, accepted p95 5.79ms. 실제 Editor 이미지 배치/화면 합성이 없으므로 UI FPS가 아니다. 1800×1200은 입력 중 새 요청의 취소로 accepted 결과 0개, 종료 뒤 최종 요청이 처리됐다 (p95 38.33ms). 600px 조작/idle 품질 구조를 유지한다.

이전 float+PNG 서비스 관찰은 58.6회/초/p95 10.27ms였으며 별도 실행·범위의 결과다. 이번 paired 표시 경로 비교와 구분한다.

## Open Verification / Missing Features

- Windows/macOS Intel 빌드·실행, 네이티브 UI 드래그/닫힘/이미지 전환/카메라 표시 검증.
- GPU/SIMD 백엔드 및 CPU/GPU 결과 비교.
- 카메라 WB의 네이티브 OS 입력/파일 선택기·카메라 데이터 실시간 캐시·중립점/Kelvin, 색행렬 적용 정확도, 기타 EXIF/렌즈 메타데이터, ICC/모니터 변환, 추가 압축 모드 및 색 정확도 기준 이미지.
- Windows 및 clean-machine native 배포, 추가 고정밀 포맷·EXIF/ICC 보존 및 전체 프로파일 기준 검증. PNG16 내보내기는 연결/실파일 검증했으며 OS 저장 dialog와 대용량 메모리/성능 검증은 남아 있다.
- 전체 앱 메모리 사용량/누수, 20MP 이상 성능 및 UI FPS.
- 사진 썸네일/캐시·SQLite catalog·대규모 탐색 성능, HSL/커브/그레이딩/디테일/마스킹 및 상용 배포 품질. Basic folder list/preset은 연결했으며 session draft의 영구 저장·사진별 history 유지·Windows/OS 검증은 남아 있다. Histogram full-resolution/고급 preset도 미완료다.
- 자동 저장·마지막 사진 자동 재개, 기존 RAW 편집값의 시각적 호환성 검증.

## Status Policy

이전 TASK-001~005 완료 및 Build/Tests PASS 기록만으로 전체 완료를 증명할 수 없었다. TASKS.md의 [~] 상태와 위 검증 범위를 사용한다. 플랫폼/UI/색상 검증이 남은 작업을 [x]로 표시하지 않는다.

## Evidence

- [폴더 탐색/선택/session draft 검증](docs/photo-folder-navigation.md)
- [폴더 관련 360개 집계](docs/verification/2026-10-11-folder-results.json)
- [Basic preset 저장/preview/다른 사진 적용](docs/basic-presets.md)
- [Preset 관련 346개 집계](docs/verification/2026-10-11-presets-results.json)
- [행 단위 PNG16/원자적 저장/20MP 비교](docs/png16-streaming.md)
- [Streaming 353개 회귀](docs/verification/2026-10-11-png-stream-results.json)
- [실시간 float histogram/UI 검증](docs/live-histogram.md)
- [309개 histogram 관련 집계](docs/verification/2026-10-11-histogram-results.json)
- [PNG16 float 출력/UI/실제 RAW 검증](docs/png16-export.md)
- [출력 관련 334개 집계](docs/verification/2026-10-11-png16-export-results.json)
- [자동 color runtime 및 macOS 배포 검증](docs/color-runtime-package.md)
- [231개 relocated publish 검증](docs/verification/2026-10-11-color-package-results.json)
- [PNG16 원본 코드 보존/편집 연결](docs/png16-input.md)
- [PNG16 관련 351개 집계](docs/verification/2026-10-11-png16-results.json)
- [LittleCMS 원본 ICC 입력/수명 검증](docs/littlecms-input.md)
- [이번 관련 회귀 집계](docs/verification/2026-10-11-lcms-results.json)
- [Source-profile float input/sRGB export](docs/profile-input-srgb-export.md)
- [색상 입력/출력 집계](docs/verification/2026-10-11-color-input-results.json)
- [Failed GPU retirement/CPU recovery](docs/gpu-retirement-recovery.md)
- [Retirement 회귀 집계](docs/verification/2026-10-11-gpu-retire-results.json)
- [실제 RAW GPU parity/선택 기준](docs/raw-gpu-parity.md)
- [RAW GPU 집계](docs/verification/2026-10-11-gpu-raw-results.json)
- [Persistent GPU controls/preview/fallback](docs/gpu-float-controls.md)
- [GPU controls 검증 집계](docs/verification/2026-10-11-gpu-controls-results.json)
- [GPU controls 활성 validation](docs/verification/2026-10-11-gpu-controls-validation.json)
- [C# Vulkan 실제 GPU 검증/라이선스](docs/gpu-compute-probe.md)
- [GPU probe 집계](docs/verification/2026-10-11-gpu-probe-results.json)
- [Area footprint 최적화/SDK 단계 측정](docs/area-coverage-profile.md)
- [Area 검증 집계](docs/verification/2026-10-11-area-coverage-results.json)
- [직접 RGB16 변환/축소 보고서](docs/direct-rgb16-preview.md)
- [RGB16 검증 집계](docs/verification/2026-10-11-rgb-preview-results.json)
- [RGB16 변환/축소 벤치마크](docs/verification/2026-10-11-rgb-preview-benchmark.txt)
- [실제 RAW 연속 WB/frame 및 재현 수정](docs/native-wb-drag.md)
- [RAW 드래그 검증 집계](docs/verification/2026-10-11-native-drag-results.json)
- [RAW 드래그 검증 로그](docs/verification/2026-10-11-native-drag-tests.txt)
- [실제 편집기 frame/input 검증](docs/editor-frame-input.md)
- [편집기 frame 회귀 집계](docs/verification/2026-10-11-editor-frames-results.json)
- [편집기 frame 회귀 로그](docs/verification/2026-10-11-editor-frames-tests.txt)
- [직접 미리보기 표시/비용 보고서](docs/direct-preview-display.md)
- [직접 표시 검증 집계](docs/verification/2026-10-11-direct-preview-results.json)
- [직접 표시 벤치마크](docs/verification/2026-10-11-direct-preview-benchmark.txt)
- [연속 WB 제어 보고서](docs/continuous-wb-controls.md)
- [연속 WB 검증 집계](docs/verification/2026-10-11-continuous-wb-results.json)
- [연속 WB 검증 로그](docs/verification/2026-10-11-continuous-wb-tests.txt)
- [Interactive WB/idle 품질 보고서](docs/interactive-wb-preview.md)
- [Fast WB 검증 집계](docs/verification/2026-10-11-fast-wb-results.json)
- [Fast WB 검증 로그](docs/verification/2026-10-11-fast-wb-tests.txt)
- [Native cache 로더/화면 연결 보고서](docs/native-cache-loader.md)
- [Cache 로더 검증 집계](docs/verification/2026-10-11-cache-loader-results.json)
- [Cache 로더 검증 로그](docs/verification/2026-10-11-cache-loader-tests.txt)
- [Native RAW session 보고서](docs/native-raw-session.md)
- [Session 테스트 집계](docs/verification/2026-10-11-session-results.json)
- [Session 테스트 로그](docs/verification/2026-10-11-session-tests.txt)
- [편집기 입력 회귀/bitmap 수명 보고서](docs/editor-input-tests.md)
- [입력 검증 집계](docs/verification/2026-10-11-ui-results.json)
- [입력 검증 로그](docs/verification/2026-10-11-ui-tests.txt)
- [상대 카메라 WB 제어 보고서](docs/camera-wb-controls.md)
- [WB 제어 테스트 집계](docs/verification/2026-10-11-wb-controls-results.json)
- [WB 제어 테스트 로그](docs/verification/2026-10-11-wb-controls-tests.txt)
- [WB 편집 상태 저장·재개 보고서](docs/photo-edit-state.md)
- [상태 저장 테스트 집계](docs/verification/2026-10-11-state-results.json)
- [상태 저장 테스트 로그](docs/verification/2026-10-11-state-tests.txt)
- [사용자 카메라 WB 현상 보고서](docs/raw-white-balance.md)
- [WB 빌드 로그](docs/verification/2026-10-11-wb-build.txt)
- [WB 테스트 로그](docs/verification/2026-10-11-wb-tests.txt)
- [WB 테스트 집계](docs/verification/2026-10-11-wb-results.json)
- [RAW 촬영·색 정보 보고서](docs/raw-color-metadata.md)
- [촬영·색 정보 테스트 로그](docs/verification/2026-10-11-color-tests.txt)
- [촬영·색 정보 집계](docs/verification/2026-10-11-color-results.json)
- [시작·Core/샘플 해시 검증](docs/verification/2026-10-11-color-startup.json)
- [이전 디코더/파이프라인 보고서](docs/decoder-pipeline.md)
- [이전 디코더 테스트 로그](docs/verification/2026-10-11-decoder-tests.txt)
- [이전 디코더 집계](docs/verification/2026-10-11-decoder-results.json)
- [이전 개발 보고서](docs/autonomous-development.md)
- [작업 목록](TASKS.md)
