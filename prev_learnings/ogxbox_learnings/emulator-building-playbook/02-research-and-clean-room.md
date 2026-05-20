# Research And Clean-Room Notes

## Start With Public Facts

Before writing code, collect stable facts:

- CPU architecture and instruction set.
- Memory map and mirror behavior.
- Boot flow and reset vector.
- Executable or ROM format.
- Storage/media format.
- Device register maps.
- Interrupt and DMA model.
- Video and audio output path.
- Controller/input protocol.
- Firmware, BIOS, OS, or kernel boundaries.

Keep these notes source-linked when possible. A future maintainer should be able
to tell which behavior came from documentation, test hardware, emulator
comparison, or inference.

## Keep Legal Boundaries Explicit

Do not ship proprietary firmware, BIOS, keys, ROMs, SDK files, dashboards,
system apps, game content, or copyrighted assets. Accept user-supplied dumps
only, and document what users must provide themselves.

When studying other emulators:

- Read architecture and behavior, not implementation you cannot reuse.
- Respect license boundaries.
- Avoid copying code unless the project intentionally adopts a compatible
  license.
- Prefer public docs, hardware tests, traces, and your own measurements.
- Keep notes in your own words.

## Record Confidence

Every behavioral note should carry one of these labels:

- **Documented:** verified by official or community hardware documentation.
- **Observed:** seen in a trace, hardware run, or emulator run.
- **Inferred:** likely based on surrounding behavior, but not proven.
- **Synthetic:** intentionally approximated to unblock progress.

This prevents synthetic behavior from becoming folklore.

## Build Tiny Tests From Research

Convert research into tests quickly:

- Parser tests for file headers and section tables.
- Memory tests for mirrors, MMIO dispatch, and bounds.
- CPU instruction tests for flags and edge cases.
- Device register tests for read/write side effects.
- Renderer tests for primitive assembly, texture addressing, and color math.
- HLE tests for handle lifetimes, file reads, timers, and synchronization.

When a bug is fixed from a trace, add a narrow regression test if the behavior is
stable enough to test without the full game.

## Compare, But Do Not Chase Blindly

Comparison against mature emulators is useful, especially for:

- Register reset values.
- Device initialization sequences.
- Filesystem expectations.
- GPU command ordering.
- Known weird hardware behavior.

But comparison alone can mislead. Mature emulators often contain compatibility
workarounds, old assumptions, or code paths for many hardware variants. Always
ask: "What evidence says my target software needs this behavior?"
