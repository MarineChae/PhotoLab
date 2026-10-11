# Row-streamed PNG16 export

2026-10-11, macOS ARM64 / .NET 10.0.102 / libpng 1.6.59.

The app's PNG16 route now writes directly into the atomic store's temporary file. `ImageConvert.WriteSrgb16` fills a caller-owned row using the same transfer/quantization arithmetic as the existing whole-buffer conversion. The additive `ph_png_write` ABI v1 entry owns one native row, libpng's filter/compression state and synchronous row/output callbacks. It never retains the borrowed callback memory or allocates a full encoded image. Existing buffered APIs/entries remain compatible.

`Png16Encoder.Write` catches all managed row/write exceptions inside each callback and returns failure to C. Native error/longjmp/cleanup finishes entirely in C before the original managed exception is rethrown. Source ownership and delegates remain live through the call, and the output stream is left open. Cancellation is checked during row conversion, at native row boundaries and output chunks.

`AtomicFileStore` now accepts a writer task in addition to bytes; the byte API delegates to the same temp/flush/cancellation/rename path. `PhotoExportStore.SavePng16Async` supplies a worker writer after the original/link guard. A partial header/body never replaces the destination. The existing source and adjusted float buffers remain image-sized; this optimization concerns export intermediates.

## Verification

- [Build](verification/2026-10-11-png-stream-build.txt), [353-check aggregate](verification/2026-10-11-png-stream-results.json), [test log](verification/2026-10-11-png-stream-tests.txt).
- Core 16, Imaging 66, Rendering 9, Desktop 104, ColorProfile 9, LittleCMS 32, PNG16Input 32, PNG16Export 33, UI 52. Native bridge also compiles with -Wall/-Wextra/-Werror.
- Streamed bytes match the existing libpng buffered output exactly; the 65,536-code/alpha file and analytic edited-output checks still pass. Six actual CR2/CR3/NEF/ARW/RAF/DNG full-resolution exports and original hashes pass through the new app service.
- Injected write exceptions and cancellation after compressed output begins propagate safely through native callbacks. Previously committed output remains unchanged and partial temporary files are deleted. Byte/sidecar atomic-save regressions also pass.
- [20MP benchmark](verification/2026-10-11-png-stream-benchmark.json) runs buffered/streamed/streamed/buffered in separate processes with identical deterministic random RGB/opaque alpha. Every file is 136,403,465 bytes with SHA-256 `CA928678A5A30C5169883CAA6603596FFC13E384A1311715B8C5A5CF91783531`.

| Metric | Buffered | Streamed |
|---|---:|---:|
| Managed allocation during encode/write | 296,404,568 bytes | 66,920 bytes |
| Encode + file write | 7.764–7.935s | 7.823–7.867s |
| Process peak RSS | 860.9–861.4MB | 740.0–743.2MB |

Timing excludes source creation/hash but includes encoding and file write. Peak RSS includes the identical source constructor's temporary float array, runtime and native allocation; it is not incremental encoder memory. Four runs demonstrate this input's allocation improvement and approximately unchanged runtime, not general photo performance. The SPEC's 20MP JPEG target and full editor/RAW-decode memory remain unproven.

```bash
dotnet run --project tests/PhotoLab.Desktop.Tests -c Release -- --png-export-benchmark /path/to/libPhotoLab.NativePng.dylib streamed
```

[Updated package/startup](verification/2026-10-11-png-stream-package.json), [ARM64 publish](verification/2026-10-11-png-stream-publish.txt): the macOS development app contains the streaming entry and verified native signatures. Core sources remain unchanged. No new dependency or library license is introduced; existing color-package notices remain included. Windows, actual OS picker, clean-machine distribution, broader performance corpus, full-app memory and the full project goal remain pending.
