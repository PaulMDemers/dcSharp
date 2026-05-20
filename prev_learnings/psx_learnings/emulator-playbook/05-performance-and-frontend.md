# Performance And Frontend

## Measure Backend And Frontend Separately

Track both:

- emulated instructions or cycles per second,
- target console cycles per second,
- realtime percentage,
- emulation thread CPU utilization,
- total process CPU utilization,
- frame posts per second,
- frame presents per second,
- dropped frames,
- renderer type,
- allocation rate,
- GC counts.

Low process CPU can still mean the emulation thread is saturating one core. High core utilization with low realtime means backend optimization is needed. Good backend speed with low UI FPS means presentation is the bottleneck.

## Console Speed Is A Contract

Know the target hardware clock and video cadence. For PS1, the CPU target was about `33.8688 MHz`, and NTSC video targets around `60 Hz`. Other consoles will have different clocks, but the rule is the same:

- run emulated time at hardware speed,
- present frames at video speed,
- avoid tying the core to host UI timer jitter.

## Batch Carefully

Batching improves throughput:

- Run many instructions per worker loop.
- Tick hardware in controlled batches.
- Use decoded instruction blocks when safe.
- Avoid checking every expensive stop condition on every instruction.

But batching can hide events. Use monotonic counters and batch-end event detection for fast scans.

## Avoid Hot-Path Allocations

Emulators expose allocation mistakes quickly. Watch for:

- per-frame byte arrays,
- trace string formatting in hot paths,
- LINQ in hot loops,
- repeated framebuffer conversions,
- queue churn in high-frequency events,
- boxing in counters or dictionaries.

Keep tracing optional and bounded. Reuse buffers for frame conversion, audio samples, command packets, and temporary rasterization data.

## Treat The Frontend Like A Game

A desktop frontend that paints pixels like a normal form will often stutter. Better patterns:

- render into a persistent texture,
- update the texture from a stable frame buffer,
- present on a steady render loop,
- keep UI controls separate from the video path,
- avoid blocking the emulation thread on paint,
- avoid allocating a new bitmap every frame,
- report renderer status in diagnostics.

The presentation path should look more like a simple game engine than a document viewer.

## Frame Pacing Matters

"Fast enough" is not the same as smooth. Track:

- posts from emulation thread,
- render ticks,
- dropped or skipped frames,
- wait/yield counts,
- frame age,
- audio underruns.

Use a real-speed mode by default. Also provide uncapped and diagnostic modes so developers can profile without presentation pacing.

## UI Design For Emulator Testing

A useful emulator UI should expose common workflows:

- File menu for firmware and media.
- Emulation menu for run, pause, reset, speed, frame advance.
- View menu for scaling, aspect, and diagnostics.
- Input settings.
- Recent media.
- Status bar with target, speed, renderer, and major subsystem state.

Do not hide diagnostics behind a debugger. Compatibility work benefits from live state.

## Benchmark Outside The UI

Always keep a headless benchmark:

- no video presentation,
- optional GPU command execution,
- optional audio generation,
- fixed step count,
- repeatable title or synthetic workload,
- stable output summary.

This distinguishes core performance from frontend performance.

