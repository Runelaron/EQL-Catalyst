# Tracker resize recovery and Pet toolbar validation

Local review; published Beta 1.4 packages remain unchanged.

## Repairs and previously missed cases

The resize handle only refreshed its visibility during SizeChanged. Collapse/restore changes ResizeMode separately; hiding and reopening reuses the same window instance, retaining the stale visibility. Handle visibility, enabled state and hit testing now reconcile after lifecycle and resize-mode changes. Lock/unlock also restores the correct resize mode after expanding a locked window.

Pending drag frames are invalidated before collapse, hide, minimization and DPI interruption. Existing Escape/capture-loss cancellation restores its snapshot. Shared transparent and solid workspaces retain their surface policy.

Pet Details inherited fixed header padding and extra margins from the general expander template. Setting Expander.Padding could not change that template. It now uses a scoped 28-DIP header, with a stable border for keyboard focus. Compare pets and Pet weights also have explicit 28-DIP heights. The final wrapped toolbar row has exactly 8 DIP before the lower workspace; hidden details reserve no extra content space.

## Verification

- 1,498/1,498 core regressions passed. The existing affected Pet reviews passed 153 + 49 assertions, including alternating table/pane drags and saved layouts.
- All 110 new lifecycle, pointer-capture/hit-test, native-size, interruption, layout and theme assertions passed against the packaged executable. Its application/core SHA-256 values match the tested assemblies.
- Repeated title-bar collapse/expand, Windows minimize/restore, hide/reopen, resize-mode-only changes, lock/unlock, locked restoration, Escape, capture loss, reversal, coalescing, minimums and DPI invalidation are covered. Combat Lab and a solid companion workspace exercise the shared controller.
- 36 fresh synthetic WPF renders cover Coldain, Sunstrider and Shadowman at 100%, 150%, 200% and 300%, with normal/narrow layouts and expanded/focused Pet Details. No desktop capture or personal profile was used.
- All 36 render capture timings and 17 end-to-end resize observations are retained without trimming. Render median/max: 190.186/413.749 ms. Resize median/max: 98.494/140.960 ms. Capture timings are not interactive frame-rate measurements.
- The historical broad UI harness aborts at its removed `⇅` overlay selector lookup. The previous local payload also aborts there; this is a pre-existing test-harness limitation. It is not counted as a passing suite. Focused shared-window checks and the current Pet reviews pass.

Tests use isolated offscreen native windows, routed Thumb events, actual WPF mouse capture/hit-testing and native rectangle checks. Rendered scale coverage does not claim physical multi-monitor DPI or live-game acceptance. The initial probe also exposed a fixture requesting dimensions beyond Windows' native tracking limit at 300%; native drag fixtures now start within that limit.

## Delivery

Deterministic, path-mapped, self-contained unsigned local build; version remains 1.4.0-beta.1. No public API, profile migration or combat calculation changes. Privacy gates inspect maintained source, packaged runtime and synthetic preview images. Verified app/profile backups precede replacement; restart is graceful. No installer rebuild, GitHub publication, WSL changes or Sandbox launch.

All nine retained Beta 1.4 release artifacts match their recorded hashes.
