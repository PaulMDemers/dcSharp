# Testing And Compatibility

Compatibility is a measurement practice. The goal is not "this game worked once"; the goal is a repeatable report that says what was run, how far it got, what changed on screen, what input was sent, what save state existed, and why it failed.

## Test Layers

Use several layers because each catches a different class of bug.

| Layer | Purpose |
| --- | --- |
| Unit tests | CPU instructions, bus mapping, save protocols, timers, pure functions. |
| Test ROMs | Hardware behavior across CPU, interrupts, DMA, video, audio, and peripherals. |
| Smoke retail set | Fast signal that real software still boots. |
| Curated milestone set | Popular, technical, and peripheral-heavy titles that represent user expectations. |
| Save roundtrip tests | Prove persistence through the game-facing protocol, not just file writes. |
| Visual baselines | Detect renderer regressions and script drift. |
| Full archive sweep | Find long-tail failures and cluster bugs by signature. |
| Manual/reference play | Validate real gameplay, audio, pacing, and UX. |

## Unit Tests

Unit tests should be small and exact. Good targets:

- instruction decode masks,
- flag behavior,
- PC/pipeline effects,
- load/store alignment,
- block transfer address ordering,
- exception entry and return,
- interrupt acknowledgement,
- DMA register behavior,
- memory mirrors,
- save protocol state machines,
- decompression/math firmware helpers.

Every compatibility fix should leave a regression test if the behavior can be isolated. If it cannot be isolated yet, document the ROM, frame, PC, and trace signature.

## Compatibility Phases

A useful compatibility command runs multiple phases per ROM. The exact phases depend on the console, but this pattern worked well:

- `boot`: run to an early frame with no input.
- `start-probe`: press Start or equivalent after the intro/title has time to appear.
- `broad-input`: send a mixture of common buttons/directions over hundreds of frames.
- `long-input`: run longer with input to catch later crashes.

Classify each phase with structured fields:

- status: boot, crash, timeout, static, invalid PC, etc.
- frame count,
- step/cycle count,
- frame hashes,
- distinct/changed frames,
- last changed frame,
- CPU state,
- video registers/state,
- save type,
- title/game code or media identifier,
- path,
- compact error.

Do not make the CSV too clever. It should be easy to group and sort.

## Frame Budgets

Fixed step caps can create false failures. A 120-frame boot probe and an 1,800-frame gameplay probe should not share the same absolute instruction budget.

Use frame-relative budgets, such as "N CPU steps per requested frame," plus wall-clock guards. Keep a strict fast mode for smoke tests, but use a fair frame-scaled budget for compatibility.

## Save Tests

A save backend is not proven because a file exists. A good save probe:

1. Detects the save type from the ROM/media.
2. Writes through the emulated game-facing protocol.
3. Exports save bytes.
4. Loads those bytes into a fresh system.
5. Reads/verifies through the emulated game-facing protocol.

For real gameplay saves, add a second layer:

1. Drive the game with an input script until it performs an in-game save.
2. Measure changed bytes against erased/default memory.
3. Reload read-only.
4. Verify a visual or memory checkpoint.

This distinguishes "save backend works" from "game actually reached a save and persisted progress."

## Visual Baselines

Frame hashes are useful, but exact framebuffer baselines are better.

A visual verifier should:

- run the ROM with deterministic input and save fixtures,
- capture a specific frame,
- compare to a baseline image,
- emit actual and diff images,
- allow small tolerances only when justified by animation or nondeterministic timing.

Use debug layer captures too. A full frame may look wrong because one background layer is wrong, an object layer is wrong, or blending/windowing is wrong. Layer captures narrow the suspect.

## Input Scripts

Frame-indexed input scripts are more robust than ad hoc "press Start after N seconds" code.

Recommended properties:

- human-readable text format,
- frame ranges,
- multiple keys per frame,
- comments,
- reusable across CLI, visual verifier, and save-roundtrip tools,
- absolute frame semantics by default.

For deep scripts, add state snapshots to the CLI. Long scripts are much easier to debug when the report includes game-specific or generic memory watch summaries.

## Full Archive Sweeps

Full-library sweeps are valuable but noisy. Treat them as data collection, not a final grade.

Required safety features:

- chunking,
- resume,
- per-process timeout,
- process-tree cleanup,
- low process priority,
- capture only selected failure classes,
- cumulative merge,
- retry timeout rows separately,
- fold best results after retry.

After a sweep, run an analyzer that groups by:

- status/classification,
- error signature,
- PC range,
- phase,
- save type,
- ROM/media path class,
- index block,
- title/game code.

Then triage archive noise away from high-signal failures. Hacks, bad dumps, videos, demos, tools, virtual-console injects, unlicensed software, and duplicate weirdness should not drive core priorities unless the project explicitly targets them.

## Compatibility Grades

Keep separate grades:

- boot compatibility,
- input-probe compatibility,
- save compatibility,
- visual regression compatibility,
- manual gameplay compatibility,
- audio/timing accuracy,
- peripheral accuracy.

One number hides too much. A game can boot, survive input, and still have broken audio, bad saves, or incorrect physics.
