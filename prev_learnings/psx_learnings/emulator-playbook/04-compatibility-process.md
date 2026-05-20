# Compatibility Process

## Compatibility Is Evidence Gathering

Treat every title as a black-box system test. Do not start by guessing the fix. First answer:

- Did firmware boot?
- Did the executable start?
- Did CPU exceptions occur?
- Are interrupts firing?
- Is input being polled?
- Are media commands active?
- Is data being transferred by DMA or CPU?
- Are video commands submitted?
- Are pixels changing in the active display area?
- Is audio doing anything?

Only then decide which subsystem owns the failure.

## Use A Repeated Pass Structure

For each title or demo:

1. Run a short smoke test.
2. Capture current state and first frame.
3. Classify the observed behavior.
4. Add a targeted stop condition or trace.
5. Make a narrow fix.
6. Add a unit or regression test when possible.
7. Re-run the focused title.
8. Re-run a broader sweep to catch regressions.
9. Save artifacts and update compatibility notes.

This process keeps compatibility work from becoming random whack-a-bug.

## Prefer Targeted Titles

Use titles as subsystem probes:

- A simple homebrew rendering test for GPU commands.
- A BIOS-heavy title for firmware callbacks.
- A streaming game for CD or cartridge timing.
- A 3D game for transform hardware.
- A rhythm or audio-heavy game for sound timing.
- A save-heavy game for memory card or persistent storage.
- A title with high resolution or interlace modes for video output.

One "A+ target" game can drive deep polish, but broad sweeps prevent overfitting.

## Understand Long Loading Paths

Do not assume black means stuck. A title may load for many emulated seconds before drawing useful geometry. In `psxSharp`, one retail title looked black for a long stretch, but a fast scan later showed:

- media streaming continued,
- task state changed,
- eventually geometry was submitted,
- the loading screen rendered correctly.

The lesson is to use long-run scans and stop-on-geometry or stop-on-visible-frame before calling a screen a hard failure.

## Separate "Only Clears" From Real Drawing

A video device can receive commands while still showing black. Track command classes separately:

- setup commands,
- clears,
- image uploads,
- VRAM copies,
- rectangles,
- lines,
- triangles,
- textured primitives,
- display mode changes.

The first real geometry or textured blit is a more meaningful milestone than the first command.

## Media Streaming Needs Special Attention

Media systems are common compatibility blockers:

- command FIFO timing,
- response FIFO behavior,
- data-ready interrupts,
- sector size modes,
- raw vs cooked sector layout,
- seek/read command sequencing,
- DMA request behavior,
- buffer overrun and underflow behavior,
- audio/data sector filtering.

When a title loops around a loader, inspect the media command sequence and the destination buffers before changing CPU or GPU code.

## Use Real Firmware Paths

Direct executable boot is useful, but not always representative. Retail software often relies on firmware-initialized state:

- interrupt masks,
- callback tables,
- device modes,
- memory layout,
- stack setup,
- media controller state,
- persistent settings.

If a bug appears only in retail boot, test through real firmware. If direct boot works but firmware boot does not, the issue may be handoff state rather than the game itself.

## Compatibility Notes Should Be Concrete

For each title, record:

- command used,
- firmware used if any,
- steps or emulated time,
- visible milestone reached,
- input used,
- exceptions,
- main active subsystems,
- screenshot path,
- suspected blockers,
- last known good run.

Avoid notes like "probably GPU." Write "GPU receives only setup and black clear commands through 400M instructions; CD streaming continues; no geometry submitted."

