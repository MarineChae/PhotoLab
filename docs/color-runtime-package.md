# Automatic color runtime and macOS staging

2026-10-11, macOS ARM64 / .NET 10.0.102 / libpng 1.6.59 / LittleCMS 2.19.1.

`ColorLibraries` first uses a nonblank explicit override, then looks for the platform-native LCMS/PNG16 filenames beside the executable (`AppContext.BaseDirectory`). A shipped color runtime therefore activates high-precision input without environment configuration. A source checkout without bundled files retains its existing Skia path. Invalid explicit paths fail visibly; they do not silently change the selected processing precision.

Automatic LCMS activation exposed a compatibility gap for grayscale ICC input: the native engine accepts RGB profiles. The loader now validates gray ICC through Skia's profile parser and retains its existing source-profile conversion. Invalid gray ICC is reported explicitly. A generated D50/gamma-2.2 gray profile verifies the transfer and adjacent PNG16 samples. This is limited gray coverage, not a complete gray/LUT ICC corpus.

## Package recipe

`scripts/package_color_mac.py` stages native color files into an already published macOS application directory. It builds the PhotoLab PNG bridge against an explicitly selected SDK, copies LCMS and libpng, rewrites the bridge's libpng dependency to `@loader_path`, removes its SDK rpath, checks the host architecture, re-signs the three modified Mach-O files ad hoc and writes a SHA-256 manifest. The installed SDK files are never rewrite targets, and output within an SDK prefix is rejected. License files are copied unchanged.

| File | Purpose | Origin |
|---|---|---|
| libPhotoLab.NativePng.dylib | PNG16 encoded samples | PhotoLab C bridge |
| libpng16.16.dylib | PNG parsing/filter/Adam7 | Installed libpng SDK |
| liblcms2.dylib | ICC→linear float | Installed LittleCMS SDK |
| licenses/libpng-LICENSE.txt | PNG Reference Library notice | SDK LICENSE |
| licenses/LittleCMS-LICENSE.txt | MIT notice | SDK LICENSE |
| color-runtime.json | Host architecture and file hashes | Packaging script |

libpng also uses the macOS system zlib; the staged color libraries otherwise depend only on system libraries. The two external color-library licenses and reviewed uses are described in [PNG16 dependency review](png16-input.md) and [LCMS dependency review](littlecms-input.md). No new NuGet package is introduced. This recipe covers color dependencies; the complete application's installer/distribution review is still pending.

```bash
dotnet publish src/PhotoLab.Desktop -c Release -r osx-arm64 --self-contained false -o artifacts/macos-arm64
python3 scripts/package_color_mac.py --png-prefix "$(brew --prefix libpng)" --lcms-prefix "$(brew --prefix little-cms2)" --directory artifacts/macos-arm64
./artifacts/macos-arm64/PhotoLab
```

## Verification

A framework-dependent published test host was staged, moved into a path containing spaces and run from `/tmp`, with both color overrides and DYLD_LIBRARY_PATH unset. [231 tests](verification/2026-10-11-color-package-results.json) passed: Bundled 9, ColorProfile 9, LittleCMS 32, PNG16 32, Desktop 102, headless UI 47. [Full log](verification/2026-10-11-color-package-tests.txt), [publish build](verification/2026-10-11-color-package-build.txt).

The bundle-specific checks prove automatic selection of both local engines, exact native LCMS output through the actual photo loader, distinct adjacent 16-bit samples/alpha, gray ICC compatibility and invalid-profile errors, override priority and invalid explicit bridge errors. JPEG's independent Skia reference now calls Skia directly because an unset override may select bundled LCMS.

[DYLD load trace](verification/2026-10-11-color-package-dyld.txt) records the actual relocated bridge, libpng and LCMS paths. It does not infer portability just from file presence. Original Homebrew dylib dependency paths were removed from staged copies. [Runtime/package evidence](verification/2026-10-11-color-package-runtime.json) records strict signature verification, dependency graphs, matching notices/manifest, SDK-output rejection and actual app startup without errors for five seconds.

An initial architecture-check command used the wrong lipo argument order and failed before re-signing. The recipe was corrected and staging/signature checks rerun successfully. Earlier generic publish also included multiple runtime architectures, so a separate `osx-arm64` publish is the current application artifact. [ARM64 publish](verification/2026-10-11-color-arm64-publish.txt) and [ARM64 signatures/startup/size](verification/2026-10-11-color-package-arm64.json) identify that artifact at `/tmp/PhotoLab macOS arm64`.

The macOS CI definition now publishes, stages, relocates and runs the bundled test host. Remote CI has not been run. Core source hashes remain unchanged. RAW/GPU native code was not modified or rerun here.

## Remaining scope

The produced app requires .NET 10 and separately installed LibRaw tools for RAW loading. This is a local development distribution, not a self-contained commercial installer. Windows filename resolution exists, but Windows packaging/run, macOS Intel, clean-machine setup, full interactive OS input, complete notices/distribution review, signing identity/notarization/update flow, monitor management, broader ICC LUT references and 16-bit output remain unfinished. The full project goal remains active.
