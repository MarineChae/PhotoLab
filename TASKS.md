PhotoLab — Development Task Tracker

실제 작업 상태는 이 문서를 기준으로 한다. `Task.md`는 호환 안내 파일이다.

문서 버전: 1.0
프로젝트 기준 버전: 0.1.2
개발 환경: C# / .NET 10 / Avalonia UI
지원 플랫폼: Windows / macOS
최종 목표: 고품질·고성능 크로스 플랫폼 RAW 사진 편집기

⸻

1. 작업 관리 규칙

본 문서는 PhotoLab의 개발 작업과 진행 상태를 관리한다.

AI Agent는 SPEC.md, AGENTS.md, TASKS.md를 기준으로 개발한다.

1.1 상태 정의

* [ ] 미착수
* [~] 진행 중
* [x] 완료 및 검증
* [!] 차단됨

[x]는 구현뿐 아니라 필요한 테스트까지 통과했을 때만 사용한다.

기존 프로젝트에서 구현되었다고 보고된 기능이라도 실제 검증되지 않았다면 완료로 표시하지 않는다.

1.2 작업 수행 규칙

1. 작업 시작 전 관련 코드를 분석한다.
2. 해당 작업의 의존성을 확인한다.
3. 한 번에 하나의 작업을 완료하는 것을 원칙으로 한다.
4. 작업과 무관한 파일은 수정하지 않는다.
5. 구현 후 빌드 및 테스트를 수행한다.
6. 테스트 결과를 기록한다.
7. 완료한 작업의 상태를 갱신한다.
8. 변경 사항을 CHANGELOG.md에 기록한다.

1.3 우선순위

우선순위	의미
P0	핵심 기능 및 필수 기반
P1	전문 편집 기능 및 성능
P2	확장 기능
P3	장기 개발

⸻

2. Phase 0 — 기존 프로젝트 검증

목표: 현재 PhotoLab v0.1.2의 실제 구현 상태를 확인하고 안정적인 개발 기반을 확보한다.

완료 조건: Windows/macOS에서 프로젝트를 빌드할 수 있으며 기존 기능의 동작 여부가 확인되어야 한다.

TASK-001 — 프로젝트 구조 분석

상태: [~] 소스 분석 완료, 빌드·테스트·플랫폼 검증 대기 (2026-10-10)
분석 결과: [docs/TASK-001-analysis.md](docs/TASK-001-analysis.md)
최초 Core 테스트 실행은 변수 중복 선언(CS0136)으로 실패했으며 TASK-002에서 수정 후 16개 테스트가 통과했다.
이후 자율 개발 요청의 빌드 지시에 따라 macOS Release 빌드와 앱 시작 확인을 수행했다. Windows 및 전체 UI 런타임 검증은 남아 있다.

* 현재 프로젝트 구조 분석
* 프로젝트 간 의존성 확인
* UI와 이미지 처리 로직의 결합도 확인
* 중복 코드 및 불필요한 의존성 확인
* 현재 구현 기능 목록 작성

완료 조건

* 실제 코드 기준으로 구현 상태를 정리한다.
* SPEC.md와 구현 간 차이를 기록한다.

TASK-002 — 빌드 환경 검증

의존성: TASK-001

상태: [~] macOS ARM64 Release 빌드·Core 테스트 통과, 앱 시작 확인; Windows 검증 대기 (2026-10-10)
검증 결과: [docs/TASK-002-validation.md](docs/TASK-002-validation.md)
테스트 변수 중복 선언을 수정했다. Core 및 Desktop 소스는 수정하지 않았다.
자율 개발 요청으로 전체 .NET 빌드가 승인되었다. macOS 앱은 5초간 오류 출력 없이 실행됐으며 Windows 앱 빌드·실행은 미검증이다.

* .NET SDK 버전 확인
* NuGet 패키지 복원
* macOS 빌드
* Windows 빌드
* 실행 오류 확인
* 빌드 스크립트 검증

완료 조건

* 두 플랫폼에서 빌드 및 실행 성공
* 플랫폼별 검증 결과 기록

TASK-003 — 기존 이미지 편집 기능 검증

의존성: TASK-002

