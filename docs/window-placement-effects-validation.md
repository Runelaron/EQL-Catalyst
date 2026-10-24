# Persistent placement and Pet Exaltation validation

Local verified build. Published Beta 1.4 artifacts remain unchanged.

## Resize failure and the previously missed release order

The old test reported DragCompleted before releasing capture. Actual WPF Thumb mouse-up clears IsDragging, releases capture, then reports DragCompleted(false). The resize controller treated that normal LostMouseCapture as cancellation and restored the old snapshot before completion. Official lifecycle: [WPF Thumb source](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/Primitives/Thumb.cs#L230-L249).

The pre-repair routed Thumb probe reproduced 900 -> 960 while dragging -> 900 after release. The packaged repair retains 960; a reversal retains 935. Capture loss is deferred only as a fallback, allowing normal or cancelled Thumb completion to win. Escape and genuine capture loss still restore the starting snapshot. Generation checks reject obsolete queued frames.

The new fixture uses actual Thumb mouse-down/up class handlers and real WPF capture. Movement uses controlled routed DragDelta events and frame application while dispatch of unrelated physical released-button reports is suppressed. Native rectangles, rendered surface dimensions and persisted widths agree after release. This is stronger lifecycle coverage than the old synthetic completion order, but does not claim OS-generated physical mouse moves.

## Placement

Tracker and Combat Lab use their existing character layout identifiers, independently. New defaults are 915 and 878 DIP wide; heights and minimums remain. Optional position, monitor origin/identity and DPI fields extend existing layout records. Completed operations, settled geometry, hide and shutdown flushes save normal placement. Native iconic/zoomed bounds and title-bar height cannot replace expanded dimensions. Save/restore does not rewrite table widths or pane proportions.

Separate-process restart tests restore exact logical dimensions and physical position. A second collapsed restart caught reentrant startup placement leaving a collapsed window at full height; guarded restoration now retains its 28-DIP face and expanded saved height. Legacy, invalid, negative-screen, changed-DPI and unavailable-monitor cases are covered; title bars remain reachable. Real hardware monitor transitions remain acceptance work.

## Pet effects and controls

Catalog intrinsic effects no longer enter active Pet equipment unless explicitly selected. Saved assignments and export observations are retained. A versioned shared policy resolves cell/tooltip order, effective assignments, rank locks, comparison contexts and cache identities. Weapons lead with Proc; armor uses Focus first. Catalog reference effects remain labeled as not selected. Self effects retain their existing behavior.

All five recognized types, including Ornamentation, enforce one selected assignment. Identical observed effects deduplicate; conflicting legacy records remain intact but their entire type is inactive. Save errors explain how to remove or replace a duplicate, and unaffected types continue. Canonical numbered-slot state is used by the direct editor and Exaltation assignment actions. Visual review exposed an inspector retaining an old immutable copy after an effect refresh; refreshed rows now replace that snapshot while preserving selection and temporary rank. Three theme assertions require both conflicting effects to appear inactive without reselection. Dormant Secondary, replacement, planned versus recorded state and complete Undo snapshots are covered.

Inventory is a title above Options; Options and Dual wield retain aligned controls. Commands are exactly Edit selected and Remove selected.

## Verification results

- 1,515/1,515 core regressions passed in 7.24 seconds. Two old expectations that automatically activated intrinsic procs were replaced with explicit-selection/recorded-effect cases. The weapon-order fixture was also corrected.
- 63 new placement/effect assertions passed against the packaged executable, plus 110 shared resize/toolbar assertions and 153 + 49 related Pet assertions: 375 total. Application and core assembly hashes match the tested payload.
- Reversal, Escape, capture loss, hide/reopen, collapse/minimize recovery, independent layout keys, exact-position restart and collapsed restart are covered. Existing shared-window tests cover interrupted drags, locking and resize-mode changes.
- 60 synthetic WPF images cover Coldain, Sunstrider and Shadowman at 100%, 150%, 200% and 300%: default Pet, narrow Pet, conflicts, full effect text and Combat Lab. No personal profile or desktop image is used. Scale renders exercise layout and text rendering; they do not establish physical multi-monitor acceptance.
- All 60 capture timings/allocations and four drag observations are retained, without trimming. Render median/max: 98.820/223.921 ms; max allocation: 7,376,312 bytes. Drag median/max: 47.540/50.064 ms; max allocation: 465,816 bytes. These include review/capture work and are not interactive frame-rate measurements.
- The historical broad UI harness has a pre-existing removed overlay-selector failure on both old and new payloads. It is not counted as passing; current affected suites above pass.

## Delivery boundaries

Self-contained, deterministic, path-mapped unsigned local runtime. Version remains 1.4.0-beta.1. No profile schema migration, public API, installer, combat-engine or game-file change. Optional layout/assignment fields preserve existing identifiers and saved data. Privacy gates inspect maintained source, runtime and synthetic images. Verified app/profile backups precede graceful local replacement and restart. Public Beta 1.4 and older retained Beta 1.1 assets are hash-checked and untouched. No GitHub write, WSL change or Sandbox launch.

Live-game acceptance and physical multi-monitor interaction remain separate.
