# DPS title selector and detailed Group icon validation

The title's actor icon is now a direct four-choice dropdown: Pet / Self / Combined / Group. It reuses existing actor selection, persisted overlay settings, menu density and dismissal behavior. The operational control remains present in Minimal style and collapsed/minimum layouts. Header dragging ignores its Button ancestor; the selector does not start a move. Accessible naming follows the selected actor; keyboard Down/F4 and accessible Invoke are covered.

The missing painted Group resource previously fell back to the flat three-person Clean glyph. A generated 256px transparent fantasy party emblem now fills that role for Vibrant throughout the shared renderer. No other icon, theme or calculation mapping changed. The new asset is listed with its hash and provenance; source help records the prompt and built-in generation mode.

## Results

- All 44 focused WPF assertions passed against the packaged runtime: direct choices, exclusive selected state after switching/reopening, immediate updates, existing persistence serialization, independent Combat Lab selection, unchanged scope/sort/filters, Down/F4, Escape, accessible Invoke, minimum/collapsed use and Minimal/Frost/Luminous visibility.
- 36 fresh synthetic images cover all three themes at 100%, 150%, 200% and 300%: title panel, minimum panel and choices with Clean/Vibrant Group samples. Production alpha and 256px resource dimensions are checked. No real profile or desktop capture is used.
- Capture timings are retained, including outliers: median 16.072 ms, max 58.195 ms. These are component capture costs, not interactive frame timings.
- Application/core hashes tie the report to the local payload. The core assembly is byte-identical to the previous verified local build (1,515 core regressions); no core test rerun is claimed for this UI-only change.
- The older comprehensive menu/display fixture aborts at its keyboard-style repeated-click assertion on both the preceding verified payload and this payload. It is not counted as passing. The focused title selector checks use complete popup-close dispatch between gestures; the unchanged shared menu controller remains in use. Baseline/new failure records are retained locally.
- Tests use synthetic WPF events and accessible Invoke. Hardware mouse/game and physical monitor acceptance remain separate.

## Delivery

Local self-contained unsigned build; version remains 1.4.0-beta.1. Privacy checks cover maintained source, packaged runtime and generated component images. App/profile backups are verified before graceful local replacement and restart. All nine Beta 1.4 assets and seven older retained Beta 1.1 assets are hash-checked and unchanged. No GitHub publication, installer build, WSL change or Sandbox launch.