상태: [~] 코덱·원본 보호·취소·히스토리 회귀 및 헤드리스 WB 입력 17개 통과; native OS UI 및 Windows 검증 대기
내보내기는 원본/링크 경로를 보호하며 임시 파일 작성 후 목적지를 교체한다.

* JPEG/PNG 불러오기
* 노출 조절
* 대비 조절
* 하이라이트/그림자 조절
* 색온도/틴트 조절
* 채도/생동감 조절
* 원본 비교
* Undo/Redo
* 이미지 내보내기

완료 조건

* 기존 기능 정상 동작
* 발견된 오류 수정
* 회귀 테스트 작성

TASK-004 — 실시간 슬라이더 검증

의존성: TASK-003

상태: [~] 즉시 미리보기 요청, 최신 결과 검사, 직렬 CPU 처리, 드래그 Undo 통합 구현 및 서비스 테스트 통과
600px 상호작용 미리보기와 180ms 유휴 후 최대 1800px 미리보기를 사용한다. WB 헤드리스 드래그는 검증했으며 네이티브 OS 입력/FPS는 미검증이다.
PNG 왕복을 제거하고 표시 RGBA8를 Bitmap으로 직접 복사한다. 600×400 합성 입력에서 float 처리+bitmap 생성 p95 5.79ms, 58.9회/초였으며 실제 Editor 화면 표시/FPS와 구분한다. 근거: docs/direct-preview-display.md.
실제 EditorWindow Exposure mouse drag에서 60개 입력/Skia 사진 영역 프레임, idle 최종 품질, 저장·단일 Undo/Redo를 검증했다. 600px 합성 이미지 관찰률 46.7회/초, polling/capture 포함 p95 24.04ms이며 네이티브 OS FPS가 아니다. 근거: docs/editor-frame-input.md.

* 슬라이더 변경 이벤트 확인
* 렌더링 요청 즉시 발생 확인
* 이전 렌더링 작업 취소
* 오래된 렌더링 결과 폐기
* 연속 조작 시 UI 응답성 확인
* Undo 기록 통합 확인

완료 조건

* 슬라이더 조작 중 이미지가 연속적으로 갱신된다.
* 오래된 결과가 최신 결과를 덮어쓰지 않는다.
* UI 스레드가 차단되지 않는다.

⸻

3. Phase 1 — 이미지 처리 기반 개선

목표: 고정밀 RAW 현상과 GPU 가속을 위한 공통 이미지 처리 구조를 구축한다.

TASK-005 — 이미지 버퍼 추상화

우선순위: P0
의존성: TASK-003

상태: [~] PhotoLab.Imaging의 RGBA32F 버퍼·Stride·선형 sRGB 메타데이터·명시적 해제 및 작은 버퍼 풀 재사용 구현
소스 수명이 끝나기 전 렌더러를 drain한다. 전체 앱 메모리 누수 및 Windows 검증은 남아 있다.
표시 배열을 생성자 호출 동안만 pin하여 Skia Bitmap으로 복사한다. 홀수 폭/RGBA/alpha 및 배열 변경·GC 뒤 화면 소유권을 검증했다. float 버퍼는 표시 Bitmap과 독립적이다.
native RGB16 직접 area 축소와 이미지 수명 내 borrowed span으로 큰 중간 RGBA32F/managed RGB16 복사를 제거했다. 150 geometry float/reference 및 실제 full/half·소유권/취소 회귀 통과. 변환 비용과 warm 할당 비교: docs/direct-rgb16-preview.md.

* ImageBuffer 구조 분석
* 픽셀 포맷 정의
* 이미지 크기 및 Stride 관리
* 색 공간 메타데이터 정의
* 버퍼 소유권 및 해제 규칙 정의
* 버퍼 재사용 구조 구현

완료 조건

* 이미지 버퍼가 UI에 종속되지 않는다.
* 픽셀 포맷과 색 공간을 명확하게 식별할 수 있다.
* 메모리 누수가 발생하지 않는다.

TASK-006 — 고정밀 이미지 처리

우선순위: P0
의존성: TASK-005

