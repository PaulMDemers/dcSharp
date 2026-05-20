# Safety, Performance, And Host Health

## Guest Software Is Untrusted

Treat every guest image as untrusted input.

Use:

- Wall-clock limits.
- Instruction limits.
- Host memory limits.
- Bounded traces.
- Bounded dumps.
- Bounded file IO logs.
- Safe path handling for mounted media.
- Explicit process cleanup after long suite runs.

The emulator should not be able to lock up the host through runaway allocation,
unbounded logging, endless background processes, or recursive filesystem work.

## Host Safety Checks

Before and after long runs:

- Check for leftover emulator processes.
- Shut down build servers if needed.
- Avoid launching too many heavy runs in parallel.
- Keep suite timeouts realistic.
- Prefer slices over one enormous run.

Parallelism is useful for file inspection and small commands. Emulator runs are
often CPU-heavy and can distort timing or cause wall-clock failures when run in
parallel.

## Bounded Memory Operations

Every host-side helper should guard:

- Guest address validity.
- Range length.
- Multiplication overflow.
- Maximum count.
- Destination mapping.
- Source mapping.

Reject unsafe helper paths and fall back to normal execution when possible.

## Performance Strategy

Optimize only after observability identifies the bottleneck.

High-value optimizations:

- Exact fast paths for common helper loops.
- Efficient memory translation.
- Reduced trace overhead.
- Device command batching.
- Renderer fast paths for common formats.
- Caching decoded instructions or commands.

Low-value early optimizations:

- Broad rewrites before compatibility is measurable.
- Native acceleration before the behavior is correct.
- Complex render backends before the software renderer can explain failures.

## Reproducibility

For each compatibility run, capture:

- Emulator version or commit if available.
- Command-line options.
- Input image name.
- Artifact directory.
- Stop reason.
- Counters.
- Frame fingerprint.

Reproducibility beats speed when debugging regressions.

## Failure Hygiene

When something fails:

- Preserve the artifact.
- Name the stop reason clearly.
- Include address, width, and access type for memory faults.
- Include opcode bytes for unimplemented instructions.
- Include register state for CPU faults.
- Include register/method names for device faults.

A good failure message should suggest the next command to run.

## Avoid Runaway Suites

Suite runners should support:

- Per-image time limits.
- Whole-suite timeout awareness.
- Name filters.
- Slices.
- Resume or skip completed images.
- Fail-on-error mode for CI.
- Non-fail mode for exploratory sweeps.

If a full suite takes too long, do not keep increasing the timeout. Split it into
clusters and improve the stop conditions.

## Keep The User In Control

For a frontend or UI:

- Show current image, stop reason, frame source, and counters.
- Provide pause/stop/reset.
- Surface host memory usage.
- Avoid hiding long-running work.
- Make artifact paths visible.

Manual testing should feel safe and recoverable.
