# Previous Emulator Learnings Digest

The prior emulator threads agree on one main thing: the emulator is also the lab. The fastest path is a tight loop of legal inputs, deterministic runs, structured evidence, and small hardware-shaped fixes.

## Project Shape

- Keep the core deterministic and headless.
- Keep CLI, frontend, batch runners, and artifact tools outside the core.
- Make CPU, bus, devices, scheduler, media, and input explicit state.
- Add abstractions only after repeated behavior proves the shape.
- Track whether a behavior is low-level emulation, HLE, synthetic fallback, or diagnostic shortcut.

## First Milestone

The first milestone should be:

1. Parse a small executable.
2. Map memory deterministically.
3. Execute a bounded number of instructions.
4. Stop at a known point or unsupported behavior.
5. Emit CPU state, memory/device accesses, counters, and a compact summary.
6. Run the same input twice and get the same stop point.

For Dreamcast, that means a KOS-built SH ELF beats any retail image as the first serious target.

## Diagnostics To Build Early

- CLI run limits by instruction count, frame count, and wall-clock time.
- Trace tail, PC range filters, address watchpoints, and device-specific traces.
- Monotonic counters that survive guest resets.
- Structured JSON/text summaries for CPU, memory, interrupts, DMA, PVR, AICA, Maple, and media.
- Frame dumps and hashes once video exists.
- Input scripts once Maple input exists.
- Save states early enough to shorten reproduction loops.

## Compatibility Process

Treat every hang, black frame, crash, and weird counter as evidence. Classify failures by subsystem instead of by vibes:

- CPU/exception issue
- memory map/open bus/mirroring issue
- timing or interrupt ordering issue
- DMA/device protocol issue
- graphics command/raster issue
- audio issue
- input/save/media issue
- bad fixture/archive/user input

Every compatibility fix should leave either a narrow regression or a documented artifact signature.

## Performance Rules

Start with a clear interpreter and measurable counters. Optimize only after behavior is visible. Cached interpreters and fast paths are fine when they preserve trace/debug mode and have correctness comparisons. Never let full traces, runaway allocation, or guest loops endanger the host.

## Legal And Research Rules

Use manuals, public docs, homebrew source, real-hardware observations, and reference emulators as evidence. Do not copy incompatible source code. Keep commercial media, firmware, proprietary SDKs, and generated captures out of git.