상태: [~] 16비트 선형 PPM → float 보정 → 출력 시 sRGB RGBA8 변환을 앱 미리보기·내보내기에 연결
인접 16비트 값, 노출 복원, sRGB 왕복, 기존 보정 비교, NaN/Infinity 및 버퍼 해제 테스트 통과.
RAW 실파일 검증 결과는 PROGRESS.md와 docs/autonomous-development.md에 기록한다. libpng PNG16 입력 및 float→sRGB RGBA16 출력을 연결했다. 전체 65,536 코드·alpha·필터/Adam7·ICC 입력과 실제 RAW 6종 전체 출력·원본 보존 검증 통과. macOS native staging/자동 선택과 PNG16 UI 선택도 연결했다. Windows·네이티브 저장 dialog·20MP 메모리/성능·전체 ICC 기준 검증은 남아 있다. 근거: docs/png16-input.md, docs/png16-export.md.

* 16비트 이미지 처리 지원
* 32비트 부동소수점 이미지 처리 지원
* 기존 8비트 처리 구조 분석
* 고정밀 처리 파이프라인 구축
* 불필요한 정밀도 손실 제거
* 기존 보정 알고리즘 이관

완료 조건

* 내부 보정 과정에서 불필요한 8비트 변환이 발생하지 않는다.
* 고정밀 이미지 데이터를 유지할 수 있다.
* 기존 기능에 회귀 문제가 발생하지 않는다.

TASK-007 — 이미지 처리 파이프라인 정리

우선순위: P0
의존성: TASK-006

상태: [~] 노출/WB → 명암 → 대비 → 색상 단계를 별도 함수로 분리하고 선형 sRGB 입출력·alpha 규칙을 명시했다.
기존 CPU 결과 일치, 비가환 처리 순서, 10회 float 재현성 테스트 통과. Windows 및 색상 정확도 검증은 남아 있다.

* 이미지 처리 단계 정의
* 단계별 입력/출력 포맷 정의
* 색 공간 변환 위치 정의
* 처리 순서 명시
* 보정 파라미터 구조 정리
* 파이프라인 단위 테스트

완료 조건

* 처리 순서가 명확하다.
* 이미지 보정 기능을 독립적으로 추가할 수 있다.
* 동일한 입력과 설정에서 재현 가능한 결과를 생성한다.

⸻

4. Phase 2 — 멀티 RAW 현상

목표: 주요 카메라 제조사의 RAW 파일을 고정밀 데이터로 현상한다.

TASK-008 — RAW 디코더 인터페이스

우선순위: P0
의존성: TASK-005

상태: [~] IRawDecoder, RawImage 소유권 이전/해제, RawMetadata 구조 및 앱 로더의 디코더 주입 구현.
실제 LibRaw 실행은 Infrastructure에 분리했다. 교체 디코더·오류·늦은 취소·기존 API 호환 테스트 통과. Windows 런타임 검증 대기.

* IRawDecoder 정의
* RAW 포맷 식별
* RAW 메타데이터 구조 정의
* 디코딩 오류 처리
* 디코더 리소스 관리
* 비동기 디코딩 구조

완료 조건

* UI와 RAW 디코더가 분리된다.
* 디코더 교체가 가능하다.
* 손상된 파일을 안전하게 처리한다.

TASK-009 — LibRaw 통합 개선

우선순위: P0
의존성: TASK-008, TASK-006

상태: [~] float 디코딩과 카메라 제조사·모델 추출을 인터페이스/Infrastructure에 연결.
같은 LibRaw 설치의 raw-identify를 선택적으로 사용하며, 누락/미인식/2초 시간 초과는 메타데이터 경고로 기록하고 이미지 편집을 유지한다.
실제 RAW 6개에서 카메라 식별·ISO/셔터/조리개·float WB/3×4 카메라 행렬·전체 해상도 출력·원본 해시 통과.
같은 설치의 LibRaw 공유 라이브러리를 C API getter로 선택적으로 로드하며 실패는 메타데이터 경고로 보존한다.
Windows 및 전체 리소스 누수 검증은 미완료다.

* 기존 LibRaw 연동 분석
* Windows/macOS 라이브러리 로딩
* RAW 디코딩 결과의 정밀도 확인
* 16비트 이상 이미지 데이터 전달
* 카메라 메타데이터 추출
* 오류 코드 및 예외 처리
* 네이티브 메모리 해제 검증

완료 조건

* RAW 파일을 고정밀 이미지 버퍼로 전달한다.
* RAW 데이터를 JPEG로 변환한 뒤 편집하는 구조를 사용하지 않는다.
* 네이티브 리소스 누수가 없다.

