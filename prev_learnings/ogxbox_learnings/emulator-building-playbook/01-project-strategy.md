# Project Strategy

## Choose The Initial Emulation Style Deliberately

Most emulator projects drift between low-level emulation, high-level emulation,
and compatibility hacks. Pick the initial posture explicitly.

Common options:

- **Low-level emulation:** Emulate CPU, memory map, devices, interrupts, DMA,
  display, audio, storage, and boot flow. This is usually the path to best
  long-term compatibility, but it is slow to reach first pixels.
- **High-level emulation:** Load guest executables and replace firmware,
  kernel, OS, library, or SDK calls with host implementations. This reaches
  homebrew and simple demos faster, but each missing behavior becomes a
  compatibility cliff.
- **Hybrid:** Start with enough high-level behavior to make software run, while
  preserving a path toward lower-level devices and more accurate timing.

The hybrid approach worked well here because it let us reach real rendering
early while still building the lower-level memory, DMA, storage, and GPU
machinery needed for broader compatibility.

## Build The Project As Tools First

An emulator without tooling becomes guesswork. Build these surfaces early:

- CLI runner with instruction and wall-clock limits.
- Structured summaries for CPU, memory, files, devices, graphics, and input.
- Trace tails for recent instructions and events.
- Stop conditions for meaningful milestones.
- Frame dumps and fragment probes.
- Suite runner that creates per-image logs and aggregate summaries.
- Unit tests for decoders, loaders, memory, device registers, and renderer math.

The CLI should be usable before the frontend is polished. The frontend can make
manual testing pleasant, but the CLI is what keeps compatibility work repeatable.

## Separate Concerns

Keep these modules distinct even in a small project:

- **Loader/media:** Parse executable, ROM, disc, cartridge, filesystem, save,
  BIOS, and metadata formats.
- **Memory:** Map address regions, RAM, ROM, mirrors, watchpoints, MMIO, and DMA
  views.
- **CPU:** Decode, execute, flags, exceptions, privilege, timers, interrupts.
- **Kernel/firmware/HLE:** System calls, object handles, threads, file APIs,
  timers, synchronization.
- **Devices:** GPU, audio, storage, input, bus, DMA, timers, interrupts.
- **Harness:** Runs, suites, logs, artifacts, compatibility classification.
- **Frontend:** Window, input bindings, audio output, frame display, settings.

This separation made it possible to improve one path, such as synthetic GPU
submission, without disturbing file IO or CPU execution.

## Grow By Milestones

A practical milestone ladder:

1. Parse image and print metadata.
2. Map memory and enter executable code.
3. Execute instructions to a deterministic stop.
4. Satisfy enough firmware/kernel calls to keep code moving.
5. See file or asset reads.
6. See device MMIO.
7. Decode first GPU command or equivalent display command.
8. Clear a framebuffer.
9. Draw first primitive.
10. Sample first texture.
11. Accept input.
12. Reach first menu or gameplay-like loop.

Each milestone should have a command that proves it happened.

## Avoid Premature Accuracy Traps

Accuracy matters, but implementing every detail before running software can bury
the project. A useful balance:

- Be exact where state escapes into guest-visible behavior.
- Be approximate where the guest only needs progress.
- Mark approximations clearly.
- Keep approximations bounded and testable.
- Revisit approximations once compatibility data shows they are blocking more
  software.

The emulator should always know whether it is doing real emulation, HLE, a
synthetic fallback, or a diagnostic shortcut.
