#!/usr/bin/env bash
# Local scanners only. Does not install tools, scan a distro, or change services.
set -euo pipefail
source_root="$(cd "${1:-.}" && pwd)"
version="$(python3 -c 'import xml.etree.ElementTree as E,sys;p=E.parse(sys.argv[1]).getroot().find("PropertyGroup");print(p.findtext("VersionPrefix")+"-"+p.findtext("VersionSuffix"))' "$source_root/Version.props")"
output_root="${2:?Specify a private scanner output directory outside the staged source tree}"
output_root="$(realpath -m "$output_root")"
case "$output_root/" in "$source_root/"*) printf '%s\n' 'Scanner outputs and caches must stay outside the staged release input.' >&2; exit 2;; esac
payload="$source_root/dist/EQL-Catalyst-$version-win-x64"
test -f "$source_root/Version.props" && test -f "$payload/EQLTracker.deps.json"
mkdir -p "$output_root/cache"
export GOMAXPROCS=2
run_low() { nice -n 15 ionice -c 3 taskset -c "${EQL_SCAN_CPUS:-0,1}" "$@"; }
run_low trivy filesystem --disable-telemetry --parallel 2 --cache-dir "$output_root/cache" --scanners vuln,secret,misconfig,license --skip-dirs '**/bin' --skip-dirs '**/obj' --skip-dirs dist --skip-dirs artifacts --format json --output "$output_root/trivy-source.json" "$source_root"
run_low trivy filesystem --disable-telemetry --parallel 2 --cache-dir "$output_root/cache" --scanners vuln,secret,license --format json --output "$output_root/trivy-payload.json" "$payload"
run_low gitleaks dir "$source_root" --config "$source_root/.gitleaks.toml" --no-banner --redact=100 --report-format json --report-path "$output_root/gitleaks.json"
run_low syft "dir:$payload" -o "cyclonedx-json=$output_root/sbom-raw.json"
printf '%s\n' 'Reports remain local. Review and sanitize before publishing; a zero exit is not vulnerability triage.'
