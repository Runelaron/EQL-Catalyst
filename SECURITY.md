# Security

Do not post secrets, character databases, unredacted logs or full inventory captures in public issues. Use GitHub private vulnerability reporting if available; otherwise open a minimal issue requesting a private contact without including exploit details or private data.

Release preparation checks the allowlisted source and self-contained payload locally with Trivy, Gitleaks and a privacy/path audit. Scans cannot prove the absence of every defect. Reports identify the tool/database versions, coverage limitations and disposition of findings. Confirmed secrets/private-data leaks and unresolved applicable High/Critical findings block publication.

The unsigned installer may be blocked by Windows policy. Verify the release checksums and source before running it; do not disable protection. Build attestations establish provenance, not Windows publisher signing.