TASK-010 — 제조사별 RAW 호환성

우선순위: P0
의존성: TASK-009

* Canon CR2/CR3
* Nikon NEF
* Sony ARW
* Fujifilm RAF
* Adobe DNG
* Panasonic RW2
* Olympus ORF
* 기타 지원 포맷 확인

완료 조건

* 테스트에 사용한 카메라 모델과 RAW 포맷을 기록한다.
* 지원 여부를 실제 파일 기준으로 검증한다.
* 미지원 파일은 명확한 오류를 표시한다.

TASK-011 — RAW 화이트밸런스 및 색상 변환

우선순위: P0
의존성: TASK-009

상태: [~] 카메라 WB 4채널 계수와 3×4 camera→sRGB 행렬을 native float 정밀도로 추출·보존했다.
Nikon 기준 계수·행렬 방향 및 DNG 네 번째 채널 부재를 실제 파일에서 검증했다.
IRawDeveloper/LibRawDecoder/앱 로더에 사용자 WB 현상 경로를 추가했다. -r과 +M을 사용해 원본 카메라 공간에서 적용하고 기본 -w 동작은 유지한다.
실제 6종의 사용자 WB와 독립 LibRaw 호출 결과·미리보기·전체 해상도 내보내기 검증 통과 (최대 float 차이 0).
WB 포함 PhotoEditState와 version 2 저장·복원, 전체 상태 히스토리, 앱의 재개·초기화·내보내기 연결을 구현했다.
구형 저장 API의 WB 손실 방지와 동시 저장 20회, 실제 RAW 6종 저장→복원→재현상 픽셀 일치 통과.
상대 카메라 Warmth/Tint 입력과 Apply WB/As shot UI, 저장 계수에서 입력값 역산을 구현했다. 실제 RAW 6종에서 조절값 저장·복원 및 독립 LibRaw 출력 일치 검증 통과.
헤드리스 텍스트·마우스 입력 17개에서 WB 적용/Original/저장/재개/As shot/Undo/Redo/실패·닫힘을 검증하고 사진 교체 중 disposed Bitmap 참조 오류를 수정했다.
네이티브 OS 입력·카메라 데이터 캐시 기반 실시간 제어·중립점/Kelvin·색 정확도·클리핑 검증은 남아 있다.

unpack 데이터를 유지하는 명시적 NativeRawSession과 half-size 미리보기 API를 추가했다. 실제 6종에서 소스 삭제 후 반복 WB·full/half 기준 일치 및 수명·취소를 검증했다.
PHOTOLAB_NATIVE_RAW_BRIDGE를 명시하면 앱 로더/화면의 custom WB와 전체 로딩에 세션을 사용하도록 연결했다. 실제 RAW 6종의 로더/헤드리스 UI 픽셀·Original·Undo 검증 통과.
파일 stamp 변경·다시 열기·창 닫힘의 캐시 수명과 늦은 반환 해제를 검증했다.
half-size→600px WB 프레임과 180ms idle 후 전체 품질 승격을 연결했다. 이전 결과 폐기·실패 복구·히스토리 유지·닫힘을 제어 테스트하고 실제 RAW 6종에서 첫 프레임/품질 결과를 검증했다.
연속 카메라 WB 슬라이더·최신 대기 값 합치기·드래그 단일 Undo를 구현했다. 헤드리스 입력에서 사진 전환/닫힘의 늦은 결과 해제와 미표현 저장 WB 보존을 검증했다.
실제 6종의 연속 입력/frame hash와 0 복원·최신 CLI 품질·단일 Undo를 검증했다. Original flash, pump idle 중 입력 역산 오차, 0에서 full decode/display 문제를 수정했다. DNG 관찰률은 21회/초로 30회/초 미달이며 half RGB16 변환/중간 할당 최적화가 다음 작업이다. 근거: docs/native-wb-drag.md.
실제 6종에서 held-mouse 드래그 프레임, idle 품질의 독립 CLI 일치, 단일 Undo를 확인했다. 첫 프레임의 헤드리스 관찰은 103~162ms였다.
네이티브 OS 지연/FPS·Windows native 빌드·sensor-float 색 정확도는 남아 있다. 근거: docs/continuous-wb-controls.md.

