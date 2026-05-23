#!/usr/bin/env bash
set -eo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
media_dir="$repo_root/artifacts/media"

mkdir -p "$media_dir"
python3 - "$media_dir" <<'PY'
from pathlib import Path
import sys

media_dir = Path(sys.argv[1])
track = bytearray(2048)
label = b"DCSHARP_GDROM_FIXTURE"
track[0:len(label)] = label
track[32:36] = bytes([0x44, 0x43, 0x53, 0x48])

track_path = media_dir / "dcsharp_gdrom_track03.bin"
gdi_path = media_dir / "dcsharp_gdrom.gdi"
track_path.write_bytes(track)
gdi_path.write_text(
    '1\n'
    '3 45000 4 2048 dcsharp_gdrom_track03.bin 0\n',
    encoding='ascii')
PY

echo "Built fixture media:"
printf '%s\n' "$media_dir/dcsharp_gdrom.gdi" "$media_dir/dcsharp_gdrom_track03.bin"
