# Cascading controls and character selection

Introduced in Beta 1.1. Existing profiles and saved layouts remain compatible.

## Controls

- Drag a table boundary or panel handle. The section in the drag direction shrinks first; more distant neighbors contribute space only after nearer ones reach their minimums. Dragging stops at the combined minimums. Text retains its font size and uses ellipses with full-value tooltips.
- Panel handles brighten on hover or keyboard focus and use the theme's gold interaction color while captured. Arrow keys make fine panel adjustments.
- Tab dashes appear only on an unselected tab header when hovered or keyboard-focused. Selected tabs and their dropdown content do not acquire dashes.
- Gear's **Expansion** title sits above its button. One selection uses its full name; multiple selections use C / K / V / ?. All and None remain explicit.

## Characters

The right-aligned character/server selector switches the whole application through a controlled save-and-reopen handoff. Current and named ordinary builds autosave; dirty Theorycraft work retains Save / Discard / Cancel. Cancel keeps the current character open. An already-running target is left alone.

The launcher remembers the last successfully opened profile and keeps an additive registry in `%LOCALAPPDATA%\EQLTracker\launcher.json`. Discovery lists configured game folders' log, UI, and inventory filenames; it does not replay their logs or import inventory. New identities are saved in this registry without creating a character database until opened. Same-name characters on different servers remain distinct.

The server identifier comes from local filenames. Verified self `/who` records continue supplying classes and level to their matching character. Other players in `/who` do not become local character profiles. Missing facts remain unknown; discovery does not enable captures or automatically switch characters.

## Validation

- 685 core regressions passed, including the shared width solver and character discovery/persistence tests.
- 99 affected-component checks passed, with 73 actual WPF renders covering 100%, 150%, 200%, and 300%, plus narrow layouts and all 14 tab styles. This includes a focused Group/Raid and Metrics splitter supplement.
- Captured pointer traces cover both header grippers and nested panel cascading. The user also tested the isolated popup and reported that it feels good. All timing and allocation samples, including outliers, remain in the local report.
- 11 isolated offline handoff checks passed: separate server databases, saved appearance, unchanged inventory, unknown new facts, capture opt-ins off, and restart recovery.
- Source-to-published-archive comparison confirms that the six affected original UI files predate security scanning. They were unchanged by public export and release preparation. Their earlier development edit history is not present in the fresh public repository.

Physical-monitor DPI and live-game acceptance remain separate. Validation used isolated synthetic profiles and did not restart WSL or launch Windows Sandbox.
