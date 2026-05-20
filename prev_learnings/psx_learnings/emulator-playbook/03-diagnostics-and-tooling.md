# Diagnostics And Tooling

## Build The CLI Early

A command-line runner is the most useful emulator frontend during bring-up. It should support:

- Firmware boot.
- Direct executable boot.
- Media boot.
- Fixed-step runs.
- Long-run safety flags.
- Progress logging.
- Frame dumps.
- VRAM or framebuffer dumps.
- Memory dumps.
- Disassembly dumps.
- Stop-on-PC and stop-on-range.
- Stop-on-exception.
- Stop-on-device-command.
- Stop-on-memory-write.
- Input scripting.

Desktop UI is important, but the CLI is where you can run fast, repeatable, unattended experiments.

## Instrument Every Subsystem

Good emulator diagnostics summarize activity without requiring a debugger:

- CPU: PC, next PC, exception, registers, decoded block stats, hot loops.
- Interrupts: status, mask, pending source, recent requests and acknowledgements.
- DMA: channel transfer counts, recent transfer summaries.
- GPU/video: command counts, display mode, display origin, drawing area, VRAM write sources, visible pixel count.
- Media: command counts, sector reads, current LBA, buffered bytes, data-ready state, overrun/underflow counts.
- Audio: key-on/key-off counts, active voices, buffer state, decoded frames.
- Input: polls, last request, last buttons, interrupt behavior.

Every line should answer "what is the program trying to do?"

## Use Stop Conditions

Stop conditions turn vague reports into precise evidence:

- Stop on CPU exception.
- Stop on first visible frame.
- Stop on first GPU image upload.
- Stop on first geometry command.
- Stop on a particular media LBA.
- Stop when a register changes.
- Stop when a DMA channel transfers from a suspicious address.
- Stop after a specific wall-clock time.

Add new stop conditions whenever you find yourself manually scanning logs.

## Keep Counters Monotonic For Long Runs

Device reset counters are useful, but long-run scans need monotonic diagnostic counters:

- Total device commands since process start.
- Total drawable commands since process start.
- Per-opcode command sequence numbers.
- Last source address per opcode.
- Last PC per event class.

This prevents a reset from erasing the event that proves progress.

## Capture Artifacts

For every compatibility run, save enough to compare later:

- Screenshot or framebuffer dump.
- VRAM atlas where relevant.
- Text summary.
- CSV or JSON summary.
- Recent device commands.
- Recent DMA transfers.
- Exception and interrupt state.
- Command-line arguments used.

Artifacts beat memory. They also help detect regressions that "feel" minor.

## Black Screen Triage

A black screen is not one bug. Classify it:

- CPU not executing useful code.
- CPU stuck in firmware.
- CPU exception loop.
- Interrupt not delivered.
- Input not acknowledged.
- Media command stuck.
- Media data present but not consumed.
- DMA not running.
- GPU commands not submitted.
- GPU commands submitted but only clears.
- GPU draws outside display area.
- Display origin or mode wrong.
- Framebuffer is valid but frontend does not present it.

The fix depends on which category evidence supports.

## Fast Scans

When performance allows, add scan modes that trade detailed per-instruction checks for quick event detection:

- Run decoded or batched CPU loops.
- Check monotonic device counters after batches.
- Print progress every fixed interval.
- Stop on high-value events like geometry, media reads, or exceptions.

Fast scans are ideal for "does this game ever leave the loading loop?" questions.

## Watch Ranges

Memory watch ranges are one of the highest leverage tools:

- Watch a task state structure.
- Watch a media buffer.
- Watch a DMA list pointer.
- Watch a callback table.
- Watch a framebuffer address.
- Watch a device register mirror.

Include PC, value, size, and nearby bytes in trace output.

