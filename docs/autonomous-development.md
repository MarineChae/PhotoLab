# 자율 개발 결과 — 고정밀 처리 및 비파괴 편집 기반

검증일: 2026-10-11 (Asia/Seoul). 전체 목표는 진행 중이며 완료로 표시하지 않는다.

## 구현

`PhotoLab.Imaging`은 UI에 독립적인 선형 sRGB(D65) RGBA32F 버퍼, 16비트 입력, float 기본 보정, 표시용 sRGB 변환, alpha-aware 선형 축소를 제공한다. 작은 버퍼는 ArrayPool을 사용하고 큰 RAW 버퍼는 정확한 크기로 할당해 풀 버킷 반올림으로 인한 과도한 메모리 증가를 피한다. 내부 writer는 전체 span을 초기화하고 소유자가 Dispose한다. 렌더 작업이 끝나기 전 소스를 해제하지 않는다.

앱의 RAW 경로는 LibRaw 16비트 PPM을 float로 읽으며 보정 중 8비트 변환을 하지 않는다. 화면 표시와 JPEG/PNG 인코딩 시 sRGB RGBA8로 변환한다. 기존 Core 파일은 변경하지 않았다. 일반 JPEG/PNG는 현재 Skia RGBA8 디코딩 결과를 선형화하므로 16비트 PNG 지원으로 간주하지 않는다.

`PreviewRenderer`는 즉시 요청을 받고 이전 작업을 취소한다. CPU 작업을 직렬화하고, 취소를 무시한 결과도 버전 검사로 폐기한다. 이미지 로딩도 취소와 최신 요청 검사를 적용했다. 입력 중에는 캐시한 600px 미리보기, 180ms 유휴 후 최대 1800px 미리보기를 처리한다. 히스토리는 드래그를 묶고 최대 128개 상태를 유지한다.

`PhotoLab.Infrastructure`의 공통 원본 보호/원자적 쓰기는 내보내기와 JSON 저장에 사용한다. 원본 및 심볼릭 링크 경로를 검사하고, 기존 파일을 직접 잘라 쓰는 대신 같은 디렉터리의 임시 파일을 완전히 기록한 뒤 교체한다. 하드 링크 목적지를 교체해도 원본 inode의 내용은 수정하지 않는다. 전원 손실 시 디스크 영속성까지 검증한 것은 아니다.

편집 파일은 기존 flat PascalCase 보정값에 `version: 1`을 추가한다. 기존 Core reader와 호환되며 버전 없는 기존 파일도 읽는다. 미지원 버전, 잘못된 JSON, 비유한/범위 밖 보정값을 거부한다. 자동 저장과 마지막 사진 자동 재개는 구현하지 않았다.

## 처리 순서와 색 공간

1. RAW: LibRaw에서 카메라 WB, 디모자이킹 및 sRGB primaries 변환 → 선형 16비트 PPM. `-w -o 1 -4`를 사용한다.
2. PPM: big-endian RGB16 → 선형 float RGBA, alpha 1. 일반 RGBA8 입력은 sRGB transfer decode 후 float로 변환한다.
3. 미리보기: 선형 영역 평균 축소. 투명 RGB는 alpha 가중치로 평균한다. 전체 해상도 내보내기는 축소하지 않는다.
4. 기본 보정: 상대 WB/노출 gain → 휘도 기반 하이라이트·그림자 → 0.18 기준 대비 → 채도·생동감. 입력과 출력 모두 선형 sRGB float다.
5. 출력: sRGB transfer encode 및 표시 범위 clipping → RGBA8 → PNG 표시 또는 JPEG/PNG 파일 인코딩.

float 처리에서 1을 넘는 밝기 및 음의 RGB를 유지한다. 유한 float 범위를 넘는 연산은 오류로 처리한다. 현재 하이라이트·그림자는 기존 기본 tone approximation이며 전문 RAW 하이라이트 재구성이 아니다. RAW WB/카메라 색 변환은 이미 LibRaw 단계에서 적용되므로 Kelvin/카메라 공간 WB 조절로 간주하지 않는다.

