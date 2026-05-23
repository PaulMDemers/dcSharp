#!/usr/bin/env bash
set -eo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
media_dir="$repo_root/artifacts/media"

mkdir -p "$media_dir"
python3 - "$media_dir" <<'PY'
from pathlib import Path
import sys

SECTOR_SIZE = 2048
TRACK_START_FAD = 45000
TRACK_SECTORS = 20
BIG_FILE_SECTOR = 10
BIG_FILE_SIZE = 5000
SUBDIR_SECTOR = 14
NESTED_FILE_SECTOR = 15
ROOT_SECTOR = 18
FILE_SECTOR = 19
FILE_NAME = b"README.TXT;1"
FILE_CONTENT = b"dcSharp ISO9660 fixture says hello from /cd.\n"
SUBDIR_NAME = b"DATA"
NESTED_FILE_NAME = b"SECOND.TXT;1"
NESTED_FILE_CONTENT = b"dcSharp ISO9660 nested fixture from /cd/DATA/SECOND.TXT.\n"
BIG_FILE_NAME = b"BIG.BIN;1"
BIG_FILE_CONTENT = bytes(((index * 31 + 7) & 0xFF) for index in range(BIG_FILE_SIZE))


def write_733(buffer, offset, value):
    buffer[offset:offset + 4] = value.to_bytes(4, "little")
    buffer[offset + 4:offset + 8] = value.to_bytes(4, "big")


def write_723(buffer, offset, value):
    buffer[offset:offset + 2] = value.to_bytes(2, "little")
    buffer[offset + 2:offset + 4] = value.to_bytes(2, "big")


def dir_record(extent, size, flags, name):
    record_length = 33 + len(name)
    if len(name) % 2 == 0:
        record_length += 1

    record = bytearray(record_length)
    record[0] = record_length
    record[1] = 0
    write_733(record, 2, extent)
    write_733(record, 10, size)
    record[18:25] = bytes([26, 5, 23, 12, 0, 0, 0])
    record[25] = flags
    record[26] = 0
    record[27] = 0
    write_723(record, 28, 1)
    record[32] = len(name)
    record[33:33 + len(name)] = name
    return record


def kos_extent(track_sector):
    return TRACK_START_FAD + track_sector - 150


def cd_sector(payload):
    sector = bytearray(2352)
    sector[16:16 + len(payload)] = payload
    return sector


media_dir = Path(sys.argv[1])
track = bytearray(TRACK_SECTORS * SECTOR_SIZE)

label = b"DCSHARP_GDROM_FIXTURE"
track[0:len(label)] = label
track[32:36] = bytes([0x44, 0x43, 0x53, 0x48])

track[FILE_SECTOR * SECTOR_SIZE:FILE_SECTOR * SECTOR_SIZE + len(FILE_CONTENT)] = FILE_CONTENT
track[NESTED_FILE_SECTOR * SECTOR_SIZE:NESTED_FILE_SECTOR * SECTOR_SIZE + len(NESTED_FILE_CONTENT)] = NESTED_FILE_CONTENT
track[BIG_FILE_SECTOR * SECTOR_SIZE:BIG_FILE_SECTOR * SECTOR_SIZE + len(BIG_FILE_CONTENT)] = BIG_FILE_CONTENT

root_record = dir_record(kos_extent(ROOT_SECTOR), SECTOR_SIZE, 2, b"\x00")
parent_record = dir_record(kos_extent(ROOT_SECTOR), SECTOR_SIZE, 2, b"\x01")
file_record = dir_record(kos_extent(FILE_SECTOR), len(FILE_CONTENT), 0, FILE_NAME)
subdir_record = dir_record(kos_extent(SUBDIR_SECTOR), SECTOR_SIZE, 2, SUBDIR_NAME)

root_offset = ROOT_SECTOR * SECTOR_SIZE
track[root_offset:root_offset + len(root_record)] = root_record
track[root_offset + len(root_record):root_offset + len(root_record) + len(parent_record)] = parent_record
file_offset = root_offset + len(root_record) + len(parent_record)
track[file_offset:file_offset + len(file_record)] = file_record
subdir_offset = file_offset + len(file_record)
track[subdir_offset:subdir_offset + len(subdir_record)] = subdir_record

