# Beta 1.4 validation

Local verification passed 1,485 core regressions, including update version ordering, signed metadata, package integrity, consent/scheduling, concurrent opt-out preservation, unsafe archive paths, replacement preservation and interrupted portable recovery.

The affected WPF and download review passed 66 assertions; 52 interface assertions also run on hosted Windows. The visual pass produced 84 synthetic images across Coldain, Sunstrider and Shadowman at 100%, 150%, 200% and 300%. It covers the Progress title-row checks, expansion toggle, update settings/icon, compact acquisition sources and managed outer-window resizing. An in-process HTTP transport exercised actual signed installer/portable streams, consent, automatic preparation, shared-download reuse, live opt-out and rejection of modified cached packages without external requests or bypassing production signature checks. Timing and allocation samples, including outliers, are retained in local validation evidence.

A disposable portable replacement used genuine app/helper named-pipe coordination with two character instances. Both original instances exited gracefully, a profile backup was created, a sentinel remained unchanged, installed payload files matched the release manifest, and both characters reported the new version after restarting. No installed-user data or live game was used.

Release gates include staged-source/payload privacy checks, Trivy dependency/secret/configuration checks, Gitleaks, a CycloneDX SBOM enriched with bundled .NET/native SQLite dependencies, and Grype. See the release's **SECURITY-SUMMARY.md**, **sbom.cdx.json**, checksums and **windows-validation.json** for the exact final artifact results and coverage limitations.

GitHub-hosted Windows checks rebuild the source, test both installation scopes and custom paths, inspect extracted installed files, exercise offline startup/first-run cancellation and preserved-data reinstall/uninstall, and run the affected WPF checks. These runners are not fresh Windows systems without .NET. Previous Sandbox results concern earlier hashes and are not reused as acceptance for this release.

Physical-monitor DPI transitions, Windows-policy/SmartScreen behavior and live-game acceptance remain separate. Windows packages are unsigned. Update-metadata signatures and build attestations do not replace Authenticode signing.
