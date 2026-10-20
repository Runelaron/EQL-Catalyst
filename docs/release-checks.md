# Release verification

The public export contains maintained application source, tests, required data and current artwork. Development history, profiles, inventory captures, private reports and art experiments are excluded. All parser fixtures are synthetic.

Local release checks scan only the exported tree and runtime payload. They do not scan a whole WSL distro, upload source to a scanning service, restart WSL/services or launch Windows Sandbox. Trivy checks supported dependencies, secrets and applicable configuration; Gitleaks supplements secret detection. Syft supplies an SBOM and bundled .NET/native runtime versions are checked independently where scanners lack coverage.

Privacy checks inspect text, binary strings, image metadata and archives. Compiler path mapping removes local build-root paths. PDBs and local profile/cache files are not distributed. Scan output is sanitized before inclusion in a public report.

The earlier development installer passed 44 lifecycle checks in a disposable offline Windows guest. That result belongs to its original hash, not automatically to rebuilt release files. Public CI performs source builds, core regressions, package checks and installer lifecycle smoke checks against the rebuilt release. Physical-monitor DPI, live-game input/capture acceptance and Windows builds outside tested environments remain pending.

Public security results and checksums identify exact release artifacts. Findings are reviewed individually; confirmed leaks/secrets and unresolved applicable High/Critical vulnerabilities block release. No scanner is a guarantee that every vulnerability has been found.

## Mandatory before every publication

Repeat privacy/path and image-metadata inspection of the final source, runtime, archives and extracted installer. Repeat Gitleaks and Trivy against a clean staged export using local tools; regenerate the SBOM and reconcile bundled runtime/native components. Record scanner/database versions and each reviewed finding. A previous release's reports do not satisfy a new release's gate.

Tie the approved input inventory and package SHA-256 values to the exact commit and upload set. Any subsequent source or package change invalidates the corresponding gate and requires checking the changed final inputs again. Verify the intended GitHub account immediately before pushes, releases, uploads or Pages configuration. Do not publish blocking findings, raw scanner paths, local reports or private data.
