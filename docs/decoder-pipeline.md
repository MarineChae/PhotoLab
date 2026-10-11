# TASK-007~009 — 처리 단계 및 RAW 디코더 경계

검증일: 2026-10-11 (Asia/Seoul). 최종 목표와 Windows/색상 정확도 검증은 진행 중이다.

## 변경 내용

FloatImageProcessor의 실제 처리 순서를 light(exposure/relative WB), tone(highlights/shadows), contrast, color(saturation/vibrance) 함수로 분리했다. 모두 선형 sRGB float에서 동작하며 alpha를 변경하지 않는다. 곱셈 순서와 수식은 유지해 기존 reference 결과를 보존했다. 중간 버퍼 복사나 새로운 외부 패키지는 추가하지 않았다.

Imaging.Decoding의 IRawDecoder는 RawImage를 비동기로 반환한다. 결과는 float surface와 RawMetadata를 포함한다. 결과를 Dispose하면 픽셀을 해제하고, TakePixels는 소유권을 한 번만 호출자에게 넘긴다. 앱 로더는 미리보기 생성 후 전체 surface를 해제하며, 전체 해상도 로딩 시 surface 소유권을 반환한다. 취소에 반응하지 않은 디코더가 늦게 반환한 surface도 해제한다.

실제 LibRaw 코드는 PhotoLab.Infrastructure.LibRawDecoder로 이동했다. Desktop.LibRawCommandDecoder의 기존 RGBA8/float API는 호환 위임으로 유지했다. PhotoImageLoader는 IRawDecoder를 받아 교체할 수 있으며 기존 3개 반환값의 LoadLinearPreviewAsync도 유지한다. 새 LoadPreviewWithMetadataAsync는 메타데이터를 앱에 전달한다. 기본 legacy RGBA8 호출은 기존 경로를 유지하고, 명시적으로 주입한 디코더는 legacy API에서도 사용한다.

메타데이터는 같은 LibRaw 설치의 raw-identify -v 결과에서 normalized make/model을 읽는다. 이 출력 형식은 [LibRaw 0.22.2 공식 소스](https://raw.githubusercontent.com/LibRaw/LibRaw/0.22.2/samples/raw-identify.cpp)를 기준으로 확인했다. 알려지지 않은 형식은 추측하지 않는다. 보조 도구 미설치·실행 오류·2초 timeout은 RawMetadata.MetadataWarning으로 남기며 기본 디코딩을 거부하지 않는다. 호출자의 취소는 전달한다. 파일명 echo를 제거한 뒤 파싱해 Unix 다중 행 파일명이 카메라 정보를 주입하지 못하도록 했다.

native 프로세스 실행은 두 실제 호출이 공유한다. stdout/stderr를 동시에 읽고 timeout/취소 시 소유한 프로세스 트리를 종료한 뒤 파이프 완료를 확인한다. 파일 경로는 절대 경로로 전달해 native 옵션으로 해석되지 않도록 한다. 카메라 제조사·모델은 앱의 사진 크기 정보에 표시한다. WB 계수/행렬·렌즈·ISO 등의 추출은 이번 구현에 포함하지 않았다.

## 검증

[최신 테스트 로그](verification/2026-10-11-decoder-tests.txt) 및 [집계](verification/2026-10-11-decoder-results.json): Core 16 + Imaging 30 + Desktop 88 = 134개 검증 통과. Desktop에는 실제 카메라 샘플과 음성 샘플 검증이 포함된다.

- macOS ARM64 Release 빌드 통과. 기존 ToggleButton.Checked/Unchecked CS0618 경고 2개 유지.
- 기존 CPU reference 결과 일치, exposure-before-contrast 및 exposure-before-highlight-mask 수치 검증, 10회 float 결과 재현성 통과.
- RawImage 소유권 이전·중복 이전 거부·해제, 교체 디코더·오류 전달·늦은 취소 버퍼 해제·기존 preview/legacy API 호환 통과.
- normalized camera parser의 CRLF, 불명확한 출력, helper 누락/timeout, 다중 행 파일명 주입 거부 통과.
- CR2/CR3/NEF/ARW/RAF/DNG 6개 실제 샘플에서 카메라 식별, float 정밀도, 전체 해상도 JPEG 재디코딩, 원본 해시 보존 통과.
- Core C# 소스 해시가 이전 체크포인트와 일치함을 확인했다.

카메라 식별 결과는 Canon EOS 40D / Canon EOS R6 / Nikon D2H / Sony ILCE-7S / Fujifilm S5000 / Pentax K-r이다. 이는 특정 파일의 결과이며 모델 전체 압축 모드나 색상 정확도를 증명하지 않는다. 샘플 출처와 해시는 [manifest](../tests/fixtures/raw/manifest.json)에 있다.

최종 서비스 측정은 600×400 float+PNG, 입력 60회 중 60개 갱신, 약 55.6프레임/초, p95 10.26ms였다. 실제 UI Bitmap 디코딩/화면 표시 시간은 제외한다. RAW 테스트 시간 로그는 메타데이터 미리보기 로딩과 별도 전체 디코딩·내보내기를 모두 포함한다.

## 남은 작업

Windows/macOS Intel 런타임, 실제 UI 조작, WB/색행렬 및 기타 EXIF, 색상 정확도·ICC, GPU/SIMD, 전체 앱 메모리와 배포 검증은 남아 있다. 카메라 메타데이터 helper는 선택적이며 버전별 출력 호환성은 추가 검증이 필요하다. 완료 상태는 [TASKS.md](../TASKS.md)와 [PROGRESS.md](../PROGRESS.md)를 기준으로 유지한다.
