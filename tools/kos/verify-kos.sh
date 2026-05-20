#!/usr/bin/env bash
set -eo pipefail

KOS_BASE="${KOS_BASE:-$HOME/kos}"

if [[ ! -f "$KOS_BASE/environ.sh" ]]; then
  echo "KOS environ.sh not found at $KOS_BASE/environ.sh" >&2
  echo "Expected the WSL setup layout: ~/kos, ~/kos-ports, ~/sh-elf" >&2
  exit 1
fi

source "$KOS_BASE/environ.sh" >/dev/null

echo "KOS_BASE=$KOS_BASE"
echo "KOS_PORTS=$KOS_PORTS"
sh-elf-gcc --version | head -n 1
kos-cc --version | head -n 1
