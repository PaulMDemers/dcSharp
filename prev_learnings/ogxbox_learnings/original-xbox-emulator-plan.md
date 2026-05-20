# ogxbSharp Original Xbox Emulator Plan

## Goal

Build an original Xbox emulator in C# that grows from a testable homebrew/XBE runner into a fuller system emulator. The short-term target is correctness, tooling, and observability. The long-term target is retail title compatibility through progressively lower-level hardware emulation.

## Research Summary

Existing projects split into two useful models:

- **xemu / XQEMU model:** low-level, full-system emulation based on QEMU. It emulates the Xbox hardware and boots with MCPX ROM, flash BIOS, HDD image, and game disc image. This is the compatibility-oriented path, but it implies a full x86 CPU, PCI/AGP devices, NV2A GPU, MCPX/APU, IDE, USB, EEPROM, and boot-chain behavior.
- **Cxbx / Cxbx-Reloaded model:** high-level emulation plus direct code execution, historically taking advantage of the Xbox being x86-based. It converts/loads XBE executables, intercepts kernel and library calls, and wraps or reimplements Xbox APIs. This is faster to first pixels, but less complete and harder to make universally compatible.

Important Xbox traits:

- CPU: custom 733 MHz Pentium III-class x86 CPU with 128 KiB L2 cache.
- Memory: retail units have 64 MiB unified CPU/GPU RAM; debug/Chihiro systems have 128 MiB.
- GPU/chipset: NVIDIA NV2A, both northbridge and GPU, derived from the NV20 family.
- Southbridge/audio/network: NVIDIA MCPX/nForce-like devices.
- Storage: IDE HDD using FATX partitions; game discs use XDVDFS/XISO layout.
- Executables: XBE files are PE-like but Xbox-specific, signed for official retail titles, with Xbox-specific headers, certificates, sections, TLS, and kernel thunk handling.
- Boot: retail full-system emulation needs MCPX boot ROM, flash BIOS, HDD image, and disc image; these must be user-supplied.

## Legal And Licensing Boundaries

- Do not ship BIOS, MCPX ROM, dashboard files, EEPROM dumps, game images, SDK headers, or leaked proprietary Microsoft material.
- Accept user-supplied dumps only.
- Keep research notes source-linked and clean-room. Avoid copying GPL implementation code unless this project intentionally adopts GPL-compatible licensing.
- xemu, XQEMU, and Cxbx-Reloaded are excellent references for behavior and architecture, but direct code reuse has licensing consequences.

## Recommended Strategy

Use a staged hybrid approach:

1. Build a managed emulator framework, media parsers, debugger, and test harness first.
2. Implement a high-level XBE/homebrew path to reach executable loading and kernel calls early.
3. Build an x86 interpreter first for correctness, then add dynamic recompilation only after the memory/device model is stable.
4. Add low-level devices incrementally, comparing behavior against xemu, XboxDevWiki notes, homebrew tests, and real hardware traces where available.

This deliberately avoids betting the project on native direct execution. Direct execution is attractive because Xbox code is x86, but a modern C# process is usually 64-bit, managed, memory-safe by default, and cannot safely run arbitrary 32-bit guest code with Xbox address-space semantics. A C# interpreter/DBT path is slower initially but debuggable, portable, and compatible with future low-level emulation.

## Proposed Solution Architecture

### Projects

- `Ogxb.Core`
  - Emulation clock, scheduler, save-state primitives, logging, configuration, byte-order helpers.
- `Ogxb.Memory`
  - 32-bit address-space map, RAM/ROM regions, MMIO dispatch, memory watchpoints, shadow mappings.
- `Ogxb.Cpu.X86`
  - x86 decoder, interpreter, exception model, FPU/MMX/SSE support, optional DBT backend later.
- `Ogxb.Loader`
  - XBE parser/loader, symbol metadata, import/thunk resolver, XDVDFS reader, FATX reader, EEPROM image handling.
- `Ogxb.KernelHle`
  - Xbox kernel object model, thread scheduling facade, memory APIs, file APIs, synchronization, timers, debug output.
- `Ogxb.Hardware`
  - PCI config space, PIT/PIC/CMOS/RTC, IDE, USB OHCI, SMBus, EEPROM, SMC, NVNet, MCPX APU/ACI.
- `Ogxb.Gpu.Nv2A`
  - NV2A register model, FIFO/push-buffer parser, texture formats, swizzled memory, shader/combiner translation.
- `Ogxb.Frontend`
  - Windowing, input, audio, settings, debugger UI, trace viewers.
- `Ogxb.Tests`
  - Unit tests for parsers/instructions, golden hardware tests, homebrew regression tests.

### Runtime Choices

- Use modern .NET with `unsafe` code only in hot paths where profiling proves value.
- Use `Span<T>`, `MemoryMarshal`, `ArrayPool<T>`, and memory-mapped files for large images.
- Use an existing x86 decoder library if licensing is clean; implement execution semantics ourselves.
- Use SDL/Silk.NET/Vortice-style bindings for windowing, controllers, audio, and graphics backend access.
- Prefer a graphics abstraction that can target Direct3D 11/12 or Vulkan later.

## Milestones

### Milestone 0: Repository And Tooling

Deliverables:

- Create solution/project layout.
- Establish logging, CLI flags, configuration, and test framework.
- Add binary-reader primitives and golden test data for synthetic files.
- Add CI that runs parser and CPU instruction tests.

Exit criteria:

- `ogxb` command starts, prints version/config, loads no copyrighted assets, and runs tests.

### Milestone 1: Media And Executable Loading

Deliverables:

- XBE parser with header, certificate, section table, TLS, library versions, entry point, and kernel thunk table handling.
- XDVDFS/XISO reader sufficient to locate `default.xbe`.
- FATX read-only parser for HDD images and memory-unit images.
- EEPROM image generator with valid non-identifying defaults.

