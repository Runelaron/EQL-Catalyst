# Shared toolbar and pet icon review validation

Focused local iteration, October 23, 2026. Production pet artwork is unchanged.

## Verified

- Release build and self-contained local publish: no warnings or errors.
- 8 core toolbar tests cover state normalization/roundtripping, order, below/above attachment, physical coordinates with DIP dimensions, small work areas, and unchanged saved expanded dimensions.
- 79 native WPF toolbar checks cover real Thumb mouse-down/up capture ownership, capture release before successful completion, cancellation, reversal, Escape, lock/unlock, pending-drag rejection on hide, native/rendered/saved widths, separate panel dimensions, persistent switching, guarded cancellation, reordering, double-click exclusions, minimize/restore, hide/reopen, update-ready placement and separate-process restart.
- 51 current Pet workspace regressions pass, including draft Save/Undo, Save/Discard/Cancel, concurrent changes, slot editing and drops. 235 Pet-related core regressions pass. Test groups can overlap.
- Public configuration builds with private research disabled; its 11-check standard-buff fixture passes. The local Pet workspace fixture confirms private research is enabled.
- Actual synthetic DPS selector samples contain all nine supported families, unresolved generic and three Monster alternatives, at 13/14/16/20/24/32/64 DIP, Clean/Vibrant, Coldain/Sunstrider/Shadowman. Separate grayscale and silhouette boards are included. Candidate review does not change either selector or identity.
- Candidate detail selection uses displayed DIP size. Painted raster decode uses `ceil(DIP × DPI)`; the policy is inspected at 100/150/200/300%. Those are icon checks, not a broad window DPI matrix.

## Findings and limits

The resize regression explicitly uses WPF's normal release order: LostMouseCapture, then DragCompleted(false). Native bounds, rendered bounds and committed dimensions must agree. Capture loss without successful completion restores the drag snapshot. Hiding or minimizing invalidates queued updates. Temporary monitor fitting does not replace saved expanded dimensions.

During preview generation, the legacy capture helper reset the captured window's minimum width. The new review restores its constraints after capture; production toolbar state was not affected. The original test failure is retained in the iteration history, and the final run passes restart at the control-derived minimum width.

The older combined PetContributions review entry point stopped at its obsolete `Pet Details…` fixture selector, before its header checks. It is excluded from passing counts. The current Pet workspace and shared-toolbar suites validate the controls used in this iteration; the older combined fixture needs a separate refresh for intervening draft/character-sheet changes.

WPF checks use real native windows, routed Thumb class handlers and native capture while suppressing physical input pumping during synthetic held gestures. They do not claim physical cursor acceptance in the live game. Physical monitor/DPI transitions, broad window theme/DPI matrices and live game acceptance remain deferred. Geometry tests include negative coordinates, removed monitors through the shared resolver, high-DPI work areas and limited available space.

The gallery's HTML/JavaScript and local references are checked statically; WPF control captures and concept boards are visually inspected. Automated in-app-browser access to local `file:` pages was blocked by browser policy, so no browser-rendered acceptance is claimed.

## Delivery evidence

Machine-readable checks, timings (including outliers), assembly hashes, privacy findings and delivery receipts are retained under the local `test-results/workspace-toolbar` directory. App, profile and private-research backups are verified before local replacement. The running application's draft guard initially cancelled preparation; delivery resumed after the user saved or discarded those edits.

The first live startup check caught a distinct lifecycle case: an explicit open can precede deferred WPF Loaded, so the first save request was skipped. Saving at the end of Loaded fixes initial placement persistence. A regression now opens a fresh toolbar before Loaded, without a second interaction, and requires resolved placement immediately afterward.

Post-startup verification compares combat tables, inventory evidence, research, custom loadouts, independent checkpoints and existing expanded layout records with the stopped backup, and checks the native toolbar and attached panel. The existing hide-when-unfocused preference is preserved; native geometry is checked even when that preference temporarily hides the windows. Deployment and startup receipts are written only after their respective steps complete.

Local delivery completed successfully. The corrected build passes native 28-DIP toolbar/attachment and separately saved placement checks. Combat records, inventory evidence, private research, custom weights/assignments, independent selectors/checkpoints, existing expanded layout records and error logs match the verified stopped backup. Out-of-process geometry inspection uses an explicit per-monitor DPI context to avoid Windows coordinate virtualization.

No game input automation, combat-ingestion changes, public API changes, profile migration, installer building or publication is part of this iteration. Published Beta 1.4 and earlier release artifacts are hash-checked unchanged. Raw personal evidence remains outside source, fixtures and the review gallery.
