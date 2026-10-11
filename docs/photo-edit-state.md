# TASK-011 / TASK-023 — 카메라 WB 편집 상태의 저장과 재개

검증일: 2026-10-11. 전문 RAW 편집기 전체 목표는 진행 중이다.

## 구현

PhotoEditState는 기존 Core.EditSettings와 선택적인 CameraWhiteBalance를 하나의 불변 상태로 묶는다. Core 변경 금지에 따라 Imaging에 추가했고 Core 소스 8개의 해시는 유지했다. 픽셀이나 버퍼를 저장·히스토리에 포함하지 않는다.

EditFileStore.SaveStateAsync/LoadStateAsync는 WB가 없으면 기존 flat version 1, 있으면 flat version 2와 CameraWhiteBalance의 네 채널을 저장한다. 채널 3의 0은 그대로 보존한다. version 2는 모든 채널이 필요하며 잘못된 객체·범위·누락·대소문자가 다른 중복 메타데이터 키를 거부한다. WB가 있는 version 1도 거부해 값을 조용히 무시하지 않는다. WB 파일은 RAW 확장자에만 허용하지만 실제 RAW 유효성은 계속 디코더가 확인한다.

기존 EditFileStore.LoadAsync는 WB를 버리며 tone만 반환하지 않는다. SaveAsync도 기존 파일을 확인해 version 2나 해석할 수 없는 파일의 덮어쓰기를 거부한다. 새 full-state 저장 API는 사용자가 명시적으로 WB를 없애면 version 1로 저장한다. 구형 Core.SidecarStore는 수정하지 않았으므로 version 2용으로 사용하면 안 된다. WB 없는 version 1은 기존 Core reader와 round-trip 호환을 검증했다.

같은 프로세스의 모든 EditFileStore 쓰기는 짧은 공통 gate를 공유해 구형 API의 확인과 교체 사이에 신형 WB 저장이 끼어들지 못하게 한다. 출력 교체는 기존 atomic write/source guard를 사용한다. 외부 프로세스나 Core.SidecarStore 쓰기까지 직렬화한다는 의미는 아니다.

EditHistory는 전체 PhotoEditState를 보존한다. 기존 Apply(EditSettings)는 현재 WB를 유지하고, 기존 Current는 tone 값을 반환한다. ResetState/ApplyState/CurrentState로 전체 상태를 처리한다. Peek은 히스토리를 이동하지 않고 undo/redo 대상을 확인해 비동기 현상이 실패할 때 현재 상태를 유지할 수 있게 한다. 기존 드래그 그룹, redo 가지 제거, 128개 제한은 유지한다.

## 앱 연결

EditorWindow는 파일을 읽을 때 먼저 전체 편집 상태를 복원한다. Original 비교는 카메라 기본 WB로 만든 미리보기를 사용하고, 편집 미리보기는 저장된 사용자 WB로 별도 현상한다. tone 슬라이더는 기존 600px/1800px 캐시를 사용한다. Save edits와 Export는 사용자 WB를 포함한 현재 상태를 캡처한다.

Reset all adjustments는 tone과 사용자 WB를 함께 초기화한다. WB를 변경하는 Undo/Redo는 원본을 재현상하며 성공한 최신 결과만 화면과 히스토리에 반영한다. 취소/실패 시 이전 상태는 유지하고 임시 preview를 해제한다. 이 UI 연결은 소스 검토·빌드·시작 검증이며 실제 클릭/드래그로 확인한 결과는 아니다.

## 검증

[빌드 로그](verification/2026-10-11-state-build.txt), [테스트 로그](verification/2026-10-11-state-tests.txt), [집계](verification/2026-10-11-state-results.json): Core 16 + Imaging 39 + Desktop 200 = 255개 검사 통과. Release 오류 0, 기존 CS0618 경고 2개.

- WB 상태 round-trip, version 1 호환, 명시적 WB 초기화, 누락·중복·invalid WB 거부.
- 취소·invalid save·구형 API 거부 시 이전 파일 바이트 보존, 일반 이미지 WB 저장 거부.
- 서로 다른 store 인스턴스의 구형/신형 동시 쓰기 20회에서 신형 WB 보존.
- WB와 tone의 Undo/Redo, tone drag group, 히스토리 미리보기의 비변경성, 기존 제한·분기 회귀.
- 실제 CR2/CR3/NEF/ARW/RAF/DNG를 소유한 임시 폴더로 복사하고 WB/노출 저장→복원→재현상. 원래 사용자 WB 결과와 모든 float 픽셀 일치, 복원한 tone/WB로 전체 해상도 JPEG 출력·재디코딩 및 복사본/원본 해시 유지.
- 기존 독립 LibRaw WB reference와 샘플 정밀도 검증 유지.
- [앱 시작·Core/fixture 해시 기록](verification/2026-10-11-state-startup.json): 5초 정상 시작, stdout/stderr 없음, Core 8개와 fixture 7개 해시 유지.

## 남은 검증과 다음 작업

WB 선택/중립점/Kelvin UI는 아직 없다. version 2를 읽고 재개하는 경로를 연결했으며 현재 Temperature/Tint는 기존 상대 sRGB 보정이다. WB 변경은 전체 원본을 재현상하므로 캐시 기반 실시간 WB 제어는 미완료다. 다음은 카메라 WB 사용자 제어와 캐시, UI 입력 회귀 및 색 정확도 검증이다.

수동 UI 검증은 version 2가 있는 RAW 열기→Custom WB 표시→Original 비교→tone 변경/저장→다시 열기→Reset all adjustments→Undo/Redo→Export 순서로 수행해야 한다. Windows/macOS Intel, GPU/ICC, 클리핑/하이라이트 복원, 전체 메모리·배포 품질도 남아 있다. TASK-011/023을 완료로 표시하지 않는다.
