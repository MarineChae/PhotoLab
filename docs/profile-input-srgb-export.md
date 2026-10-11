# Source-profile input and sRGB export

The previous standard-image float path read SKBitmap colors into RGBA8 and then applied the sRGB transfer function. That 8-bit boundary could not retain negative or above-1 working RGB. The float loader now keeps the decoded bitmap's source profile through Skia's image readback into linear sRGB RGBA32F. RGB outside the working gamut remains negative or above 1 until the display/export boundary. This does not assume that all previous Skia color access ignored profiles.

The legacy RGBA8 full/preview APIs explicitly decode into sRGB. JPEG/PNG encoding now declares sRGB, matching the pixels from `ImageConvert.ToSrgb8`; copying a source P3 ICC onto converted sRGB output would describe the wrong colors.

## Scope and engine

This first input/export step uses existing SkiaSharp 3.119.4 and its color conversion; it adds no library. It does not replace the SPEC's LittleCMS plan. LittleCMS profile validation/management, monitor conversion, rendering intent, full ICC coverage, input-profile metadata preservation and 16-bit standard-image decode/export remain unfinished.

Skia codec direct RGBAF32 and RGBA16161616 requests were rejected as InvalidConversion for the tested ordinary PNG. The implemented path therefore keeps the existing 8-bit source decode, followed by float profile conversion. It does not claim 16-bit PNG preservation. The float readback and owned-buffer copy add large-image transient memory, which needs further optimization/measurement. RAW uses the existing 16-bit LibRaw→float path.

## Evidence

Generated a real PNG with an embedded Display P3 profile and wrote encoded P3 bytes directly, avoiding SKColor's sRGB interpretation. The P3 red primary maps to linear sRGB approximately `(1.22483, -0.0420455, -0.0196277)` and remains unclipped in the app buffer. Neutral gray/straight alpha and the app loader connection passed. Export JPEG/PNG both decode with an actual sRGB declaration. Untagged RGB retains the existing sRGB assumption.

The independent expected primary comes from [ICC Display P3 chromaticities/transfer/white point](https://registry.color.org/rgb-registry/displayp3) and [ICC sRGB encoding](https://registry.color.org/rgb-registry/srgb). Conversion uses [SKCodec output decoding](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skcodec.getpixels) followed by float image readback. This is not an absolute color chart/monitor fidelity test.

The source files are read-only, and temporary test fixtures are deleted. Existing RAW/float/GPU/export/history/editor regressions are rerun because color declarations affect all encoders and standard-image input.

```bash
dotnet run --project tests/PhotoLab.Desktop.Tests -c Release -- --color-profile
```

[Results](verification/2026-10-11-color-input-results.json) · [Tests](verification/2026-10-11-color-input-tests.txt) · [Build](verification/2026-10-11-color-input-build.txt) · [Hashes](verification/2026-10-11-color-input-hashes.json)

Release solution build and 666 CPU/GPU/variant checks passed on macOS ARM64, including 9 profile checks and prior actual-RAW/export/editor regressions. Core and all RAW fixture hashes match. Windows and the remaining color-management scope are not marked complete.
