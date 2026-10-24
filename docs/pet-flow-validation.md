# Pet flow validation

Local review only; public Beta 1.4 artifacts are unchanged.

- 1498 core regression checks passed; 202 UI assertions passed.
- 144 synthetic WPF renders cover Coldain, Sunstrider and Shadowman at 100%, 150%, 200% and 300%, including narrow/short panes, expanded controls and rank previews.
- 9 routed header/splitter drag observations cover alternating drags, reversal, reordering, Escape and capture loss. Tests use controlled coordinates and real WPF handlers/capture. Physical-device DPI acceptance remains separate.
- Both planning scrollbar orientations measure 5 DIP. Menu and overlay density remain separately scoped.
- Reset tests cover catalog +0, recorded +5, explicit Set Rank and temporary previews. Existing tests preserve records above +10, slot effects, dual wield and unchanged ownership.
- Median rendered-frame capture: 149.285 ms; maximum: 418.264 ms. All 144 timing/allocation records and all drag observations are retained locally; no outlier removal.

## Catalog audit

Reviewed 6808 unique bundled definitions and accounted for 18,872 indexed titles. The resumable cache contains 7,363 public item/quest pages. Added 2365 acquisition records for 1390 items; items lacking a documented acquisition path fell from 2631 to 1758.

Remaining dispositions: {"No acquisition documented on reviewed item page": 1579, "Quest connection documented; output/acquisition not established": 178, "Public item page missing or identity unresolved": 1}. Indexed-only names remain classified as on-demand metadata, not silently declared wearable. Full item/title accounting and provenance remain in the local audit outputs. These evidence gaps are explicitly retained, not marked verified.

## Missed cases repaired

The old inventory formatter selected Proc effects only; the shared projection now includes all populated kinds. Reset had no baseline-aware enabled state. Source parsing omitted purchases and many quest outputs. Related quest redirects, Sky reward cells and Gear Set reward declarations are now supported without treating ingredients as rewards. Scheduled refresh no longer skips independently changed quest evidence because the item revision is unchanged. Duplicate AC grouping and empty footer rows are removed.

Bloodmoon +4's 0.75 is traceable to the retained 5% estimate, not an observation. Source disagreements remain Conditional. Catalog +0, ranked haste and upgraded weight have distinct explanations. Exact Whip/Evensong observations, custom weights and damage predictions remain separate.

## Delivery gates

The local payload is self-contained and unsigned. Privacy scans cover maintained source, compiled payload and preview images. App/profile backup and graceful restart follow verification. No installer build, GitHub write, WSL operation or Sandbox launch is part of this change. No profile migration or combat-engine change is included.

Privacy gates passed for 730 maintained files, 516 runtime files and 144 preview images. Initial dotted-version findings were retained and reviewed against the existing exact .NET assembly-version dispositions; no broad scanner exclusion was added.

The local update verified 2,328 app-backup files and 71 profile-backup files, then verified all 516 runtime files. Profiles were unchanged during replacement. The application restarted normally; existing error logs remained unchanged. All nine Beta 1.4 release assets and the seven older Beta 1.1 assets retain their hashes.
