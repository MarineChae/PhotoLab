# TASK-009~011 — RAW 촬영 정보와 카메라 색 계수

검증일: 2026-10-11. Windows/macOS 전문 RAW 편집기 목표는 진행 중이다.

## 변경 및 색 공간

RawMetadata에 ISO, 노출 시간(초), 조리개와 RawColorInfo를 추가했다. raw-identify 출력의 지정된 필드만 invariant 숫자로 읽고, 0·음수·NaN·Infinity는 미상으로 남긴다. 카메라 이름 이후의 필드도 읽으며 echo된 파일명을 제거해 다중 행 파일명으로 촬영값을 주입하지 못하게 한다. 출력 형식과 반올림은 [LibRaw 0.22.2 raw-identify 소스](https://raw.githubusercontent.com/LibRaw/LibRaw/0.22.2/samples/raw-identify.cpp)를 따른다. 촬영 숫자는 진단 출력의 정밀도로 제한되며 원본 EXIF 유리수의 무손실 보존은 아니다.

WB·색행렬은 진단 문자열을 파싱하지 않고 libraw_get_cam_mul과 libraw_get_rgb_cam으로 float 값을 읽는다. 3×4 행렬은 row-major이고 네 번째 WB 채널은 alpha가 아니다. 행렬 배열은 복사해 외부 변경을 막으며 값·치수를 검증한다. getter의 필드와 호출 규약은 [고정 버전 C API 소스](https://raw.githubusercontent.com/LibRaw/LibRaw/0.22.2/src/libraw_c_api.cpp)를 기준으로 확인했다. 이는 open_file 후 LibRaw의 카메라 정보이며 최종 현상 과정의 모든 보정이나 색 정확도를 증명하지 않는다.

dcraw_emu -w -o 1 -4가 만든 픽셀은 이미 WB와 색 변환을 적용한 선형 sRGB이다. 추출한 카메라 계수는 별도로 보존하며 이 surface에 다시 적용하지 않는다. 카메라 공간에서의 사용자 WB 조절과 Kelvin WB는 미완료다. Core 소스는 수정하지 않았다.

## 네이티브 경계 및 의존성

Infrastructure가 실행 파일의 실제 위치와 인접 lib 디렉터리에서 공유 라이브러리를 찾는다. macOS는 libraw_r.dylib/libraw.dylib, Windows는 libraw_r.dll/libraw.dll/raw.dll을 확인한다. 시스템 전역에서 임의의 라이브러리를 선택하지 않는다. NativeLibrary로 C getter를 동적으로 바인딩하므로 버전 의존 native struct를 매핑하지 않는다. Windows 경로는 wide API, Unix는 UTF-8 API로 전달한다. Windows DLL과 호출 규약은 실기기 검증이 남아 있다.

컨텍스트 close와 라이브러리 Free는 finally에서 수행한다. 재진입 불가 빌드에 대비해 호출을 직렬화하며 UI 밖 Task.Run에서 실행한다. 취소는 native open 전후 확인하지만 진행 중 open_file을 중단하거나 timeout으로 종료하지 못한다. 기존 프로세스 디코딩과 raw-identify는 기존 취소/timeout을 유지한다. 공유 라이브러리 누락·진입점 누락·유효하지 않은 계수는 경고로 남기고 현상된 이미지 사용을 유지한다. 전체 앱/네이티브 메모리 누수 장시간 검증은 남아 있다.

검증 의존성은 설치된 LibRaw 0.22.2-Release (macOS ARM64)이며 새 NuGet 패키지나 native 바이너리는 번들하지 않았다. 공식 소스는 LGPL 2.1 또는 CDDL 1.0 선택을 명시한다. 배포 시 선택한 라이선스의 고지·소스 제공 등 조건과 설치 라이브러리/추가 코덱의 구성을 검토해야 한다. [LGPL 원문](https://raw.githubusercontent.com/LibRaw/LibRaw/0.22.2/LICENSE.LGPL), [CDDL 원문](https://raw.githubusercontent.com/LibRaw/LibRaw/0.22.2/LICENSE.CDDL). 상용 배포 승인이나 패키징 완료를 의미하지 않는다.

## 검증

Release 빌드 성공, 기존 CS0618 경고 2개. [테스트 로그](verification/2026-10-11-color-tests.txt), [집계](verification/2026-10-11-color-results.json): Core 16, Imaging 34, Desktop 110, 총 160개 검사 통과.

- 배열 불변성, 3×4 치수, non-finite 행렬, 잘못된 WB 계수 거부.
- 분수/초 셔터, 프랑스 문화권의 decimal parsing, 미상 조리개 및 잘못된 촬영값 제외, 파일명 주입 거부.
- 실제 CR2/CR3/NEF/ARW/RAF/DNG의 촬영 정보와 native WB/행렬, float 미리보기와 전체 해상도 JPEG 출력, 원본 해시 보존.
- Nikon D2H: WB 563/256/373/256, 행렬 [0,0] 약 1.7988 및 [1,1] 약 1.3266. 기준 진단 행렬의 반올림 오차 내 일치로 인덱스 방향을 확인했다.
- Sony ILCE-7S의 f/0은 null, Pentax K-r DNG의 네 번째 WB 채널 0은 그대로 보존.
- [앱 시작·Core/fixture 해시 기록](verification/2026-10-11-color-startup.json). 5초 시작 검증은 실제 UI 입력 검증과 구분한다.

600×400 float+PNG 서비스는 60회 입력 중 60회 갱신, p95 10.47ms. UI 표시·고해상도 편집 FPS는 포함하지 않는다. Windows, 전체 UI 촬영값 표시, ICC와 기준 사진 색 정확도, GPU, 장시간 메모리 검증은 남아 있다.
