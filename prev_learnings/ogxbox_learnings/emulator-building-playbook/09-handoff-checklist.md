# Handoff Checklist

Use this checklist when starting the next emulator project. It is intentionally
platform-neutral.

## Before Coding

- Identify legal asset boundaries and document what users must supply.
- Choose initial emulation style: low-level, high-level, or hybrid.
- List known executable/media formats.
- Sketch memory map and device map.
- Decide first milestone input: tiny ROM, homebrew, test binary, or firmware.
- Create a place for artifacts and compatibility summaries.

## Repository Setup

- Create separate projects or modules for loader, memory, CPU, devices, HLE,
  harness, frontend, and tests.
- Add unit test framework.
- Add logging and structured run summaries.
- Add CLI with instruction limit and wall-clock limit.
- Add a safe artifact output directory.
- Add documentation for required user-supplied files.

## First Harness Features

- Stop at instruction budget.
- Stop at wall-clock budget.
- Stop at guest address.
- Trace last N instructions.
- Dump selected guest memory ranges.
- Count file/media operations.
- Count device register reads/writes.
- Print stop reason and guest state on failure.

## First Compatibility Features

- Parse image and metadata.
- Map memory.
- Enter guest code.
- Run to a deterministic stop.
- Report unmapped memory accesses with address and width.
- Report unimplemented instructions with bytes and address.
- Save per-run logs.

## First Graphics Features

- Count display commands or framebuffer writes.
- Detect clear-only frames.
- Dump frame candidates.
- Track nonzero pixels and unique colors.
- Add a fragment or pixel probe once primitives exist.

## HLE Safety Gate

Before adding an HLE shortcut, confirm:

- Pattern or call signature is specific.
- Arguments are validated.
- Memory ranges are bounded.
- Register and stack landing state are known.
- It logs an HLE event.
- It can be disabled or compared if needed.

## Weekly Compatibility Rhythm

- Run the small smoke set.
- Review regressions first.
- Pick the largest shared blocker.
- Implement the smallest bounded fix.
- Re-run the same smoke command.
- Save artifacts with a timestamp.
- Update notes with what changed and what remains synthetic.

## Red Flags

- Long runs with no stop reason.
- Unbounded logs or memory dumps.
- Host CPU pegged after a run should have ended.
- Screenshots without counters.
- Counters without artifacts.
- Broad HLE paths that match unrelated software.
- "Works in this one title" fixes with no explanation.

## Definition Of A Useful Milestone

A milestone is useful when it has:

- A repeatable command.
- A clear pass/fail or advance/no-advance signal.
- A short enough runtime to run often.
- Artifacts that explain failures.
- A next-step rule when it fails.
