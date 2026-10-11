# Portable basic adjustment presets

2026-10-11, macOS ARM64 / .NET 10.0.102.

The editor's **BASIC PRESETS** section provides a name box, saved list, Save new, Preview preset, Apply and Delete. The current format stores all eight existing basic controls: Exposure, Contrast, Highlights, Shadows, Temperature, Tint, Saturation and Vibrance. Camera-specific RAW white-balance coefficients remain with the destination photo. This prevents blindly transferring coefficients between unrelated camera spaces; transferable camera-WB semantics need a future format.

Version-1 JSON resides under the local application-data `PhotoLab/Presets` directory. `PresetStore` accepts an explicit directory for isolated tests. Display names are trimmed, bounded and permit Unicode, but never become paths: a SHA-256 of their uppercase identity determines the filename. Case variants identify the same preset. Save rejects duplicates rather than overwriting previous corrections, and uses the existing atomic temporary-file commit. Deletion removes only the derived preset entry.

Reads require the complete root/schema and all eight settings, reject unknown/duplicate case aliases, unsupported versions, mismatched filename/name, oversized files and invalid numerical ranges. Presets reuse the existing Infrastructure edit-value validator; Core is unchanged. Listing is sorted by name and a missing store does not create files. A malformed store entry reports an error without changing edits.

The UI uses injected/default stores and versions asynchronous list refreshes. Preview selects a temporary render setting under the existing renderer version gate, without changing actual controls or history. Cancel restores the current applied rendering. Saves/exports, including keyboard save, are blocked during preview. Apply uses the existing edit-history route as one Undo step and preserves current camera WB. Opening another photo clears a temporary preview; selection/deletion also restores the applied state. Applied edits survive deleting their source preset.

## Verification

- [Build](verification/2026-10-11-presets-build.txt), [346-check aggregate](verification/2026-10-11-presets-results.json), [test log](verification/2026-10-11-presets-tests.txt).
- Core 16, Imaging 66, Rendering 9, Desktop 104, Presets 21, ColorProfile 9, CPU UI 60, actual Vulkan UI 61.
- Store cases include all-control roundtrip through a new instance, Unicode/separator-safe names, duplicate preservation, invalid name/numbers/ranges, cancellation, six corrupt/ambiguous schema cases, listing/deletion and case-insensitive identity.
- Eight added headless actions use real control mouse/text events: save, preview without commit, cancel, apply preserving camera WB without RAW redevelopment, single Undo, Redo, apply to a second photo without importing WB and delete without resetting edits. Synthetic RAW fixtures exercise UI state; they are not a claim of additional real-camera compatibility.
- Existing WB/coalescing/source-failure/close, editor held-mouse/photo-frame and CPU/Vulkan backend regressions pass. Persistence CI invocation was added for both platform jobs; remote CI/Windows execution is not verified here.
- [Updated macOS ARM64 publish](verification/2026-10-11-presets-publish.txt), [five-second startup](verification/2026-10-11-presets-startup.json). Core source hashes match all eight baseline files; no new dependency/native change.

TASK-025 remains in progress for Windows/native OS coverage and future advanced-control/raw-WB presets. The current format deliberately represents only implemented basic controls; HSL/curves/grading, catalog/batch workflows, monitor management and the full professional-editor goal remain unfinished.
