# Read-only photo-folder navigation

2026-10-11, macOS ARM64 / .NET 10.0.102.

The library pane provides Open folder, a bounded-height photo list and previous/next controls. `PhotoFolder.ScanAsync` enumerates only the selected directory on a worker, filters through the existing Core `PhotoFormats.CanOpen` and sorts filenames deterministically. No decoding, metadata writes or source modification occurs during discovery. An extension is only a candidate; the existing decoder decides actual compatibility on selection. Subdirectories are not scanned recursively.

`OpenFolderAsync` is the same local-directory route used by the folder picker and isolated tests. New requests cancel prior scans and pending photo loading and invalidate old preview publication. The list remains selectable while image loading so newer selections supersede older work through existing loader/render gates. Only the newest discovery replaces the list; missing/corrupt/empty results leave the last accepted image available. Failed photo selection restores the accepted selection. Explicit file opens synchronize the existing list when the photo is in that folder.

Folder navigation retains accepted settings and camera WB in a window-owned dictionary, keyed by full path and `PhotoSourceStamp`. Returning to an unchanged file restores its parameters with a fresh history instead of another photo's Undo entries. Normal length/mtime changes invalidate a draft. The stamp is lightweight, not a cryptographic identity. Pending unapplied RAW development values are not drafts. This feature retains parameter snapshots, not image-sized caches.

Drafts are session-local and are not written automatically. **Save edits is still needed for persistence before closing**. Per-photo Undo timelines, last-session automatic recovery and conflicts with external sidecar updates need further work. The current implementation does not claim a durable SQLite catalog.

## Verification

- [Build](verification/2026-10-11-folder-build.txt), [360-check aggregate](verification/2026-10-11-folder-results.json), [full log](verification/2026-10-11-folder-tests.txt).
- Core 16, Imaging 66, Rendering 9, Desktop 104, Presets 21, PhotoFolder 7, CPU UI 68, actual Vulkan UI 69.
- Seven discovery cases check candidate extensions/case/order, full paths, read-only source preservation, empty/missing directory, pre-cancellation and 2,000 filename fixtures without image decoding. Those empty filename fixtures are not a camera-format compatibility or large-photo loading benchmark.
- Eight new headless checks open actual encoded PNG files through the editor: first selection, next-photo mouse input, latest rapid selection, corrupt payload recovery, unsaved adjustment restoration with per-photo history reset, failed/empty folder and superseded discovery. Existing camera WB, presets, rendering, close and held-mouse photo-region checks still pass.
- CI now includes discovery on both configured OS jobs, but remote/Windows execution is not verified here.
- [Updated ARM64 publish](verification/2026-10-11-folder-publish.txt), [startup](verification/2026-10-11-folder-startup.json): app maintained five-second startup without errors. Core source hashes remain unchanged. No new dependency/native change.

TASK-026 remains in progress: thumbnails, thumbnail cache, SQLite catalog, large real-photo switching/resource measurements, Windows and native folder-picker interaction are missing. Full professional editing and cross-platform completion remain unproven; the goal remains active.
