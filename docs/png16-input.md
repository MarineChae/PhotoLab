# PNG16 source precision

2026-10-11, macOS ARM64 / Apple M4 / .NET 10.0.102 / libpng 1.6.59 / LittleCMS 2.19.1.

`PHOTOLAB_PNG16_BRIDGE` selects the new high-depth PNG input path. The existing app loader uses it for float preview and full-resolution export-source loading. JPEG and PNG8 retain their previous codecs. Without the bridge selection, PNG16 also retains the prior 8-bit Skia decode; automatic packaged activation is unfinished.

## Implementation

`native/PhotoLab.NativePng/decode.c` delegates PNG parsing, filters, tRNS and Adam7 to libpng. It returns native-endian source-encoded RGBA16 with straight alpha, expanding gray to RGB and filling absent alpha with 65535. It applies no gamma, ICC or premultiplication. CRC/truncation errors produce no partial result. The managed callback supports cancellation between native rows. C longjmp handling remains inside the native decoder; mutable cleanup state lives on the heap and pixels are freed on every failed/canceled path.

`Png16Decoder` pins encoded file bytes for the synchronous native call, normalizes borrowed native samples into an owned managed float array, then releases native pixels/module. Pixel pointers never escape that lifetime. Native and managed dimensions/allocation counts are checked. Current compressed-file and image-sized allocations still need large-photo memory measurement.

`ImageConvert.FromSrgb32` reuses the CPU sRGB transfer function for normalized encoded floats and preserves alpha. Source RGB and alpha remain normalized until the color conversion. Source-profile PNG16 may flow directly into LittleCMS without an intermediate bitmap when `PHOTOLAB_LCMS_LIBRARY` is selected; non-ICC gamma/chromaticity retains Skia source-profile float conversion. Other non-sRGB input uses the existing Skia profile path if LCMS is not selected.

## Decoder evidence and precision

The installed SkiaSharp 3.119.4 codec rejected RGBA16161616 and RGBAF32 decode requests. Its successful RGBAF16 decode merged source codes 32768–32771 into the same half-float value. This was observed on real independently generated PNG8 and PNG16 files, rather than inferred from the source's reported default color type.

Initial F32 bitmap color readback also had a maximum sRGB transfer error of 0.0002235. The implemented sRGB path therefore uses the existing analytic CPU transfer instead. The tests do not loosen that comparison to accept the observed error.

The final native suite independently generates real PNG files with all five filters and Adam7 pass packing. It checks every RGB/alpha sample of a 256×256 ramp spanning all 65,536 code values. Every encoded sample equals its ushort/65535 float reference; every sRGB red code stays strictly ordered/distinct after CPU and LCMS conversion. Maximum working RGB error relative to a double analytic transfer is 2.29379e-7 for CPU and 2.97996e-8 for LCMS. Alpha includes exact zero/full/fractional float values. These results establish tested PNG input precision, not arbitrary ICC LUT or monitor color fidelity.

Additional coverage includes odd-sized interlaced gray/RGB/gray-alpha/RGBA, 16-bit tRNS matching and hidden RGB, non-ICC gAMA, P3 extended gamut and adjacent values, original ICC→LCMS, invalid normalized sRGB input, wrong bit depth, cancellation, bad CRC and truncation. The source flows through app loading, float exposure and existing sRGB8 export while its SHA-256 remains unchanged. Native pixel release does not affect returned managed arrays.

## Validation

- C bridge compiled with `-Wall -Wextra -Werror`; whole .NET Release solution build passed.
- [Build log](verification/2026-10-11-png16-build.txt), [regression log](verification/2026-10-11-png16-tests.txt), [351-check aggregate](verification/2026-10-11-png16-results.json), [Core/binary hashes](verification/2026-10-11-png16-hashes.json).
- Core 16, Imaging 57, Rendering 9, Desktop 102, ColorProfile 9, LittleCMS 32, PNG16 32, default UI 47, both-native-configured UI 47. The UI fixtures are ordinary 8-bit files, verifying regression with the selections enabled; PNG16 app precision is checked through the actual loader.
- [Controlled native-row cancellation](verification/2026-10-11-png16-cancellation.json): status 3, null output, zero dimensions. This is separate from timed native GUI interaction.
- RAW/GPU native implementations were unchanged and their large suites were not rerun. Windows and remote CI have not executed this change. Core source hashes match all eight baseline files.

## Dependency and remaining work

The existing installed libpng is used through public C APIs behind Infrastructure. The [official libpng API](https://raw.githubusercontent.com/pnggroup/libpng/libpng16/png.h) defines the decode transformations. Its [PNG Reference Library license](https://raw.githubusercontent.com/pnggroup/libpng/libpng16/LICENSE) permits commercial use/distribution subject to preserving notices and accurately identifying altered source. This bridge is PhotoLab code; libpng source was not copied or modified. No new NuGet dependency is added.

Platform deployment needs the architecture-specific bridge, compatible libpng and zlib runtime and their notices. The build script has macOS and Windows/MSVC recipes, with an explicit Windows import-library override; only macOS compilation/execution has been verified. The macOS CI recipe installs/builds/runs the native suite but has not executed remotely.

16-bit PNG output, TIFF/HEIC high-depth input, default native packaging, broad LUT/gray ICC corpus, monitor management, 20MP memory/performance and Windows remain unfinished. Existing export is encoded sRGB8. Full RGB16 precision is checked for CPU sRGB and LCMS sRGB; wide-gamut Skia fallback has P3/adjacency checks and needs broader numeric reference coverage. The full project goal remains active.
