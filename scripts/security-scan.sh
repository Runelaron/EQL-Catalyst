#!/usr/bin/env bash
# Scan release staging only. Does not log in, restart services, or scan the host.
set -euo pipefail
source_tree=$(realpath "$1")
payload=$(realpath "$2")
output=$(realpath -m "$3")
mkdir -p "$output/cache"
export GOMAXPROCS=2 TRIVY_DISABLE_TELEMETRY=true GRYPE_CHECK_FOR_APP_UPDATE=false
export PATH="$HOME/.local/bin:$PATH"
low(){ nice -n 15 ionice -c 3 taskset -c 0,1 "$@"; }
low trivy --version > "$output/trivy-version.txt"
low gitleaks version > "$output/gitleaks-version.txt"
low syft version > "$output/syft-version.txt"
for scope in source payload; do
  target="$source_tree"; [ "$scope" != payload ] || target="$payload"
  low trivy filesystem --disable-telemetry --parallel 2 --cache-dir "$output/cache" --scanners vuln,secret,misconfig --format json --output "$output/trivy-$scope.json" "$target"
  low gitleaks dir "$target" --config "$source_tree/.gitleaks.toml" --no-banner --redact=100 --report-format json --report-path "$output/gitleaks-$scope.json"
done
low syft "dir:$payload" -o "cyclonedx-json=$output/sbom-raw.json"
cp "$output/cache/db/metadata.json" "$output/advisory-db.json"
