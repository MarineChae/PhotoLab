# TASK-011 — native 세션의 앱 로더·화면 연결

검증일: 2026-10-11. 전문 RAW 편집기 전체 목표는 진행 중이다.

## 변경

PHOTOLAB_NATIVE_RAW_BRIDGE에 빌드한 연결 라이브러리의 절대 경로를 지정하면 기본 PhotoImageLoader가 SessionRawDecoder를 사용한다. 지정하지 않은 실행과 legacy RGBA8 API는 기존 동작을 유지한다. 선택한 bridge의 로딩/현상 오류는 UI 오류로 보고하며 이전 사진 상태를 보존한다.

SessionRawDecoder는 카메라 기본 WB를 기존 디코더로 읽고 메타데이터를 유지한다. custom WB 요청은 하나의 native unpack 세션에서 반복 현상하며 미리보기와 전체 해상도 로딩/내보내기가 세션을 공유한다. 수명·직렬화는 native 단계와 별도로 관리한다. 늦게 반환된 세션이나 픽셀은 취소 확인 후 해제한다.

PhotoSourceStamp는 정규화된 경로·파일 길이·UTC 수정 시각으로 일반적인 외부 변경을 감지한다. 변경하면 이전 세션/메타데이터를 폐기하고 다시 읽는다. 명시적인 사진 다시 열기와 일반 이미지 전환도 캐시를 해제한다. 같은 길이와 수정 시각을 유지한 외부 수정까지 감지하는 암호학적 identity는 아니다.

동일 사진의 custom WB 변경은 기존 Original 비교 bitmap을 재사용한다. 새 미리보기를 만드는 동안 이를 해제하지 않도록 소유권을 분리했다. 원본 변화나 명시적 다시 열기 시 새 Original을 만든다. As shot은 기존 카메라 기본 WB semantics를 유지한다. 창 닫힘은 렌더러와 loader 작업 종료 후 native cache를 비동기로 해제한다.

이번 연결은 전체 native 현상을 사용해 1800px 품질 미리보기를 만든다. half-size API를 아직 UI의 600px WB 입력/idle 업그레이드에 연결하지 않았다. 기존 tone 조작의 600px 캐시는 유지하며 새로운 WB 입력의 30~60FPS를 주장하지 않는다. Core 소스는 수정하지 않았다.

## 실행

```bash
python3 scripts/build_native_raw.py --libraw-root /opt/homebrew/opt/libraw --output /tmp/photolab-native
PHOTOLAB_NATIVE_RAW_BRIDGE=/tmp/photolab-native/libPhotoLab.NativeRaw.dylib dotnet run --project src/PhotoLab.Desktop/PhotoLab.Desktop.csproj -c Release
```

새 SDK나 바이너리는 번들하지 않았다. [native bridge의 ABI·라이선스·플랫폼 범위](native-raw-session.md)를 따른다. Windows native SDK/빌드·배포는 별도 검증이 필요하다.

## 검증

[빌드](verification/2026-10-11-cache-loader-build.txt), [테스트 로그](verification/2026-10-11-cache-loader-tests.txt), [집계](verification/2026-10-11-cache-loader-results.json): Core 16 + Imaging 51 + Desktop 206 + UI 17 + NativeSession 80 + CachedLoader 39 + NativeUI 18 = 427개 검사 통과.

- 로더 반복 WB의 metadata/session 재사용, 파일 변경 시 폐기, 명시적 다시 열기, 늦은 open/픽셀 취소와 해제, 중복 Dispose, dispose 후 요청 거부.
- 실제 CR2/CR3/NEF/ARW/RAF/DNG의 native 앱 로더 미리보기·전체/내보내기 픽셀이 CLI와 일치. preview/export가 한 세션을 공유하고 Reset 후 다시 열어도 같은 결과 유지.
- 실제 RAW 6종을 기본 EditorWindow에 열고 헤드리스 텍스트·마우스 입력으로 WB 50/20 적용. 화면의 RGBA8 픽셀이 CLI 품질 미리보기와 일치하며 Original 비교와 Undo도 일치.
- 기존 전체/half native 비교·소스 삭제 후 cache 사용·취소/소유권·저장/복원 회귀 유지.
- [시작·해시](verification/2026-10-11-cache-loader-startup.json): native opt-in 앱 5초 정상 시작, Core 8개와 fixture 7개 해시 유지.

헤드리스 입력은 실제 OS의 파일 선택기·키보드/마우스·창 표시·모니터 ICC 검증과 구분한다. 실파일 화면 검증에서 Save 버튼으로 fixture 디렉터리의 파일을 바꾸지 않았다.

## 다음 작업

half-size interactive WB 요청, 최신 결과 우선 처리와 idle 전체 품질 갱신을 연결해야 한다. 세션 무효화 시 원본 snapshot과 화면의 처리 상태가 일치하는지 추가 검증하고, UI FPS·전체 앱 메모리를 측정한다. 중립점/Kelvin·sensor-float/ICC·GPU, Windows와 상용 배포 검증도 남아 있다. TASK-011은 진행 상태를 유지한다.
