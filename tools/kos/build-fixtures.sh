#!/usr/bin/env bash
set -eo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
manifest="${1:-$repo_root/fixtures/kos.json}"

case "$manifest" in
  /*) ;;
  *) manifest="$repo_root/$manifest" ;;
esac

if [[ ! -f "$manifest" ]]; then
  echo "Fixture manifest not found: $manifest" >&2
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "python3 is required to read fixture sample paths from $manifest" >&2
  exit 1
fi

mapfile -t samples < <(python3 - "$manifest" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as stream:
    manifest = json.load(stream)

seen = set()
for fixture in manifest.get("fixtures", []):
    sample = fixture.get("sample")
    if sample and sample not in seen:
        seen.add(sample)
        print(sample)
PY
)

if [[ "${#samples[@]}" -eq 0 ]]; then
  echo "No fixture samples found in $manifest" >&2
  exit 1
fi

for sample in "${samples[@]}"; do
  echo "==> Building $sample"
  "$script_dir/build-sample.sh" "$sample"
done
