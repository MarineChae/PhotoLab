# Continuous camera white balance

명시적 `PHOTOLAB_NATIVE_RAW_BRIDGE` 경로에서 RAW Warmth/Tint 슬라이더를 제공한다. 기본 CLI 경로는 텍스트 입력과 Apply를 사용한다. 기존 출력 sRGB Temperature/Tint와 별도이며, 카메라 as-shot 계수에 상대 조절을 적용해 RAW를 재현상한다.

## Input and ownership

- 실행 중인 WB 미리보기 하나와 최신 대기 값 하나를 유지한다. 슬라이더는 현상 중에도 입력을 받으며 중간 대기 값은 합친다.
- 드래그 중 성공한 미리보기를 표시하되 최신 thumb 위치를 이전 결과로 되돌리지 않는다. 성공한 상태만 히스토리에 기록하고 전체 드래그를 하나의 Undo로 묶는다.
- 드래그와 WB 작업이 끝나면 기존 180ms idle 품질 승격을 사용한다. 처리 중 저장/내보내기/히스토리 버튼은 비활성화한다.
- 사진 열기와 닫힘은 입력 세대를 변경하고 대기 값을 버린다. 취소를 무시한 이전 현상 결과도 기존 로더 소유권 규칙으로 해제한다.
- 저장된 계수를 상대 조절값으로 표현할 수 없으면 thumb를 비활성화한다. 저장은 계수를 보존하며 명시적 입력/As shot으로 다시 조절할 수 있다.

## Evidence

제어된 헤드리스 입력은 연속 입력 합치기, pointer release 이전 픽셀 표시, 최신 값 선택, 단일 Undo/Redo, 미표현 WB 보존, 사진 전환과 닫힘의 늦은 결과 해제를 검증한다. 역산 값은 float 계수에서 계산하므로 Redo 위치 검사는 0.0001 허용 오차를 사용한다.

실제 CR2/CR3/NEF/ARW/RAF/DNG 6종은 마우스를 누른 상태에서 미리보기 갱신, 놓은 뒤 1800px 결과와 독립 CLI 출력의 byte 일치, 단일 Undo를 검증했다. 기본 원본 비교와 Apply 경로 검사도 유지했다.

첫 드래그 샘플부터 Bitmap 교체까지 헤드리스 관찰값은 CR2 161.83ms, CR3 140.13ms, NEF 131.71ms, ARW 129.09ms, RAF 102.78ms, DNG 106.64ms였다. 각 파일 한 번의 관찰이며 입력/현상/float 처리/PNG/Bitmap/최대 10ms polling을 포함한다. 워밍업, 동시 작업, OS 화면 합성의 영향을 분리한 벤치마크나 FPS 증거는 아니다.

Windows native bridge, 네이티브 OS 드래그/표시, 연속 입력 FPS/p95, 더 큰 RAW, ICC와 기준 색 정확도는 남아 있다. 다음 성능 작업은 프레임 경로의 PNG 인코딩/디코딩 비용을 분리 측정하고 픽셀 버퍼 직접 표시를 검토한다.

[검증 집계](verification/2026-10-11-continuous-wb-results.json) · [검증 로그](verification/2026-10-11-continuous-wb-tests.txt) · [빌드](verification/2026-10-11-continuous-wb-build.txt) · [시작/해시](verification/2026-10-11-continuous-wb-startup.json)