* 카메라 화이트밸런스 정보 추출
* 화이트밸런스 조절
* 카메라 색상 행렬 처리
* 작업 색 공간 변환
* 색상 클리핑 검증
* 색상 정확도 테스트

완료 조건

* RAW의 화이트밸런스를 고정밀 데이터에서 조절할 수 있다.
* 색 공간 변환 결과가 검증된다.

⸻

5. Phase 3 — GPU 가속

목표: Windows/macOS에서 실시간 이미지 보정 성능을 확보한다.

TASK-012 — GPU 백엔드 기술 검증

우선순위: P1
의존성: TASK-007

상태: [~] Silk.NET.Vulkan 2.23.0/MoltenVK 1.4.2의 standalone C# compute probe를 구현했다. Apple M4 hardware에서 노출/전송/HDR/alpha/tail 12개 검사가 CPU 명시적 계산과 오차 0, 활성 Vulkan validation layer에서도 통과했다.
Portability enumeration/subset 처리와 라이선스 검토를 기록했다. shaderFloat64 미지원, 전체 control/kernel 오차·GPU 버퍼 재사용·앱 연결 및 Windows 실제 실행은 남아 있다. 최종 backend 선택은 미완료다. 근거: docs/gpu-compute-probe.md.

* Vulkan 기반 GPU 처리 검토
* macOS MoltenVK 호환성 확인
* C# 바인딩 검토
* GPU Compute Shader 실행
* GPU 메모리 전송 테스트
* CPU 처리 결과와 비교
* 외부 라이브러리 라이선스 검토

완료 조건

* Windows/macOS에서 GPU 연산이 실제로 동작한다.
* 채택할 GPU 백엔드를 결정한다.
* 기술 검증 결과를 문서화한다.

TASK-013 — GPU 이미지 버퍼

우선순위: P1
의존성: TASK-012

상태: [~] host-coherent buffer 2개/pipeline/descriptor/command/fence 재사용, immutable source upload 재사용 및 capacity growth/A-B-A 전환을 구현했다. 1800px/4.41MP, fence 이후 결과 폐기와 renderer close reader→device 해제를 검증했다. driver 전체 메모리·장시간/device-loss 및 Windows 검증은 남아 있다.

* GPU 이미지 버퍼 생성
* CPU/GPU 데이터 업로드
* GPU 버퍼 재사용
* GPU 동기화
* 리소스 해제
* 메모리 사용량 측정

완료 조건

* 이미지 데이터를 GPU 메모리에 유지할 수 있다.
* 반복 렌더링 시 불필요한 메모리 할당이 최소화된다.

TASK-014 — GPU 기본 보정

우선순위: P1
의존성: TASK-013

상태: [~] linear float 8개 controls와 CPU/GPU 공통 파라미터 준비, CPU fallback/PHOTOLAB_VULKAN_LIBRARY opt-in 미리보기 연결을 구현했다. Apple M4에서 27개 neutral/endpoints/combined 설정, 최대 float 오차 1.90735e-6 (허용 2e-5 relative/absolute), alpha exact. 실제 editor Vulkan 입력과 RAW 6종 neutral CLI 비교 통과. Windows/다른 GPU/실제 RAW의 다양한 controls·paired 성능은 미검증이다. 근거: docs/gpu-float-controls.md.

* 노출
* 대비
* 화이트밸런스
* 채도
* 생동감
* 하이라이트/그림자
* CPU/GPU 결과 비교

완료 조건

* 기본 보정이 GPU에서 실행된다.
* CPU 결과와 허용 오차 내에서 일치한다.

TASK-015 — 실시간 렌더링 최적화

우선순위: P1
의존성: TASK-014

* 미리보기 캐싱
* 슬라이더 연속 입력 최적화
* 렌더링 작업 버전 관리
* 미리보기 해상도 조절
* 조작 종료 후 고품질 렌더링
* GPU 버퍼 재사용
* UI 응답성 측정

완료 조건

* 목표 하드웨어에서 30~60FPS 미리보기를 지향한다.
* 연속 슬라이더 조작 중 UI가 멈추지 않는다.
* 최종 결과가 최신 보정값과 일치한다.

TASK-016 — CPU Fallback

우선순위: P1
의존성: TASK-014

