# Observability And Test Harnesses

## Instrument First

The fastest way to stall an emulator project is to run software and only know
"it hung." Every run should produce enough evidence to answer:

- How far did the guest get?
- What was the last useful milestone?
- What device or subsystem was active?
- Did the guest make progress recently?
- Was the stop intentional, a crash, a timeout, or a host safety limit?
- What should be inspected next?

## Essential CLI Controls

Implement these early:

- Maximum instruction count.
- Wall-clock time limit.
- Host memory limit.
- Stop after CPU address or occurrence.
- Stop after device events, such as file read, DMA submit, GPU method, triangle,
  present, audio submit, or input poll.
- Trace tail count.
- Device log counts.
- Progress interval.
- Frame dump path.
- Compatibility suite output directory.

These controls turn a vague hang into a small reproducible investigation.

## Useful Run Summaries

A run summary should include:

- Loaded image name and entry point.
- Executed instruction count.
- Stop reason.
- CPU halt/crash/fault information.
- File opens, reads, writes, bytes transferred.
- Device MMIO read/write counters.
- DMA submissions and decoded commands.
- Graphics methods, clears, primitives, presents, frame source.
- Input polls/transfers.
- Recent progress milestones.
- Hot code addresses if profiling is enabled.

One-line summaries are especially valuable in suite output.

## Milestone Stops

Milestone stops are more useful than waiting for guest exit.

Examples:

- Stop after first file open.
- Stop after N file reads.
- Stop after first decoded display command.
- Stop after first clear.
- Stop after first primitive.
- Stop after first nonblank frame.
- Stop after first input packet.
- Stop after first audio buffer.

When a milestone succeeds, move the target forward. This keeps work focused on
the next blocker instead of repeatedly proving the same early path.

## Traces Should Be Bounded

Full traces become enormous and often slow the emulator enough to change timing.
Use bounded traces:

- Last N decoded instructions.
- Last N memory accesses.
- Last N IO accesses.
- Last N device events.
- Optional address-range tracing.
- Optional breakpoint-on-read/write/execute.

Trace tails are usually enough to identify the active helper, loop, syscall, or
device interaction near a failure.

## Frame And Fragment Probes

For graphics work, do not rely only on screenshots.

Add:

- Frame dumps.
- Nonzero pixel counts.
- Unique color counts.
- Frame fingerprints.
- Source of the frame, such as renderer snapshot, guest framebuffer, or memory
  write hotspot.
- Fragment probe at a coordinate or automatic best candidate.
- Texture sample diagnostics.

This distinction mattered a lot: several runs counted primitives but produced no
rasterized fragments, while others drew visible frames with flat colors.

## Compatibility Suite Runner

A suite runner should:

- Discover images in a folder.
- Run each with identical options.
- Create per-image text logs and frame dumps.
- Generate aggregate CSV/Markdown summaries.
- Classify each result.
- Support name filters and slices.
- Fail optionally on crashes or regressions.

Include enough data in each row to sort by next blocker, not just pass/fail.

Recommended columns:

- Image name.
- Classification.
- Stop reason.
- Steps.
- File operations.
- Device counters.
- Graphics counters.
- Frame source.
- Nonzero pixels.
- Unique colors.
- Fragment probe result.
- Error type and address.

## Artifacts Are Part Of The Product

Keep artifacts organized by timestamped run directories. Do not depend on memory
or chat history to remember what changed.

Good artifact names:

- `suite-homebrew-YYYYMMDD-HHMMSS`
- `retail-smoke-YYYYMMDD-HHMMSS`
- `title-after-fix-name.png`
- `title-after-fix-name.txt`

The goal is to make regression hunting boring.