Exit criteria:

- CLI can inspect a legal homebrew XBE/XISO, print metadata, list files, and map sections into guest memory.

### Milestone 2: CPU Interpreter MVP

Deliverables:

- 32-bit x86 protected-mode execution core.
- Integer instructions, branches, stack, calls/returns, flags, exceptions, basic segment behavior.
- FPU/MMX/SSE plan; add instructions as tests demand.
- Instruction trace mode and deterministic stepping.

Exit criteria:

- Runs tiny synthetic XBE/homebrew programs that exercise arithmetic, branches, memory, stack, and kernel-call stubs.

### Milestone 3: Kernel HLE Homebrew Runner

Deliverables:

- Minimal Xbox kernel API implementation for thread startup, memory allocation, events, mutexes, timers, file I/O, debug print, and process termination.
- XBE entry startup and TLS setup.
- Basic host filesystem or virtual mounted media path.

Exit criteria:

- Runs simple legal homebrew command-line or framebuffer tests to completion.

### Milestone 4: First Video And Input

Deliverables:

- Framebuffer path for homebrew.
- Controller abstraction mapping host gamepads/keyboard to Xbox controller state.
- Basic audio sink stub that can be enabled without blocking emulation.

Exit criteria:

- A homebrew sample draws pixels and responds to input.

### Milestone 5: NV2A Bring-Up

Deliverables:

- NV2A MMIO register skeleton.
- FIFO/push-buffer capture and replay tooling.
- Swizzled texture helpers, color/depth surface tracking, DMA object model.
- Initial fixed-function translation to host graphics API.

Exit criteria:

- Can display simple 3D homebrew or known simple samples with visible geometry.

### Milestone 6: Storage, Timing, And Device Fidelity

Deliverables:

- IDE disk/DVD behavior beyond simple file access.
- PIT/PIC/CMOS/RTC behavior.
- USB OHCI controller model for controllers/memory units.
- Scheduler tied to emulated time with deterministic frame pacing.

Exit criteria:

- More demanding homebrew and debug BIOS/kernel experiments behave consistently.

### Milestone 7: Dynamic Recompiler

Deliverables:

- Basic block cache from guest x86 to either .NET IL, expression-tree compiled delegates, or generated native code.
- MMIO exits, page invalidation, self-modifying-code detection, exceptions, and trace fallback.
- Interpreter remains the correctness oracle.

Exit criteria:

- Same test suite passes under interpreter and DBT; measurable speedup on CPU-bound homebrew.

### Milestone 8: Full-System Path

Deliverables:

- MCPX ROM and flash BIOS mapping.
- Boot-vector behavior, protected-mode setup compatibility, PCI device enumeration.
- Kernel boot enough to reach dashboard/game loader with user-supplied legal dumps.

Exit criteria:

- Boots a compatible non-retail/modded/debug BIOS far enough to initialize display or produce diagnosable boot logs.

## Testing Plan

- Parser tests: synthetic XBE, XDVDFS, and FATX images with known offsets and invalid cases.
- CPU tests: generated instruction tests comparing against a trusted x86 oracle where feasible.
- Kernel HLE tests: homebrew test suite that calls one API at a time.
- GPU tests: capture push buffers and compare decoded command streams, not just screenshots.
- Regression tests: deterministic traces for known legal homebrew.
- Compatibility tracking: title/homebrew database with emulator version, settings, status, known issues, and trace attachments.

## Major Risks

- **NV2A accuracy:** the GPU is the hardest part. Pixel combiners, texture swizzling, Xbox-specific formats, render targets, shader variants, and timing will dominate compatibility work.
- **C# DBT complexity:** .NET IL generation can be fast, but x86 flags, exceptions, MMIO, self-modifying code, and FPU/SSE semantics are easy to get subtly wrong.
- **Kernel HLE ceiling:** HLE can run simpler software early, but retail games often depend on exact libraries, kernel behavior, device state, and GPU command semantics.
- **Licensing contamination:** copying from GPL projects could force licensing decisions. Treat existing source as reference material only unless we choose a compatible license.
- **Asset legality:** users need their own dumps; the emulator must make that clear in UX and docs.

## Immediate Next Steps

1. Create the .NET solution and project skeleton.
2. Implement `Ogxb.Loader` with XBE parsing first.
3. Add `Ogxb.Tests` with synthetic XBE fixtures.
4. Add a CLI command: `ogxb inspect <path>`.
5. Implement XDVDFS read-only support.
6. Implement memory map primitives.
7. Start the x86 interpreter with only the instructions needed by tiny homebrew tests.

## References

- xemu home/docs: https://xemu.app/
- xemu about/development model: https://xemu.app/docs/about/
- xemu required files: https://xemu.app/docs/required-files/
- XQEMU required files/full-system model: https://xqemu.com/getting-started/
- Cxbx emulation theory: https://www.caustik.com/cxbx/progress.htm
- Cxbx-Reloaded repository/readme: https://github.com/Cxbx-Reloaded/Cxbx-Reloaded
- XboxDevWiki hardware overview: https://xboxdevwiki.net/Xbox_Hardware_Overview
- XboxDevWiki CPU: https://xboxdevwiki.net/CPU
- XboxDevWiki memory map: https://xboxdevwiki.net/Memory
- XboxDevWiki boot process: https://xboxdevwiki.net/Boot_Process
- XboxDevWiki NV2A: https://xboxdevwiki.net/NV2A
- XboxDevWiki XBE: https://xboxdevwiki.net/Xbe
- XboxDevWiki FATX: https://xboxdevwiki.net/FATX
- XboxDevWiki XDVDFS: https://xboxdevwiki.net/XDVDFS