subdir_self_record = dir_record(kos_extent(SUBDIR_SECTOR), SECTOR_SIZE, 2, b"\x00")
subdir_parent_record = dir_record(kos_extent(ROOT_SECTOR), SECTOR_SIZE, 2, b"\x01")
nested_file_record = dir_record(kos_extent(NESTED_FILE_SECTOR), len(NESTED_FILE_CONTENT), 0, NESTED_FILE_NAME)
big_file_record = dir_record(kos_extent(BIG_FILE_SECTOR), len(BIG_FILE_CONTENT), 0, BIG_FILE_NAME)

subdir_data_offset = SUBDIR_SECTOR * SECTOR_SIZE
track[subdir_data_offset:subdir_data_offset + len(subdir_self_record)] = subdir_self_record
track[subdir_data_offset + len(subdir_self_record):subdir_data_offset + len(subdir_self_record) + len(subdir_parent_record)] = subdir_parent_record
nested_file_offset = subdir_data_offset + len(subdir_self_record) + len(subdir_parent_record)
track[nested_file_offset:nested_file_offset + len(nested_file_record)] = nested_file_record
big_file_offset = nested_file_offset + len(nested_file_record)
track[big_file_offset:big_file_offset + len(big_file_record)] = big_file_record

pvd_offset = 16 * SECTOR_SIZE
track[pvd_offset] = 1
track[pvd_offset + 1:pvd_offset + 6] = b"CD001"
track[pvd_offset + 6] = 1
track[pvd_offset + 8:pvd_offset + 40] = b"DCSHARP".ljust(32, b" ")
track[pvd_offset + 40:pvd_offset + 72] = b"DCSHARP_FIXTURE".ljust(32, b" ")
write_733(track, pvd_offset + 80, TRACK_SECTORS)
write_723(track, pvd_offset + 120, 1)
write_723(track, pvd_offset + 124, 1)
write_723(track, pvd_offset + 128, SECTOR_SIZE)
track[pvd_offset + 156:pvd_offset + 156 + len(root_record)] = root_record

terminator_offset = 17 * SECTOR_SIZE
track[terminator_offset] = 255
track[terminator_offset + 1:terminator_offset + 6] = b"CD001"
track[terminator_offset + 6] = 1

track_path = media_dir / "dcsharp_gdrom_track03.bin"
gdi_path = media_dir / "dcsharp_gdrom.gdi"
track_path.write_bytes(track)
gdi_path.write_text(
    '1\n'
    '3 45000 4 2048 dcsharp_gdrom_track03.bin 0\n',
    encoding='ascii')

multi_track03 = bytearray(2 * SECTOR_SIZE)
multi_track03[0:4] = b"MTK3"
multi_track04 = bytearray(track)
multi_track04[0:4] = b"MTK4"
multi_track04[32:36] = b"DCSH"

multi_track03_path = media_dir / "dcsharp_gdrom_multitrack_track03.bin"
multi_track04_path = media_dir / "dcsharp_gdrom_multitrack_track04.bin"
multi_gdi_path = media_dir / "dcsharp_gdrom_multitrack.gdi"
multi_track03_path.write_bytes(multi_track03)
multi_track04_path.write_bytes(multi_track04)
multi_gdi_path.write_text(
    '2\n'
    '3 45000 4 2048 dcsharp_gdrom_multitrack_track03.bin 0\n'
    '4 45150 4 2048 dcsharp_gdrom_multitrack_track04.bin 0\n',
    encoding='ascii')

edge_track = bytearray(b"OFFSET-PREFIX-FOR-2352-GDI....!!")
edge_track.extend(cd_sector(b"M235"))
for index in range(1, TRACK_SECTORS):
    edge_track.extend(cd_sector(bytes([(index * 17) & 0xff])))

edge_track_path = media_dir / "dcsharp_gdrom_2352_track05.bin"
edge_gdi_path = media_dir / "dcsharp_gdrom_2352_track05.gdi"
edge_track_path.write_bytes(edge_track)
edge_gdi_path.write_text(
    '1\n'
    '5 45200 4 2352 dcsharp_gdrom_2352_track05.bin 32\n',
    encoding='ascii')
PY

echo "Built fixture media:"
printf '%s\n' \
  "$media_dir/dcsharp_gdrom.gdi" \
  "$media_dir/dcsharp_gdrom_track03.bin" \
  "$media_dir/dcsharp_gdrom_multitrack.gdi" \
  "$media_dir/dcsharp_gdrom_multitrack_track03.bin" \
  "$media_dir/dcsharp_gdrom_multitrack_track04.bin" \
  "$media_dir/dcsharp_gdrom_2352_track05.gdi" \
  "$media_dir/dcsharp_gdrom_2352_track05.bin"
