# Beta 1.7 validation

Release target: **1.7.0-beta.1**, **Beta 1.7**. The final release attachments identify the frozen commit, exact hosted artifact hashes, scanner/database versions and signed update evidence. This source document describes the scope; it does not substitute local builds for final CI packages.

## Content and retained state

- Fresh wiki revision checks: 19,056 item titles, 67 new titles, 23 changed definitions and 170 linked zone pages. Offline item definitions: 6,906 → 6,958; documented zone-qualified named NPCs: 926.
- All 134 existing quest identities and 746 checklist IDs retained. One new quest; 55 reviewed route actions across eight quests. Wording changes do not reset progress. Older incomplete markers stay optional and cannot satisfy new requirements.
- Acquisition gaps: 2,556 → 2,565. New catalog entries can lack routes. Missing numeric metadata, template examples and reward/drop disagreements remain explicit. Newer omissions cannot resurrect older cached stat values.
- Existing combat totals, encounter boundaries, independent ingestion checkpoints, inventories, weights and rank models are unchanged by content reprojection. Private evidence stays outside published inputs.

## Local checks before source freeze

The complete core suite passes **1,716 tests**, including real companion TLS/authentication, origin, cookie, permission, revocation, cross-character and failure cases. Package policy and exact-hash privacy-exception behavior pass ten Python regressions. Android protocol unit tests and 36 offline browser component checks cover the retained protocol without an APK release or certificate bypass.

Native studies use actual WPF components, synthetic profiles, all three themes and 100/150/200/300% rendering where specified. They cover toolbar attachment and independent dimensions, first-open persistence, release/cancellation ordering, motion reversals, transparent states, keyboard focus, Overview, compact Bard/compatibility forms, independent selectors, draft Save/Undo, summon context, standard buffs and artwork. Initial timing outliers and failed probes are retained privately. A content-tooltip regression exposed the missing access-NPC name after supplemental wiki notes replaced older prose; the revised note retains Retainer Ozval alongside the new route evidence.

A fresh public profile reports private research off, sharing disabled and unconfigured, no listener, unknown character facts and native capture off. Paintings are used at every Vibrant size; Clean remains independent. Reviewed artwork hashes and all retained checklist identities are checked directly.

## Final artifact and security gates

Before any public push, scan exact publication inputs and new Git history. Reject local guides, agent instructions, credentials, profiles, logs, research, private keys and development output by package-content assertions, not ignore rules alone. Hash-reviewed test-network constants and third-party library constants retain their raw findings and reasons; changed bytes invalidate approval.

Hosted Windows builds run the complete core suite, native studies, browser components, Android protocol tests, privacy gates and disposable user/all-user installer/reinstall/uninstall checks. Public release packages explicitly compile without private research. Check exact CI source correspondence and every payload hash, then repeat Trivy/Gitleaks scans on source, archives, installer and extracted payload. Generate an exact-payload SBOM and scan with Grype using recorded database metadata.

The separately dispatched signed-update workflow tests the exact candidate packages from Beta 1.4 and Beta 1.6, portable and installed scopes, two profile restarts, invalid-signature rejection, recovery backups and retained data. Workspace cancellation and transactional recovery are also covered by native/core tests. The existing protected trust key signs public manifest bytes locally; no private signing material enters CI. Verify all draft remote digests before publishing, then verify update discovery and the download site.

## Acceptance limits

Synthetic rendering and native geometry do not establish physical multi-monitor DPI, live-game behavior, Windows policy or SmartScreen acceptance. Hosted Windows includes .NET and is not a fresh-OS image. Windows binaries remain unsigned. No Sandbox, WSL configuration changes, Store submission, Android APK release, signing-key replacement or destructive migration is part of this release.

Local research-enabled delivery uses the same frozen source commit with distinct build hashes, verified source/application/profile/research/companion backups and normal guarded shutdown. Retained layouts and evidence are checked after restart.
