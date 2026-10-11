# Live edited-preview histogram

2026-10-11, macOS ARM64 / .NET 10.0.102.

`ImageHistogram` owns four 256-bin count arrays detached from the image buffer. It measures the adjusted linear sRGB preview before display quantization. RGB bins use `floor(value*256)`, clamped to bins 0/255; linear Y uses 0.2126 R + 0.7152 G + 0.0722 B. Fully transparent pixels are excluded, while partial-alpha pixels count once. Each series sums to `PixelCount`. Pixels with any RGB channel ≤0/≥1 contribute once to the corresponding clipping count, including exact endpoints and extended-gamut values.

These are working-preview channel boundary indicators, not proof of sensor clipping or unrecoverable original highlights. The view labels the domain as linear RGB/Y and counts with a shared logarithmic vertical scale. It is explicitly **Edited preview**, so Original comparison keeps the current corrected histogram. The histogram describes the accepted 600px interactive or up-to-1800px quality preview, not a full-resolution export histogram.

`PreviewRenderer` computes on the worker and returns histogram plus display pixels in the same versioned frame. Obsolete results/cancellation suppress both. Its owned float result is released after the count arrays and display bytes are complete; no span of released pooled pixels escapes. Existing legacy byte calls convert their result into linear sRGB for the same count definitions. `PreviewFrame` retains its existing two-argument constructor, with histogram added as a property.

`HistogramView` draws RGB/Y line curves in the develop panel; the editor updates data and percentages with the accepted bitmap. New photos and close clear prior histogram data. WB refresh keeps the accepted image/histogram until a replacement is ready.

## Validation

- [Release build](verification/2026-10-11-histogram-build.txt), [test log](verification/2026-10-11-histogram-tests.txt), [309-check aggregate](verification/2026-10-11-histogram-results.json).
- Core 16, Imaging 66, Rendering 9, Desktop 104, ColorProfile 9, CPU UI 52, Vulkan UI 53. Nine new imaging checks cover conservation, partial/zero alpha, exact/extended boundaries, independent Y weights, source disposal, all bin boundaries, exposure-induced clipping and cancellation.
- Renderer checks compare the accepted histogram to independently computed adjusted float output; existing controlled obsolete/noncancellable render checks discard complete frames and their histograms.
- Headless editor checks verify populated counts, actual colored pixels inside the captured Skia graph region and histogram replacement with an accepted WB frame. CPU and actual Vulkan editor variants passed.
- Service benchmark: 600×400, 60 continuous requests, 60 accepted during 1.079s input (55.6/s), accepted p95 7.28ms. Includes float processing/histogram/RGBA8 but excludes UI bitmap/presentation. It is not a paired performance comparison or OS FPS claim.
- Existing headless held-mouse/photo-region checks also passed. They include polling/capture overhead and do not measure native presentation timing.
- [Updated app publish](verification/2026-10-11-histogram-publish.txt), [startup](verification/2026-10-11-histogram-startup.json): macOS ARM64 app runs five seconds without error output. Core source hashes match all eight baseline files. Native RAW/GPU kernels were not changed.

The new algorithm uses existing Imaging/float infrastructure, with no dependency or Core change. Windows, full-resolution distributions, area/downsampling effects on clipping interpretation, broader photo corpus and native OS presentation/performance remain to be verified. TASK-019 remains in progress and the full project goal remains active.
