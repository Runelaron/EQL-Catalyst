# Application updates

Open **Settings → Sync & files → Application updates**. Automatic updates start **off**, for both new and existing users. Enabling them checks immediately and then once every seven days while EQL Catalyst is running. The preference applies to every character. There is no Windows service or scheduled task.

Updates come from the public **Runelaron/EQL-Catalyst** GitHub Releases page, including published Beta releases. No GitHub account is needed. Requests send ordinary network information and an application-version User-Agent; profiles, game paths and logs are not uploaded. Offline and diagnostic modes disable update networking.

Use **Check for updates** at any time. Manual checks offer a download; automatic checks download it when available. A verified download shows an update icon **immediately to the right of Settings**. Open it, review the version/release notes, then choose **Update and restart** when ready. Automatic updates never install or restart Windows on their own. Disabling them stops automatic work; a prepared download can still be installed manually.

The updater checks a separately signed manifest and SHA-256 package hashes. The Windows app and installer remain **unsigned**: these checks do not replace Authenticode signing or guarantee that Windows policy permits execution. Never bypass organizational security policy.

Installed copies retain their directory and per-user/all-users scope; all-users installation requests elevation. Portable copies update in their existing folder. Unsaved builds retain Save/Discard/Cancel protection. An older or inaccessible running instance must be closed manually. No instance is forcibly terminated.

Application files and closed profile data are backed up before replacement. Recovery files stay local under the update cache; portable application backups sit beside the portable application folder. Profiles, game files and capture opt-ins are not reset. If installation fails, the helper explains how to reopen the current app or rerun the verified installer. A lost helper can be reopened from its prepared update folder to recover an interrupted portable rename.

Beta 1.1 has no updater. Install Beta 1.4 manually once to gain this feature. Future updates can then use the in-app controls.

## Release maintenance and trust

`scripts/update-manifest.py` creates schema-1 metadata from final validated installer/portable assets and their exact source commit. `scripts/sign-update.ps1` signs its exact bytes using ECDSA P-256/SHA-256 (IEEE P1363 signature encoding). The app embeds only the public verification key. Generate and keep the private key outside the repository, protected with Windows DPAPI for the releasing user; never upload it or put it in CI logs. Signing does not require a paid service.

The private key must remain available for future releases. If it is lost or compromised, disable automatic release publication and distribute a manually verified replacement build with a new trust key. Do not silently accept a key supplied by unsigned update metadata. Build provenance attestations supplement these checks and do not sign the Windows executable.

Stage and scan source and packages before any public push. After Windows CI, scan the exact final artifacts again, generate the SBOM, sign update metadata, regenerate checksums, and upload the complete draft release before publishing it. Never rebuild assets after signing without repeating validation and signing.
