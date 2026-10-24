# Pet Details popup — focused local validation

The old inline expander is replaced by a measured 28-DIP button and an owned nonmodal window. The shared editor retains autosave, Undo and Theorycraft behavior. The current source follows confirmed owned-pet summons, with build-only overrides. Catalog matching precedes rank parsing, including spell-line numerals, aliases and recorded ranks above ten. Missing fields remain nullable evidence.

Focused core cases cover all nine cataloged families, exact names/aliases, Monster II versus upgrade VIII, failed casts, delayed ownership, reviewed-family metadata recovery, follow/manual isolation, inventory/identity preservation, nullable restart state, missing catalogs, Undo, rank/gear mismatches and invalid HP/resist coverage. Existing summon, research and independent-selector regressions also pass.

Synthetic WPF smoke checks cover the toolbar button, single-window reopening, follow/override/return actions, unchanged inventory, rank preservation, observed-versus-gear display, normal/minimum widths, unknown resists, obsolete-result rejection, Escape, tab/workspace changes and independent planned contexts. The earlier stale-result test initially allowed the normal refresh timer to requeue a valid selection; it now clears the model selection before asserting rejection. Older component-review assertions were updated from expander assumptions to popup behavior.

The metadata-only recovery pass operates on a copied profile, publishes after successful completion, and advances the existing evidence version. It does not recount encounters or replace group evidence. Popup statistics use immutable inputs and generation checks; incomplete captures are labeled rather than promoted to current totals. Timing results are retained in the local validation output. Broad theme/DPI reviews remain deferred.

## Character-sheet iteration — October 22

The four-column DataGrid is replaced by themed character-sheet groups. The identity header separates captured actual level from summon rank. Summon controls wrap above it, and the former green level/formula paragraphs move into Evidence. Pet Details restores its flat disclosure-style face as an ordinary popup action, with no dropdown arrow.

Focused results: **24/24 popup/context data tests**, **16/16 existing research regressions**, and **35/35 synthetic WPF checks in both local and public configurations**. The checks cover all tracked fields, all seven resists, current/maximum versus maximum-only resources, true zero values, invalid negative HP and nonfinite values, pair ordering, separate equipment contributions, absent captures, Current/override/planned behavior, recorded ranks above ten, retained results and stale-result rejection. Additional scalar AC/Attack observations and current-only resources remain accessible under additional stats without manufacturing pairs.

Rendered smoke cases use 740×800, 500×580 and 500×420 DIP, plus expanded equipment/skills and the toolbar action. Normal width uses balanced sections; narrow widths stack them. The sheet has no DataGrid or nested scrolling; the fixed Close action stays within the window while content scrolls. Keyboard focus eligibility, Escape and long summon labels are covered. The previous table review verified values but missed that repeated section rows and column headers obscured character-sheet readability.

Two intermediate review failures are retained locally: an overbroad scrollbar assertion counted ComboBox template scroll hosts, and an observed fixture was captured before effective equipment finished initializing. The final review scopes scrolling to the sheet and captures evidence after initialization; matching rules were not relaxed. Final review timing samples and original outliers remain in the local results. Broad theme/DPI matrices and live-game acceptance remain deferred.

The change adds transient internal capture metadata only. Persisted profile/research formats, rank rules, custom weights and combat calculations are unchanged. The public configuration leaves private research disabled. Delivery verifies targeted source/payload/preview privacy checks, backup hashes and published artifact hashes before graceful replacement.

Delivery uses targeted source/payload/synthetic-preview privacy checks, retained Beta artifact hashes, verified app/profile/research backups and graceful local replacement. Personal screenshots, names and observations remain outside public fixtures. No installer, GitHub publication, WSL or Sandbox work is included.
