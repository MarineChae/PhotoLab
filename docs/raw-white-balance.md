# TASK-011 — 카메라 공간 사용자 WB 현상 경로

검증일: 2026-10-11. 전체 전문 편집기 목표는 진행 중이다.

## 구현

IRawDeveloper는 원본 RAW와 4채널 CameraWhiteBalance를 받아 선형 sRGB float 결과를 소유권과 함께 반환한다. 기존 IRawDecoder의 기본 카메라 WB 동작은 유지한다. LibRawDecoder가 두 인터페이스를 구현하며 PhotoImageLoader의 미리보기/전체 해상도 overload가 이 기능을 사용한다. 미지원 디코더나 일반 JPEG/PNG에 사용자 카메라 WB를 요청하면 조용히 무시하지 않고 명확히 거부한다. Core 파일은 변경하지 않았다.

현상에는 `-r mul0 mul1 mul2 mul3 +M -o 1 -4`를 사용한다. 사용자 계수는 선형 sRGB가 되기 전 LibRaw scale_colors에 적용된다. `-w`를 함께 사용하면 scale_colors가 user_mul을 카메라 계수로 덮어쓰므로 사용자 경로에서는 제외한다. `+M`은 WB 선택과 독립적으로 내장 카메라 색 데이터를 사용한다. 이 동작과 순서는 [dcraw_emu 옵션 소스](https://raw.githubusercontent.com/LibRaw/LibRaw/0.22.2/samples/dcraw_emu.cpp), [scale_colors 소스](https://raw.githubusercontent.com/LibRaw/LibRaw/0.22.2/src/postprocessing/postprocessing_utils_dcrdefs.cpp), [현상 단계 소스](https://raw.githubusercontent.com/LibRaw/LibRaw/0.22.2/src/postprocessing/dcraw_process.cpp)를 확인했다.

WB는 계수의 비율이다. 유효한 최솟값을 1로 정규화해 작은 공통 배율이 LibRaw의 tiny-value fallback을 일으키지 않도록 한다. 네 번째 채널 0은 부재 표시로 그대로 전달하며 alpha로 해석하지 않는다. 유한값·양수·native float에서 표현할 수 있는 비율을 검증한다. native 인수는 invariant round-trip 문자열로 전달한다. 요청 계수는 AppliedWhiteBalance에, 원본 카메라 계수는 CameraColor.AsShot에 별도로 보존한다. native 결과에 같은 카메라 WB를 다시 적용하지 않는다.

모든 현상은 기존 프로세스 취소/timeout·임시 출력 정리 경로를 사용한다. 앱 로더는 늦게 완료된 개발 결과도 취소 확인 후 폐기·해제한다. 픽셀 경로는 기존 선형 16비트 PPM→float이며 새로운 외부 라이브러리나 native 파일은 추가하지 않았다. 라이선스와 native 메타데이터 경계는 [이전 보고서](raw-color-metadata.md)를 따른다.

## 검증 범위

Release 빌드와 Core/Imaging/Desktop 검증 결과는 [빌드 로그](verification/2026-10-11-wb-build.txt), [테스트 로그](verification/2026-10-11-wb-tests.txt), [집계](verification/2026-10-11-wb-results.json)에 보존한다.

Core 16 + Imaging 39 + Desktop 155 = 210개 검사 통과. 실제 6종의 독립 reference와 최대 float 차이는 모두 0이었다. 기존 CS0618 경고 2개 유지. 앱 시작 5초 정상, Core 소스 8개와 fixture 7개 해시 일치.

- 작은 공통 배율의 비율 유지, 네 번째 물리 채널, 음수·Infinity·표현 불가 비율 거부.
- 개발 기능 교체와 WB 전달, preview/full 소유권, 미지원 기능 거부, pre/late cancellation 및 surface 해제.
- CR2/CR3/NEF/ARW/RAF/DNG 원본을 각각 사용자 계수로 재현상. red 채널 256개 초과 레벨과 원본 AsShot 보존 확인.
- 프랑스 문화권에서 호출해도 계수의 소수점이 정확히 전달됨을 검증.
- 앱 로더의 사용자 WB 미리보기와 같은 WB의 전체 결과를 축소한 값이 일치함을 검증.
- 정규화하지 않은 원래 계수를 독립 dcraw_emu 호출에 전달해 모든 float 샘플 비교. 허용 오차는 4/65535이며 실제 최대 차이는 집계의 WB 항목에 기록한다.
- 사용자 WB 결과를 추가 float 보정 후 전체 해상도 JPEG로 저장·재디코딩. 기본 현상/내보내기 회귀 및 원본 해시 보존도 유지한다.

이 비교는 동일 LibRaw 개발 엔진과의 수치 일치이며 Lightroom/색표 기반 색 정확도 검증은 아니다. [시작·Core/fixture 해시 기록](verification/2026-10-11-wb-startup.json)의 앱 시작은 UI 입력 검증과 구분한다.

## 남은 작업

새 기능은 개발 엔진과 앱 로더 API에 구현했다. 현재 UI의 Temperature/Tint는 기존 상대 sRGB 보정이고 이 새 경로에 연결하지 않았다. 사용자의 카메라 WB UI, Kelvin/중립점 선택, WB 설정의 저장·Undo/Redo 및 캐시 기반 실시간 미리보기가 다음 작업이다. 재현상마다 원본을 다시 읽으므로 이 API만으로 30~60FPS를 주장하지 않는다.

LibRaw 16비트 현상 출력까지의 클리핑은 되돌리지 못한다. float sensor/demosaic 기반 작업 공간, 하이라이트 복원, ICC, Windows/macOS Intel, GPU와 상용 배포 검증은 여전히 필요하다. TASK-011을 완료 표시하지 않는다.
