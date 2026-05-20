# Tests And Regression

## Unit Tests

Unit tests should cover behavior that can be isolated:

- CPU instructions and exceptions.
- Memory map aliases.
- Device register reads and writes.
- FIFO behavior.
- DMA transfers.
- Video command parsing.
- Media sector decoding.
- Audio decode primitives.
- Save-state serialization if present.

Use small deterministic fixtures. Add a test whenever a bug can be reduced to a narrow behavior.

## Generated Test Programs

Generated homebrew or synthetic executables are extremely useful:

- draw known shapes,
- write known pixels,
- trigger DMA,
- poll input,
- stream media,
- play a simple sound,
- intentionally raise exceptions.

Generated programs are legal, reproducible, and tailored to your current subsystem.

## Smoke Tests

Smoke tests should answer "does this still basically work?"

- Boot firmware for N steps.
- Boot a small homebrew and dump a frame.
- Boot a target title to first visible frame.
- Run an input script to title screen.
- Verify no unexpected CPU exception.
- Verify active subsystem counters changed.

Keep smoke tests short enough to run often.

## Compatibility Sweeps

For larger sets, automate:

- media discovery,
- per-title command generation,
- timeouts,
- screenshot capture,
- summary JSON,
- summary CSV,
- summary Markdown,
- optional HTML dashboard.

Sweeps should classify outputs, not just pass/fail. Categories like "BIOS menu", "black with CD activity", "visible", and "exception" guide next work.

## Artifact Review

Review visual artifacts side by side:

- framebuffer,
- VRAM atlas,
- first visible frame,
- frame sequence,
- logs around the first geometry or media transfer.

Screenshots often reveal display-origin, color-channel, interlace, palette, or clipping bugs faster than code inspection.

## Regression Strategy

Every compatibility fix risks another title. After focused fixes:

1. Run the narrow unit test.
2. Run the affected title.
3. Run adjacent subsystem tests.
4. Run a focused compatibility set.
5. Run a broad sweep before calling the milestone complete.

Record the command and artifact folder for the run.

## What To Assert

Avoid brittle screenshot equality early. Prefer robust assertions:

- no CPU exception,
- PC leaves firmware,
- first visible frame occurs,
- nonzero visible pixel count,
- expected command class appears,
- media sector range is read,
- input poll count increases,
- DMA channel transfers,
- audio frames are produced.

As accuracy improves, add stricter visual or state comparisons.

## Keep Test Assets Legal

Do not ship copyrighted firmware, games, SDK samples, or proprietary assets. Use:

- generated binaries,
- public-domain homebrew,
- user-provided firmware and media,
- tests that synthesize sectors or files in memory.

Document expected local paths without committing the assets.

