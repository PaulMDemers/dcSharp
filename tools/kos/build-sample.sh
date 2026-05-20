#!/usr/bin/env bash
set -eo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
sample="${1:-samples/kos/hello}"
sample_dir="$repo_root/$sample"

if [[ ! -d "$sample_dir" ]]; then
  echo "Sample directory not found: $sample_dir" >&2
  exit 1
fi

KOS_BASE="${KOS_BASE:-$HOME/kos}"
source "$KOS_BASE/environ.sh" >/dev/null

make -C "$sample_dir" clean all

artifact_dir="$repo_root/artifacts/kos"
mkdir -p "$artifact_dir"
artifacts=()
while IFS= read -r -d '' elf; do
  artifact="$artifact_dir/$(basename "$elf")"
  cp "$elf" "$artifact"
  artifacts+=("$artifact")
done < <(find "$sample_dir" -maxdepth 1 -name '*.elf' -print0)
if [[ "${#artifacts[@]}" -eq 0 ]]; then
  echo "No ELF artifacts produced by $sample_dir" >&2
  exit 1
fi
make -C "$sample_dir" clean >/dev/null

echo "Built KOS sample artifacts:"
printf '%s\n' "${artifacts[@]}"
