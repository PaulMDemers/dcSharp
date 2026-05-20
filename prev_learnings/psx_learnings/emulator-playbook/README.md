# Emulator Bring-Up Playbook

This documentation pack captures the techniques, tools, and process that proved useful while building `psxSharp`. It is intentionally console-neutral: the next emulator may target different hardware, but the workflow lessons still apply.

## Files

- [01-research-and-scope.md](01-research-and-scope.md): how to research a console and set useful milestones.
- [02-core-architecture.md](02-core-architecture.md): core layout, timing, bus, devices, and frontend boundaries.
- [03-diagnostics-and-tooling.md](03-diagnostics-and-tooling.md): CLI tools, counters, traces, screenshots, and fast scans.
- [04-compatibility-process.md](04-compatibility-process.md): a repeatable process for moving games from black screen to playable.
- [05-performance-and-frontend.md](05-performance-and-frontend.md): backend throughput, frame pacing, and rendering frontend lessons.
- [06-low-level-systems.md](06-low-level-systems.md): CPU, DMA, GPU, CD, audio, input, and BIOS bring-up lessons.
- [07-tests-and-regression.md](07-tests-and-regression.md): unit tests, smoke tests, compatibility sweeps, and artifact review.
- [08-project-checklists.md](08-project-checklists.md): practical checklists for a new emulator project.

## Biggest Lessons

1. Build the emulator core as a deterministic headless library before investing in frontend polish.
2. Treat every black screen as a missing measurement, not a single kind of failure.
3. Create a CLI runner early. It should boot software, dump frames, trace reads/writes, stop on interesting events, and run long unattended sweeps.
4. Make diagnostics monotonic when resets are expected. A console reset, GPU reset, DMA reset, or boot handoff can erase local counters and hide the event you needed.
5. Separate correctness from presentation. The emulator can be running fast enough while the UI drops frames, or the UI can look smooth while the core is starved.
6. Add compatibility evidence in layers: CPU exceptions, interrupt state, DMA traffic, media reads, video commands, audio activity, input polls, visible pixels.
7. Prefer real hardware behavior or trusted references over convenient HLE guesses. HLE is useful, but compatibility eventually forces the low-level path.
8. Performance work needs measurement, not vibes. Track emulated steps per second, one-core utilization, allocation rate, frame posts, frame drops, and renderer state.

## Suggested Reading Order

Start with research and architecture. Then read diagnostics and compatibility together. Performance and low-level systems are best read once the core can boot simple software. Keep the checklists nearby during implementation.

