# TASK-011 — 상대 카메라 WB 사용자 제어

검증일: 2026-10-11. 전체 전문 RAW 편집기 목표는 진행 중이다.

## 구현

RAW 사진에 Warmth·Tint 입력, Apply WB 및 As shot 버튼을 추가했다. 범위는 -100~100의 상대값이며 Kelvin이 아니다. 기존 Temperature/Tint 슬라이더는 기존 출력 색 보정을 유지하고 새 RAW WB 블록과 구분했다. 일반 JPEG/PNG에서는 RAW 블록을 숨긴다. 촬영 WB 정보를 얻을 수 없으면 입력/Apply를 비활성화하지만 기존 사용자 WB를 As shot으로 되돌릴 수 있다.

CameraWhiteBalanceAdjust는 Imaging에서 촬영 WB를 기준으로 RGB/G 채널 gain을 계산한다. w=warmth/100, t=tint/100일 때 red=2^(0.32w+0.10t), green=2^(-0.18t), blue=2^(-0.32w+0.10t)이며 네 번째 채널에도 green gain을 적용한다. 네 번째 채널 0은 부재로 그대로 유지한다. 이 수식은 상대 조절 범위의 정의이며 색온도 물리 모델이나 비-RGB 센서의 보정을 의미하지 않는다.

계수는 이전 IRawDeveloper 경로로 전달해 원본 현상에 적용한다. 이미 현상된 선형 sRGB surface에 카메라 gain을 다시 곱하지 않는다. Apply는 성공한 최신 현상 결과를 편집 상태와 히스토리에 반영하며 저장/재개/내보내기를 지원한다. As shot은 WB만 해제하고 tone 값을 유지한다. Reset all adjustments는 WB와 tone을 모두 초기화한다.

저장한 WB 비율에서 입력값을 역산한다. 범위를 벗어나거나 네 번째 채널 관계가 맞지 않는 값은 빈 입력으로 표시하고 원래 WB를 그대로 보존한다. 값을 임의로 clamp해 적용하지 않는다. 화면에는 입력값을 소수 세 자리로 표시하지만 저장된 계수는 float 그대로 유지한다. 변경하지 않은 입력에서 Apply를 누르면 표시 반올림 때문에 새로운 WB/히스토리를 만들지 않는다. 입력 파싱/표시는 현재 UI 문화권, native 인수는 기존 invariant 형식을 사용한다.

중립점 선택은 구현하지 않았다. 현재 surface는 LibRaw의 16비트 현상 출력이며, 클리핑과 실제 적용 행렬의 차이를 정확히 되돌릴 수 없다. 카메라 데이터 경로 없이 역행렬만으로 neutral picker를 구현하면 정확도를 보장할 수 없어 다음 엔진 작업으로 남겼다. Core 소스는 변경하지 않았다.

## 검증

[빌드 로그](verification/2026-10-11-wb-controls-build.txt), [테스트 로그](verification/2026-10-11-wb-controls-tests.txt), [집계](verification/2026-10-11-wb-controls-results.json): Core 16 + Imaging 48 + Desktop 206 = 270개 검사 통과. Release 오류 0, 기존 CS0618 경고 2개.

- warmth의 red/blue 방향, tint의 두 green 관계, 채널 부재 보존.
- 양/음 endpoint 9조합의 저장값 역산, As shot 영점, 미표현 WB·잘못된 범위·NaN 거부.
- 실제 CR2/CR3/NEF/ARW/RAF/DNG에서 warmth 50/tint 20의 현상, 독립 LibRaw reference, 미리보기와 전체 해상도 출력, WB 파일 복원과 조절값 역산. reference 최대 float 차이는 모두 0이었다.
- 기존 저장 경합·히스토리·취소/소유권·원본 보호 회귀 유지.
- [앱 시작·해시 기록](verification/2026-10-11-wb-controls-startup.json): 5초 정상 시작, Core 8개와 fixture 7개 해시 일치.

이 검증은 수식·개발 엔진·저장 경로와 앱 시작을 확인한다. 실제 TextBox 입력/버튼 클릭/Undo/Redo의 GUI 통합 검증과 색표 기반 정확도는 아직 확인하지 않았다. 기존 600×400 float+PNG 서비스 측정 p95 10.44ms는 tone 미리보기이며 WB 재현상 시간을 포함하지 않는다.

## 수동 UI 확인 및 다음 작업

RAW 열기→RAW white balance의 Warmth 50/Tint 20→Apply WB→Original 비교→Save edits→다시 열기→입력값 복원→As shot→Undo/Redo→Export를 확인해야 한다. 잘못된 입력, 다른 사진을 여는 도중의 WB 작업 취소, metadata 누락, JPEG/PNG 블록 숨김도 확인해야 한다.

WB 변경마다 원본을 다시 현상하므로 실시간 WB 슬라이더는 미완료다. 다음 우선 작업은 카메라 데이터 캐시와 WB 미리보기, 실제 UI 입력 검증이다. 중립점/Kelvin, ICC/색 정확도, 하이라이트 복원, GPU, Windows/macOS Intel 및 배포 검증은 남아 있다. TASK-011을 완료로 표시하지 않는다.