* GPU 초기화 실패 처리
* GPU 미지원 환경 감지
* CPU 렌더러 자동 전환
* GPU 오류 복구
* 결과 일관성 검증

완료 조건

* GPU를 사용할 수 없어도 기본 이미지 편집이 가능하다.

⸻

6. Phase 4 — 전문 이미지 보정

목표: Lightroom의 주요 현상 기능을 대체한다.

TASK-017 — HSL 보정

우선순위: P1
의존성: TASK-007

* 색상별 Hue 조절
* 색상별 Saturation 조절
* 색상별 Luminance 조절
* 색상 경계 보간
* 색상 정확도 검증
* UI 연동

완료 조건

* 색상별 보정이 자연스럽게 적용된다.
* 극단적인 보정값에서도 비정상적인 색상 변화가 최소화된다.

TASK-018 — 톤 커브

우선순위: P1
의존성: TASK-007

* RGB 통합 커브
* R/G/B 개별 커브
* 제어점 추가/삭제
* 곡선 보간
* 커브 프리셋
* UI 연동

완료 조건

* 사용자가 커브를 직접 편집할 수 있다.
* 결과가 실시간으로 반영된다.

TASK-019 — 히스토그램

우선순위: P1
의존성: TASK-006

상태: [~] 보정된 float preview의 RGB/Y 256-bin 집계, RGB 채널 ≤0/≥1 지표와 실시간 log-count 그래프 연결. 투명도/경계/HDR/소유권/취소, 최신 version publication, CPU/Vulkan headless 그래프 capture와 WB 갱신 검증 통과. full-resolution 집계·실제 OS 표시·Windows 검증은 남아 있다. 근거: docs/live-histogram.md.

* RGB 히스토그램
* 휘도 히스토그램
* 하이라이트 클리핑 표시
* 그림자 클리핑 표시
* 실시간 갱신

완료 조건

* 현재 보정 결과를 기준으로 히스토그램이 표시된다.

TASK-020 — 컬러 그레이딩

우선순위: P1
의존성: TASK-007

* Shadows 색상 조절
* Midtones 색상 조절
* Highlights 색상 조절
* 색상 혼합
* Balance 조절
* UI 연동

완료 조건

* 명암 영역별 색상 조절이 가능하다.

TASK-021 — 디테일 보정

우선순위: P2
의존성: TASK-007

* 샤프닝
* 노이즈 감소
* 색상 노이즈 감소
* 디테일 보존
* 확대 미리보기 검증

완료 조건

* 노이즈 감소와 디테일 보존 사이의 균형을 조절할 수 있다.

TASK-022 — 렌즈 보정

우선순위: P2
의존성: TASK-009

* 렌즈 메타데이터 추출
* 렌즈 프로파일 연동
* 왜곡 보정
* 색수차 보정
* 비네팅 보정

완료 조건

* 지원 렌즈에 대해 프로파일 기반 보정을 수행할 수 있다.

⸻

7. Phase 5 — 비파괴 편집 및 프리셋

TASK-023 — 편집 상태 저장

우선순위: P0
의존성: TASK-007

상태: [~] 기존 저장 오류 수정의 일부로 버전 1 JSON·원자적 저장·레거시 호환·잘못된 값/버전 거부 및 원본 링크 보호 구현
WB 포함 version 2 저장·복원, WB 손실을 막는 구형 API guard·프로세스 내 동시 쓰기 직렬화, 실제 RAW 6종 저장 후 픽셀 재현성 검증 통과.
취소 시 기존 JSON 보존 테스트 통과. 자동 저장 및 마지막 작업 자동 재개는 미구현이다.

* 보정 데이터 버전 관리
* JSON 저장/불러오기
* 파일별 편집 상태 관리
* 앱 재실행 후 복원
* 잘못된 데이터 처리

완료 조건

* 원본 파일을 변경하지 않고 편집 상태를 복원할 수 있다.

TASK-024 — 편집 히스토리

우선순위: P0
의존성: TASK-023

* Undo/Redo 검증
* 연속 슬라이더 조작 통합
* 히스토리 크기 제한
* 편집 상태 복원
* 메모리 사용량 검증

완료 조건

* 연속적인 편집 작업을 안정적으로 되돌릴 수 있다.

TASK-025 — 프리셋

우선순위: P1
의존성: TASK-023

