# Translucent workspace and round-two artwork validation

Focused local review, October 23, 2026.

## Verified implementation

- Private local publish and public configuration build complete with no warnings or errors. The packaged local assemblies are the assemblies exercised by the final native reviews.
- 110 native WPF toolbar checks pass. Coverage includes translucent shared surfaces with opaque content, fixed 28-DIP height, flat controls, 6-DIP heading inset, hidden attached drag grip, exposed edge joins with either width larger, below/above placement, sequential switching, reversals, latest intent, stale completions, dirty-work cancellation, reduced motion, minimize/restore and temporary hiding.
- Real Thumb mouse-down/up handlers retain the release-before-completion order. Native, rendered and saved dimensions reconcile after resizing, reversal, Escape and capture loss. A resize begun during opening snaps the slide without losing capture or rolling back its committed dimensions. Toolbar width stays separate from the two expanded workspace sizes. Locking, reordering, keyboard commands, narrow bounds and separate-process restart remain covered.
- The first explicit open before deferred WPF Loaded still persists resolved placement without requiring a second interaction. Explicit open callers can populate their persistent target before an animated switch completes; focus requests wait for the shown target.
- 51 Pet workspace checks pass with private research enabled, including draft Save/Undo, cancellation, editor and drop behavior. Eight toolbar core tests and 235 pet core tests pass. The public configuration's 11 standard-buff checks confirm private research remains disabled.
- Approved Monster A's original PNG hash is unchanged. Its shared rendering path replaces the former composite; Clean and all sizes through 24 DIP use the approved simplified silhouette. Larger Vibrant icons use the approved painting with DPI-sized decoding. Generic fallback, detected identities and independent selectors are preserved. Other production artwork is hash-checked against the pre-change source backup.
- The gallery contains 18 new review concepts and 11 archived earlier concepts, with Monster A marked In use. Family/variant/status metadata drives the boards. Painted and simplified treatments are available at 13/14/16/20/24/32/64 DIP; actual DPS control boards cover all three themes and both styles, plus grayscale and silhouettes. PNG alpha, prompt hashes and provenance hashes are checked.

## Findings and limits

The motion review exposed Windows' automatic restoration of previously visible owned windows: an inactive panel could reappear after its toolbar restored, despite a completed closing animation. The visibility guard now hides unrequested owned windows after restoration. This is distinct from a workspace open request and does not change saved selection.

A target must exist before explicit open callers populate it, but WPF requires its owner's native handle first. The initial-open regression covers that lifecycle. Motion uses a revisioned visual progress value; no native size, saved placement, pane layout, encounter boundary or rate denominator is animated. Repeated equivalent transition requests coalesce.

Native captures combine the actual WPF content roots at their native dimensions over a synthetic striped backing to make translucency and frame joins visible. They are component evidence, not screenshots of a live game. Timing samples, including slow resize/render outliers, remain unfiltered in the machine-readable reports. The gallery and local references receive static HTML/JavaScript checks; automated local-file browser access remains blocked by browser policy and is not bypassed.

Physical monitor/DPI transitions, broad window theme/DPI matrices and live game pointer acceptance remain deferred. Icon detail/decode policy is checked at 100/150/200/300%; this does not claim a full window DPI acceptance matrix. Existing unrelated older review entry points are not counted as passing this iteration.

## Local delivery

Targeted privacy checks cover changed source, packaged files and review assets. Personal evidence is excluded. Backups of the application, profiles and private research are hash-verified before graceful local replacement. The deployment and startup receipts record actual completion, assembly hashes and data comparisons. Combat records, inventory evidence, research, custom weights/loadouts, saved expanded dimensions and independent log checkpoints must match the stopped backup.

This update introduces internal presentation state only. No public API, profile migration, combat calculation, public release artifact, installer, application/tray icon, Store submission, GitHub publication, WSL or Sandbox change is included.

Local delivery completed successfully. The backup verified 2,441 application files, 114 profile files and 42 private-evidence files; all 777 delivered runtime files were verified. Startup checks confirm the native 28-DIP toolbar, attached panel, separately saved toolbar width and unchanged expanded layouts. All 688 encounters, 183,330 combat events, 2,156 observations, 42,665 healing records and 4,676 ability outcomes match the stopped backup. Inventory, private research, custom weights/loadouts, independent DPS selectors, log checkpoints and error logs also match. The initial inspection preceded deferred UI startup; the completed startup inspection passes. Published release hashes remain unchanged.
