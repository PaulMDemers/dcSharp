# HLE And Fast-Path Techniques

## What HLE Is Good For

High-level emulation is useful for:

- Firmware, BIOS, OS, or kernel calls.
- File APIs.
- Thread and synchronization primitives.
- Timers.
- Known library helper loops.
- Device setup shortcuts.
- Homebrew runtime support.

It is also dangerous. A broad HLE shortcut can hide a real bug, corrupt guest
state, or make one test pass while breaking another.

## Add HLE Only With Evidence

A good HLE fast path has:

- A recognizable instruction pattern or call signature.
- Validated arguments.
- Bounded memory access.
- Deterministic side effects.
- Correct register, stack, and flag landing state.
- Trace logging.
- Tests or at least before/after run artifacts.

Avoid "if address X, skip it" unless address X is stable across the exact input
and you have no better option. Pattern recognition is safer than hard-coded
addresses.

## Fast-Path Helper Loops

Many systems spend huge time in small helper loops:

- `memcpy`, `memset`, string scans.
- Table fills.
- Color conversion.
- Texture uploads.
- Matrix/vector math.
- Sorts or list operations.
- Synchronization spin loops.

Replacing these with exact bounded host implementations can change a run from
minutes to seconds. The key is proving the helper pattern and preserving guest
state.

## Validate Memory Before Touching It

Before an HLE helper reads or writes guest memory:

- Check the range is mapped.
- Check multiplication does not overflow.
- Cap counts to a sane maximum.
- Use translated/guest memory APIs rather than host pointers.
- Handle memory exceptions by rejecting the HLE path.

If validation fails, fall back to normal CPU execution where possible.

## Preserve Landing State

For call-like helpers:

- Read return address from the guest stack.
- Adjust stack exactly as the guest call convention requires.
- Set return registers.
- Preserve registers that the real helper would preserve.
- Set flags if the guest observes them.
- Move the instruction pointer to the return address.

When uncertain, add a comparison mode that executes the helper normally and
compares the final state against the HLE result.

## HLE Logging

Trace HLE events with names like:

- `hle:memcpy`
- `hle:kernel-open-file`
- `hle:gpu-progress-wait`
- `hle:texture-upload-loop`
- `hle:staged-vertex-flush`

When a profiler says the hottest address is an HLE entry point, treat that as a
call-count marker. Inspect the next real guest helper or loop to find the actual
blocker.

## Synthetic Behavior

Sometimes HLE must synthesize behavior to expose the next layer. Examples:

- Pretend a device wait completed.
- Satisfy a semaphore.
- Convert guest-staged vertices into renderer submissions.
- Build a texture from a recognized procedural upload loop.

These can be useful, but they must be named and bounded. Prefer synthetic
behavior that is easy to remove once the real device path exists.

## Signs An HLE Path Is Too Broad

Watch for:

- It matches unrelated software.
- It writes into unknown global state.
- It assumes one memory layout.
- It changes frame output in unrelated tests.
- It hides the difference between two APIs.
- It makes counters better but screenshots worse.

When that happens, narrow the recognizer or split the path into variants.