상태: [~] 현재 8개 기본 보정값의 version-1 preset 저장/목록/삭제/미리보기/적용을 구현했다. 엄격한 JSON·중복/취소·파일명 안전성, 다른 사진 적용·단일 Undo/Redo·camera WB 유지 및 CPU/Vulkan headless 입력 검증 통과. Windows/native OS와 향후 고급 보정/RAW WB preset format은 남아 있다. 근거: docs/basic-presets.md.

* 프리셋 저장
* 프리셋 불러오기
* 프리셋 목록
* 프리셋 삭제
* 프리셋 미리보기
* 프리셋 버전 호환성

완료 조건

* 사용자가 보정 설정을 저장하고 다른 사진에 적용할 수 있다.

⸻

8. Phase 6 — 사진 라이브러리

TASK-026 — 사진 탐색

우선순위: P2

상태: [~] top-level 폴더 candidate 목록·worker scan·파일 선택/이전/다음·최신 요청 처리와 source-stamped session draft 보관을 연결했다. 2,000개 파일 discovery 및 CPU/Vulkan headless 선택·실패·복원·supersession 검증 통과. 썸네일 생성/캐시·SQLite catalog·Windows/native folder picker·실제 대규모 전환 성능은 남아 있다. 근거: docs/photo-folder-navigation.md.

* 폴더 탐색
* 이미지 목록
* 썸네일 생성
* 썸네일 캐싱
* 이미지 선택
* 빠른 이미지 전환

완료 조건

* 대량의 이미지가 있는 폴더를 안정적으로 탐색할 수 있다.

TASK-027 — 사진 메타데이터

우선순위: P2
의존성: TASK-026

* EXIF 추출
* 촬영 날짜
* 카메라 모델
* 렌즈 정보
* ISO
* 셔터속도
* 조리개

완료 조건

* 주요 촬영 정보가 정확하게 표시된다.

TASK-028 — 사진 라이브러리 DB

우선순위: P2
의존성: TASK-026

* SQLite 연동
* 사진 인덱싱
* 별점
* 태그
* 앨범
* 검색
* 파일 이동 및 삭제 대응

완료 조건

* 대량의 사진을 관리할 수 있다.
* 원본 파일과 라이브러리 정보가 일관되게 유지된다.

TASK-029 — 일괄 편집

우선순위: P2
의존성: TASK-025, TASK-026

* 여러 사진 선택
* 보정값 복사
* 보정값 붙여넣기
* 프리셋 일괄 적용
* 일괄 내보내기
* 작업 진행률
* 작업 취소

완료 조건

* 여러 사진을 안정적으로 일괄 처리할 수 있다.

⸻

9. Phase 7 — 고급 기능

TASK-030 — 부분 마스킹

우선순위: P2

* 브러시 마스킹
* 선형 그라데이션
* 원형 그라데이션
* 마스크 Feather
* 마스크 반전
* 마스크별 보정값
* GPU 처리 연동

TASK-031 — AI 마스킹

우선순위: P3
의존성: TASK-030

* 피사체 선택
* 하늘 선택
* 배경 선택
* AI 모델 실행 환경
* 추론 성능 최적화

TASK-032 — AI 노이즈 제거

우선순위: P3

* AI 노이즈 제거 모델 검토
* RAW 처리 파이프라인 연동
* GPU 추론
* 디테일 보존 검증
* 처리 성능 측정

⸻

10. Phase 8 — 품질 및 배포

TASK-033 — 이미지 품질 검증

우선순위: P0

* 기준 이미지 데이터셋
* CPU/GPU 결과 비교
* 색상 정확도 검증
* 하이라이트 복원 검증
* 색상 밴딩 검사
* RAW 제조사별 결과 비교

TASK-034 — 성능 프로파일링

우선순위: P1

* RAW 로딩 시간
* 보정 처리 시간
* 미리보기 FPS
* CPU 사용량
* GPU 사용량
* 메모리 사용량
* 이미지 내보내기 시간

TASK-035 — 크로스 플랫폼 검증

우선순위: P0

* Windows 빌드
* macOS Intel 빌드
* macOS Apple Silicon 빌드
* 플랫폼별 RAW 디코딩
* GPU 초기화
* 색상 출력 비교
* 파일 시스템 동작

