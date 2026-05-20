# Core Architecture

## Keep The Core Headless

The core should not know about windows, menus, file dialogs, or desktop UI controls. It should expose:

- Load firmware.
- Attach media.
- Reset.
- Run for N cycles or instructions.
- Read current video/audio/input state.
- Save or restore state.
- Query diagnostics.

Frontends can be CLI, desktop, web, or test harnesses. They should depend on the core, not the other way around.

## Use A Bus-Centric Design

Most consoles are a CPU plus a memory map plus devices. A clean bus makes the rest of the emulator easier:

- Normalize mirrored and aliased addresses at the boundary.
- Route reads and writes to RAM, ROM, device registers, and unmapped regions.
- Track current CPU PC for diagnostics.
- Provide optional watch ranges for debugging.
- Keep device register side effects in devices, not in the CPU.

When a game fails, being able to say "the CPU wrote this value to this device register from this PC" is priceless.

## Make Time Explicit

Even a simple emulator needs a timing model:

- CPU instructions consume cycles.
- Devices schedule future events.
- Interrupts are raised through a shared interrupt controller.
- DMA can be immediate at first, but should have a path toward timed behavior.
- Video and audio should advance from emulated time, not host frame callbacks.

Start coarse, but do not hide time in random frontend timers.

## Prefer Deterministic Stepping

The core should support:

- Single instruction stepping.
- Running a fixed number of instructions or cycles.
- Reproducible tests with fixed inputs.
- Event hooks at instruction boundaries.
- Stop conditions on PC, exception, register, memory write, device command, or visible frame.

This makes bugs reproducible. It also makes long compatibility sweeps possible.

## Separate Emulated Work From Presentation

Emulation and presentation have different clocks:

- The core runs according to console time.
- The renderer presents frames according to host display pacing.
- Audio consumes a steady stream.
- Input is sampled and translated into device state.

Do not let UI repaint frequency define emulated time.

## Expect Resets Inside Software

Firmware and games may reset devices during normal boot. Diagnostics must survive those resets when you need long-run evidence. Use separate counters:

- Device-local counters that model emulated reset state.
- Monotonic diagnostic counters that survive resets.

This distinction mattered repeatedly in `psxSharp`: GPU packet counters that reset on GP1 reset were correct for emulated state, but bad for long fast scans.

## HLE Is A Tool, Not The Destination

High-level emulation can get early milestones moving:

- BIOS calls.
- File loading.
- Simple callbacks.
- Synthetic boot paths.

But it can also hide missing low-level behavior. Retail compatibility usually demands the real path:

- Firmware boot.
- Device command FIFOs.
- DMA behavior.
- Interrupt timing.
- Media streaming.
- Input polling.

Use HLE to learn, then replace it with low-level behavior when compatibility asks for it.

