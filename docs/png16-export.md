# PNG16 export from working float

2026-10-11, macOS ARM64 / .NET 10.0.102 / libpng 1.6.59.

The editor has an opt-in **16-bit PNG** checkbox. Select it and choose PNG in the existing Export file picker to save a high-depth output. JPEG and unchecked PNG retain the previous byte export behavior. The precision choice changes neither editing history nor preview. The current macOS ARM64 development app at `/tmp/PhotoLab macOS arm64` includes the updated encoder; a source build needs the latest PNG bridge.

## Pipeline and ownership

`ImageConvert.ToSrgb16` converts working linear sRGB float directly into source-encoded RGBA16. RGB is clamped to the output gamut only at this boundary, passed through the shared sRGB transfer function and rounded to 0..65535. Alpha is quantized as linear coverage, retaining hidden color in transparent pixels. The working buffer is never modified. Existing RGBA8 conversion uses the same unchanged transfer arithmetic.

`ph_png_encode` is an additive entry in the existing v1 native bridge. libpng writes non-interlaced RGBA16 with sRGB/gAMA/cHRM declarations, handles filtering/compression/CRC and converts native-endian samples to PNG byte order. A bounded native output buffer grows as needed; failure/cancellation frees its partially written data. All longjmp/error cleanup remains inside C. Managed code pins the ushort array during the native call, copies complete encoded bytes, then frees the native result/module. Cancellation is checked during conversion, before each native row and before returning output.

`PhotoExportStore.SavePng16Async` checks original/link paths, encodes on a worker and commits via the existing atomic file store. Failed/canceled encoding leaves the previous destination intact. The editor keeps its full-resolution adjustment snapshot and camera-WB source route. Its default byte-export adjusted buffer still ends its lifetime inside the worker before the file write, matching previous behavior.

## Evidence

- [Build](verification/2026-10-11-png16-export-build.txt), [regression log](verification/2026-10-11-png16-export-tests.txt), [334-check aggregate](verification/2026-10-11-png16-export-results.json).
- Every RGB/alpha sample in a 256×256 65,536-code fixture survives float conversion→native PNG16→libpng decoding exactly. The decoder is independently checked against hand-generated PNG16 filter/Adam7 fixtures in the input suite.
- Actual PNG IHDR declares 16-bit RGBA and dimensions; Skia recognizes the sRGB output declaration. Edited output agrees with a double analytic sRGB reference within one code; synthetic maximum observed error was 7.70475e-6 in normalized encoded RGB.
- HDR/negative RGB is clipped at output without modifying working values; exact 16-bit straight alpha and adjacent edited midtones survive.
- Direct/symlink original overwrite is rejected. Pre-cancellation/native-load failure preserves the destination, original hash and absence of temporary files.
- Actual CR2/CR3/NEF/ARW/RAF/DNG full-resolution float loading→exposure→PNG16 output agrees at every channel with the independent analytic quantization bound; dimensions and each original SHA-256 are preserved. The 25.863-second export test group includes all six files, references and synthetic/failure checks, not a single-image export benchmark.
- Two new headless mouse/control checks verify the default precision and selection without image/history changes. UI total is 49; native OS save-dialog interaction remains untested.
- [Controlled native encode cancellation](verification/2026-10-11-png16-export-cancellation.json): cancellation after the first row returns status 3, null output and zero bytes.
- [Updated package evidence](verification/2026-10-11-png16-export-package.json), [ARM64 publish](verification/2026-10-11-png16-export-publish.txt): encode entry, native signatures/manifest and five-second app startup verified. Core source hashes remain unchanged.

The [official libpng write example](https://raw.githubusercontent.com/pnggroup/libpng/libpng16/example.c) documents header setup, transformations after `png_write_info` and write-row/end lifecycle. The existing [color-runtime package](color-runtime-package.md) includes the libpng notice; no new dependency is introduced. macOS CI now invokes the export suite, but remote execution remains unverified.

## Remaining scope

Encoding currently allocates a full RGBA16 array, growing native compressed output and a managed encoded copy alongside the working buffers. 20MP memory/performance and row-streaming export need measurement/implementation; this change does not establish the SPEC's performance target. OS file picker, Windows runtime/packaging, clean-machine installation, monitor transforms, full ICC/LUT fidelity, EXIF retention and TIFF remain pending. RGB gamut mapping beyond output clipping is also unfinished. The full project goal remains active.