TASK-036 — 배포 시스템

우선순위: P3

* Windows 설치 프로그램
* macOS 앱 번들
* 코드 서명
* macOS 공증
* 버전 관리
* 자동 업데이트
* 오류 로깅
* 라이선스 검토

⸻

11. 권장 개발 순서

작업은 다음 순서로 진행한다.

TASK-001 ~ TASK-004
        │
        ▼
TASK-005 ~ TASK-007
        │
        ▼
TASK-008 ~ TASK-011
        │
        ▼
TASK-023 ~ TASK-024
        │
        ▼
TASK-012 ~ TASK-016
        │
        ▼
TASK-017 ~ TASK-020
        │
        ▼
TASK-025
        │
        ▼
TASK-021 ~ TASK-022
        │
        ▼
TASK-026 ~ TASK-029
        │
        ▼
TASK-030 ~ TASK-032
        │
        ▼
TASK-033 ~ TASK-036

단, 품질 검증과 플랫폼 테스트는 마지막 단계에서만 수행하지 않고 각 단계에서 반복적으로 진행한다.

⸻

12. 다음 작업

현재 작업: TASK-026 [~] — 폴더 목록·이전/다음·최신 선택·session draft 복원을 연결했고 360개 관련 검증 통과. 근거: docs/photo-folder-navigation.md. 썸네일/SQLite catalog·대규모 전환 측정은 남아 있다. TASK-025 basic presets, TASK-019 histogram과 PNG16 streaming은 유지한다. Windows/clean-machine 배포, LUT/monitor 색상·native picker, 고급 보정, GPU 장시간 및 전체 앱 20MP 메모리/성능 검증은 계속 필요하다.

진행 기록 (2026-10-10): 자율 개발 요청에 따라 macOS 전체 빌드, 원본 보호, 실시간 미리보기 및 float 처리 구현을 진행했다. 플랫폼/UI/품질 검증이 남은 작업은 [~]로 유지한다. 상세 검증 근거는 PROGRESS.md를 기준으로 확인한다.

Codex는 다음 순서로 진행한다.

1. 현재 프로젝트 전체 구조를 확인한다.
2. 실제 구현된 기능을 식별한다.
3. SPEC.md와 비교한다.
4. 아키텍처 및 의존성 문제를 확인한다.
5. 필요한 수정 사항을 정리한다.
6. TASK-002 진행 가능 여부를 판단한다.

사용자가 별도의 구현 요청을 하지 않았다면 대규모 리팩터링을 자동으로 시작하지 않는다.

⸻

13. 완료 기준 (Definition of Done)

각 작업은 다음 조건을 충족해야 완료로 처리한다.

* 요구사항 충족
* 코드 구현 완료
* 빌드 성공
* 관련 테스트 통과
* 기존 기능 회귀 없음
* 필요한 문서 갱신
* CHANGELOG 기록
* 미해결 문제 보고

플랫폼별 검증이 필요한 작업은 해당 플랫폼의 테스트 결과를 기록한다.

테스트할 수 없는 환경이라면 완료로 표시하지 않고 미검증 상태를 명시한다.

⸻

14. 작업 결과 보고 형식

AI Agent는 작업 완료 후 다음 형식으로 보고한다.

작업

TASK-XXX — 작업명

구현 내용

수정 및 추가된 기능을 간결하게 설명한다.

변경 파일

변경된 주요 파일을 기록한다.

검증 결과

* Build:
* Unit Test:
* Runtime Test:
* Platform:

남은 문제

미구현 또는 미검증 사항을 기록한다.

다음 작업

다음으로 진행할 작업 ID를 제시한다.

⸻

15. 프로젝트 마일스톤

마일스톤	목표	관련 작업
M0	기존 프로젝트 안정화	001~004
M1	고정밀 이미지 처리 기반	005~007
M2	멀티 RAW 현상	008~011
M3	비파괴 편집 안정화	023~024
M4	GPU 가속	012~016
M5	전문 이미지 보정	017~022
M6	프리셋 및 사진 관리	025~029
M7	AI 고급 기능	030~032
M8	품질 검증 및 상용화	033~036

최종 목표: Lightroom Classic의 핵심 사진 편집 작업을 대체할 수 있는 고품질·고성능 RAW 사진 편집 프로그램.
