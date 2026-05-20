# CPU, Memory, And Device Bring-Up

## CPU Work

Start with correctness and observability:

- Decode enough instructions to run simple test programs.
- Implement flags carefully.
- Add tests for edge cases, not just common cases.
- Track unimplemented opcodes with address and bytes.
- Support trace-code ranges.
- Add profiling by guest address.

Do not optimize the CPU before the memory and device model are stable. A fast
wrong interpreter just reaches wrong states faster.

## Floating Point And SIMD

Floating point and SIMD bugs often surface as bad geometry, impossible loops, or
NaN propagation.

Useful practices:

- Add instruction tests for rounding, conversion, comparisons, and flags.
- Log NaN/Infinity when they enter graphics or device code.
- Treat conversion instructions as high priority once 3D software runs.
- Compare matrix/vector helper outputs against small reference cases.

## Memory Model

The memory system should provide:

- Mapped regions with names.
- Unmapped access exceptions with address and width.
- Read/write watchpoints.
- MMIO dispatch.
- Mirroring and aliasing.
- DMA-safe physical views.
- Optional unwatched reads for device internals.

Every memory fault should tell you enough to decide whether the bug is CPU,
loader, address translation, DMA, or guest behavior.

## Device Bring-Up

For each device:

1. Define reset state.
2. Implement register reads/writes with logs.
3. Count accesses.
4. Add side effects one by one.
5. Add interrupt/DMA behavior only when software reaches it.
6. Build tests for stable register behavior.

Do not implement a whole device spec at once. The guest will tell you the next
piece it needs.

## DMA And Command Buffers

DMA paths are common compatibility bottlenecks.

Track:

- Put/get pointers.
- Base and limit.
- Submission count.
- Decoded command count.
- Empty submissions.
- Invalid commands.
- Read failures.
- Retries or recovery paths.
- Last decoded command range.

DMA recovery logic should be visible in logs. If the emulator guesses a command
window, record that guess.

## File And Media IO

File systems and media images need their own observability:

- Open count.
- Directory enumeration count.
- Missing path count.
- Read count.
- Bytes read.
- Write count.
- Last paths.
- Path normalization logs.

Demo discs and menus are especially valuable because they stress directory
layout, launching, and asset reads before demanding perfect rendering.

## Timing And Waits

Guests often spin on:

- VBlank.
- DMA completion.
- Audio buffer status.
- Input transfer completion.
- File IO status.
- Thread scheduling.
- Semaphores or event flags.

Implement enough scheduling to keep progress deterministic. For early bring-up,
bounded wait satisfaction can be acceptable if it is logged and revisited.