LibRaw의 `-4` 정의는 [공식 dcraw_emu 소스](https://github.com/LibRaw/LibRaw/blob/master/samples/dcraw_emu.cpp)에 따라 선형 16비트, 자동 밝기 보정 해제다. 설치된 실행 파일은 `/opt/homebrew/Cellar/libraw/0.22.2/bin/dcraw_emu`였다. 기존 자동 밝기/감마 RAW 경로와 결과가 달라질 수 있어 기존 편집값의 시각적 호환성은 추가 검증이 필요하다.

## 검증 근거

[전체 테스트 로그](verification/2026-10-11-tests.txt)와 [집계](verification/2026-10-11-results.json)에 명령 결과를 보존했다. Core 16, Imaging 24, Desktop 69개 검증이 통과했다. Desktop 수에는 실제 카메라 파일의 full-resolution export 검증과 음성 샘플 검증이 포함된다.

- macOS ARM64, .NET SDK 10.0.102 / runtime 10.0.2에서 전체 Release 빌드 성공.
- 재컴파일 시 기존 ToggleButton.Checked/Unchecked의 CS0618 경고 2개가 있다. 최종 증분 빌드는 오류 없이 통과했다.
- 현재 앱을 시작해 5초간 프로세스가 유지되고 stdout/stderr가 비어 있음을 확인했다. 이는 실제 UI 조작 통과를 의미하지 않는다.
- 정밀도: 인접 16비트 값 구분, neutral float bit 보존, 1 초과 노출 복원, 전체 256 sRGB/alpha 값 왕복, 기존 CPU 결과 비교, malformed/truncated PPM, NaN/Infinity 및 float overflow 검증.
- 경합: 취소를 무시하는 CPU 작업을 지연시키고 후속 요청과 대기 요청을 넣어 오래된 결과 폐기·직렬 처리·drain을 검증했다.
- 파일: 직접/정규화/링크 원본 경로 거부, 하드 링크 목적지 교체, 출력·JSON 취소 시 기존 파일 보존, 이전 JSON reader 호환, 버전/잘못된 값 거부를 검증했다.

실제 RAW 샘플은 [raw.pixls.us](https://raw.pixls.us/)의 CC0 표시 파일만 사용했다. [manifest](../tests/fixtures/raw/manifest.json)에 모델·모드·URL·SHA-256을 기록하고 다운로드 해시를 검증했다. 파일은 저장소에 동봉하지 않았다.

| 형식 | 모델 | 디코딩 크기 | 결과 |
|---|---|---|---|
| CR2 | Canon EOS 40D, sRAW2 | 1944×1296 | float 정밀도, 전체 해상도 JPEG 재디코딩, 원본 해시 통과 |
| CR3 | Canon EOS R6 | 3407×2271 | 동일 검증 통과 |
| NEF | Nikon D2H | 2482×1648 | 동일 검증 통과 |
| ARW | Sony ILCE-7S | 2784×1872 | 동일 검증 통과 |
| RAF | Fujifilm FinePix S5000 | 2012×1521 | 동일 검증 통과 |
| DNG | Pentax K-r | 4309×2868 | 동일 검증 통과 |

모델 전체의 압축 모드/펌웨어 지원이나 색상 정확도를 보장하는 결과는 아니다. 추가 Samsung .dng 저장소 샘플은 JFIF JPEG 페이로드라 LibRaw가 거부했다. 그 파일은 `negative_samples`에 보존했으며 카메라 전체의 미지원 판정 근거로 사용하지 않는다.

600×400 선형 float + PNG 미리보기 서비스에 16ms 간격으로 60회 입력했을 때 최종 측정은 입력 중 60프레임, 약 55.7프레임/초, accepted request p95 9.70ms였다. UI Bitmap 디코딩·화면 표시 시간은 제외한 측정이다. 실제 UI 30~60FPS 달성이나 전체 해상도 성능으로 확대 해석하지 않는다.

## 재현

```sh
dotnet restore PhotoLab.slnx
dotnet build PhotoLab.slnx -c Release --no-restore -m:1 -nodeReuse:false
dotnet run --project tests/PhotoLab.Core.Tests/PhotoLab.Core.Tests.csproj -c Release --no-build
dotnet run --project tests/PhotoLab.Imaging.Tests/PhotoLab.Imaging.Tests.csproj -c Release --no-build
dotnet run --project tests/PhotoLab.Desktop.Tests/PhotoLab.Desktop.Tests.csproj -c Release --no-build -- --benchmark
python3 scripts/download_raw_samples.py /tmp/photolab-raw-samples
dotnet run --project tests/PhotoLab.Desktop.Tests/PhotoLab.Desktop.Tests.csproj -c Release --no-build -- /tmp/photolab-raw-samples/cr2.cr2 /tmp/photolab-raw-samples/cr3.cr3 /tmp/photolab-raw-samples/nef.nef /tmp/photolab-raw-samples/arw.arw /tmp/photolab-raw-samples/raf.raf /tmp/photolab-raw-samples/dng.dng --reject-not-raw /tmp/photolab-raw-samples/samsung-mislabeled.dng
```

실파일 테스트에는 LibRaw 설치가 필요하다. Windows에서는 임시 경로를 해당 OS 경로로 바꾸고 `PHOTOLAB_DCRAW_EMU`를 설정해야 한다. CI 정의에는 기본 Core/Imaging/Desktop 테스트를 추가했으며 네트워크/LibRaw가 필요한 실파일 테스트는 자동 실행하지 않는다.

## 남은 작업

TASK-007의 처리 단계 확장 구조, TASK-008~011의 디코더 인터페이스/메타데이터/WB 및 색 정확도, TASK-012~016의 GPU·CPU 결과 검증이 남아 있다. Windows/macOS Intel 실행, 실제 UI 입력, 메모리/누수 측정, ICC, 일반 16비트 이미지, 프리셋/사진 관리/고급 보정/상용 배포도 미완료다. 작업 상태는 [TASKS.md](../TASKS.md)와 [PROGRESS.md](../PROGRESS.md)를 따른다.
