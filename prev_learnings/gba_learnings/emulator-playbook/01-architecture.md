# Emulator Architecture

The best architecture for an emulator is boring in the right places. Put the hardware model in a deterministic core, keep user interface and batch tooling outside it, and make every subsystem inspectable.

## Core Boundaries

Build a core library that has no dependency on desktop UI, test harnesses, or shell scripts. The core should expose:

- a system object that owns CPU, bus, scheduler, video, audio, timers, DMA, input, cartridge/media, and save devices,
- a step/run API that advances deterministic time,
- explicit load/reset/save-data methods,
- debug hooks for instruction trace, memory reads/writes, interrupts, DMA, and frame completion,
- stable snapshots of visible state for tests and tools.

The desktop frontend should be a thin host around the core. The CLI should also be a thin host. If either grows emulator logic, later debugging becomes painful because headless and interactive runs diverge.

## Subsystem Shape

Prefer these ownership lines:

- CPU owns registers, instruction decoding, exceptions, mode/state changes, and cycle results.
- Memory bus owns address mapping, open bus behavior, wait states, MMIO dispatch, cartridge mapping, and peripheral routing.
- Scheduler owns time and event ordering.
- DMA/timers/video/audio schedule and consume bus cycles through explicit APIs.
- Renderer owns framebuffer construction and debug layer extraction.
- Save/peripheral devices own their protocols and backing storage.

The CPU should not know that a given address is "VRAM tile data" or "save memory protocol" beyond bus timing. The renderer should not special-case CPU state. Keep hardware contracts local.

## Determinism

Determinism is a feature. Build so that a ROM, save file, input script, and configuration produce the same frame hashes and crash point on every run.

Useful deterministic inputs:

- frame-indexed key events,
- fixed save fixtures,
- fixed RTC/peripheral defaults unless a test overrides them,
- explicit BIOS/no-BIOS mode,
- fixed random seeds for any test-only fuzzing.

Useful deterministic outputs:

- frame number,
- cycle count,
- PC/CPSR or equivalent CPU state,
- frame hash,
- changed-frame count,
- save hash,
- exact exception/crash classification.

## Debug Hooks Are Architecture

Do not bolt debugging on at the end. Debug hooks should be first-class, low-overhead events:

- instruction trace line formatter,
- trace range filters,
- trace frame filters,
- bounded trace tail,
- memory read/write observers,
- IO read/write observers,
- DMA start events,
- interrupt enter/return events,
- software interrupt/system-call events,
- framebuffer/debug-layer capture.

The hooks should be cheap when disabled and precise when enabled. A full instruction trace is often too much; a trace tail plus watchpoints is usually enough.

## Timing Model

Use a scheduler or comparable event model from the beginning. Even if early timing is rough, every subsystem should speak in cycles or machine ticks rather than "call this after each frame."

Timing bugs often look like control-flow bugs:

- interrupt returns to the wrong place,
- DMA runs too early or reenters itself,
- polling loops see flags in the wrong order,
- audio/FIFO starvation perturbs game state,
- generated code is copied before it is complete.

Build timing visibility into the architecture before chasing these.

## No-BIOS And Real-BIOS Modes

If the original machine has a boot ROM or firmware, support two paths:

- real firmware execution when a firmware image is available,
- clean-room high-level boot/BIOS behavior for convenience and tests.

The no-firmware path is often where retail compatibility bugs hide. It must reproduce the documented post-boot register state, stack setup, interrupt trampoline behavior, firmware service side effects, and memory mirrors closely enough that games cannot tell.

Keep this code isolated and heavily documented. It is compatibility-sensitive and easy to accidentally turn into a pile of game-specific hacks.

## Frontend Separation

The frontend should handle:

- ROM selection,
- save file paths,
- input mapping,
- audio/video presentation,
- speed controls,
- debug display toggles.

It should not implement hardware behavior. When a frontend feature needs hardware state, expose it through the core in a deliberate API.
