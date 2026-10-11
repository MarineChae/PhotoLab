# TASK-009/011 — unpack 캐시와 native WB 미리보기

검증일: 2026-10-11. 전체 전문 RAW 편집기 목표는 진행 중이다.

## 구현과 경계

IRawSession/NativeRawSession은 원본을 한 번 open/unpack하고 LibRaw rawdata를 유지한다. custom WB 전체 현상과 half-size 미리보기를 같은 세션에서 반복할 수 있다. open 후 파일 스트림을 해제하며 결과는 세션과 독립적인 FloatImageBuffer를 소유한다. 현재 앱 기본 디코더는 기존 CLI 경로이며 이 API를 아직 UI에 연결하지 않았다.

설치 SDK의 LibRaw 헤더로 빌드하는 작은 C++ 연결 계층을 추가했다. 버전 의존 LibRaw 구조체는 C# ABI를 건너지 않는다. ABI에는 opaque handle, scalar WB, callback과 RGB16 payload 포인터/치수만 전달한다. SDK 헤더와 runtime의 숫자 버전을 검사하며 같은 SDK·컴파일러 ABI의 사용이 필요하다. C++ 예외는 연결 계층에서 오류 코드로 변환한다.

현상은 custom WB, +M에 해당하는 카메라 행렬, sRGB, 16비트, gamma 1/자동 밝기 없음 설정을 사용한다. 원본 처리 순서와 재현상 시 rawdata 복원은 [LibRaw dcraw_process](https://raw.githubusercontent.com/LibRaw/LibRaw/0.22.2/src/postprocessing/dcraw_process.cpp)와 [raw2image](https://raw.githubusercontent.com/LibRaw/LibRaw/0.22.2/src/preprocessing/raw2image.cpp)를 확인했다. [메모리 이미지 API](https://raw.githubusercontent.com/LibRaw/LibRaw/0.22.2/src/postprocessing/mem_image.cpp)의 native-endian RGB16을 opaque RGBA32F로 확장한다. 8비트나 RGBA16 중간 버퍼는 사용하지 않는다.

half-size→600px preview는 LibRaw의 빠른 half-size 결과를 선형 float로 축소한다. 초기 구현에서는 CR3 결과가 기준과 0.136 정도 달랐다. open 단계가 홀수 Bayer active 폭/높이를 짝수로 자르는 [공식 코드](https://raw.githubusercontent.com/LibRaw/LibRaw/0.22.2/src/utils/open.cpp)를 확인하고, half 현상에만 동일한 경계를 임시 적용했다. RAII로 원래 active 크기를 복원해 이후 전체 현상에 영향을 주지 않는다. 카메라 픽셀은 수정하지 않는다.

## 수명·취소

각 세션의 작업은 직렬화한다. SafeHandle은 세션을 destroy한 다음 연결 라이브러리를 해제한다. 처리 이미지 메모리는 float 복사 후 finally에서 해제한다. DisposeAsync는 소유자 취소를 요청하고 native 작업과 변환이 끝난 뒤 해제하며 UI thread에서 기다리며 블록하지 않는다. 반복 Dispose는 같은 완료 작업을 반환한다.

LibRaw의 중간 취소/치명적 오류는 rawdata를 recycle할 수 있다. 변경된 WB로 대체된 요청은 실행 전 취소하거나 native 처리 후 결과를 폐기해 캐시를 유지한다. 소유자 Dispose만 progress callback으로 native 작업을 중단한다. 오류로 캐시가 무효화된 세션은 재개하지 않고 원본을 다시 열어야 한다. open/unpack 취소는 callback으로 전달한다. callback을 호출하지 않는 native 구간의 즉시 중단이나 timeout은 보장하지 않는다.

## 의존성과 빌드

새 NuGet 패키지는 없다. 이 연결 계층은 설치된 LibRaw 0.22.2-Release reentrant 공유 라이브러리에 동적으로 연결했다. native 바이너리·SDK·LibRaw 소스는 저장소나 앱에 번들하지 않았다. LibRaw는 [LGPL 2.1 또는 CDDL 1.0](https://raw.githubusercontent.com/LibRaw/LibRaw/0.22.2/src/libraw_c_api.cpp)을 제공한다. 배포 시 선택한 라이선스의 고지/소스 제공 등 조건, 추가 코덱, SDK와 compiler ABI를 검토해야 한다. 상용 배포 검증 완료를 의미하지 않는다.

macOS 빌드 및 테스트 예:

```bash
python3 scripts/build_native_raw.py --libraw-root /opt/homebrew/opt/libraw --output /tmp/photolab-native
dotnet run --project tests/PhotoLab.Desktop.Tests/PhotoLab.Desktop.Tests.csproj -c Release -- --native-session /tmp/photolab-native/libPhotoLab.NativeRaw.dylib /path/to/photo.NEF
```

Windows용 스크립트 분기는 Developer Command Prompt의 cl 및 include/lib/libraw_r.lib SDK를 요구한다. SDK의 runtime DLL도 로더가 찾을 수 있어야 한다. Unicode 경로는 UTF-8→wide 변환 후 native wide API로 연다. Windows/Linux 빌드 분기는 현재 환경에서 실행하지 않았다.

## 검증과 측정

[전체 로그](verification/2026-10-11-session-tests.txt), [집계](verification/2026-10-11-session-results.json), [빌드](verification/2026-10-11-session-build.txt): Core 16 + Imaging 51 + Desktop 206 + UI 17 + NativeSession 80 = 370개 검사 통과.

- 실제 CR2/CR3/NEF/ARW/RAF/DNG에서 전체 현상과 CLI, half preview와 독립 CLI -h 기준의 최대 float 차이 모두 0.
- 소스 복사본 삭제 후 A→B→A가 동일한 픽셀을 복원해 추가 파일 읽기 없이 cache 사용을 확인.
- 공백·한글이 포함된 복사본 경로에서 macOS UTF-8 open/unpack 검증.
- full→half→full 결과 유지, pre-cancel 및 대체 요청 후 캐시 유지, 동시 Dispose/작업 종료, 출력의 독립 소유권과 반복 해제.
- RGB16 확장의 치수·정밀도·alpha·취소 검증, 기존 실제 RAW/파일 저장/헤드리스 입력 회귀 유지.
- [시작/해시](verification/2026-10-11-session-startup.json): 앱 시작 5초 정상, Core 8개·fixture 7개 해시 일치.

아래는 macOS ARM64의 단계별 단일 측정(ms)이다. CLI endpoint에는 subprocess·PPM·메타데이터가 포함되고 native 단계에는 native 처리/managed 복사/float 변환이 포함된다. half/600px에는 축소도 포함하지만 PNG·tone·UI 표시와 시작 unpack은 제외한다. 순수 unpack 캐시 효과의 배율이나 앱 FPS로 해석하지 않는다.

| 형식 | CLI endpoint | open/unpack | 반복 전체 | half/600px |
|---|---:|---:|---:|---:|
| CR2 | 241.9 | 61.1 | 15.2 | 24.0 |
| CR3 | 196.0 | 21.2 | 131.9 | 25.3 |
| NEF | 168.1 | 38.0 | 82.3 | 12.3 |
| ARW | 163.1 | 17.5 | 96.9 | 14.8 |
| RAF | 102.1 | 5.2 | 42.8 | 14.9 |
| DNG | 395.8 | 139.1 | 187.3 | 32.5 |

## 다음 작업 / 남은 범위

다음은 명시적인 SDK/bridge 선택과 원본 snapshot 수명 관리를 포함한 UI session 연결, 최신 WB 요청 처리와 idle 전체 품질 갱신이다. native API 결과는 여전히 LibRaw의 16비트 sRGB 출력이므로 그 이전 클리핑을 되돌리지 못한다. sensor-float 작업 공간, 중립점/Kelvin, ICC/색 정확도, 30~60FPS GUI 측정, 메모리 장시간 검증, 추가 RAW 모드와 Windows/GPU/배포 품질은 남아 있다. TASK-009/011을 완료 표시하지 않는다.
