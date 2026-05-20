# Project Checklists

## New Emulator Setup

- Create a headless core project.
- Create a CLI project.
- Create a test project.
- Add a minimal desktop or display frontend only after the core can produce frames.
- Define legal asset policy.
- Collect primary references.
- Define the first generated test program.
- Define the first retail or real-software target.
- Set artifact folders for screenshots, logs, and sweeps.

## Core Bring-Up Checklist

- Reset vector runs.
- Memory map returns stable reads.
- CPU can execute simple tests.
- Exceptions and interrupts are observable.
- Firmware or monitor ROM can run without immediate crash.
- Direct executable loader works.
- Device register stubs are safe and traced.
- Basic timing scheduler exists.
- CLI can run fixed steps and dump state.

## First Video Checklist

- Video RAM or framebuffer model exists.
- Display mode and origin are tracked.
- First clear command works.
- First pixel write works.
- First rectangle works.
- First image upload works.
- First DMA graphics transfer works.
- Frame dump works from CLI.
- Frontend presents the same frame as CLI dump.

## First Retail Boot Checklist

- Firmware boot path works.
- Media parser reads boot metadata.
- Boot executable is copied exactly.
- Handoff state is logged.
- Instruction cache or decoded blocks are invalidated after code load.
- Media command FIFO and response FIFO are implemented enough for boot.
- DMA channels used by boot are implemented.
- Interrupt masks and status are visible.
- CLI can stop on boot executable entry.

## Black Screen Checklist

- Is the program still in firmware?
- Is PC advancing?
- Is there a CPU exception?
- Are interrupts pending but masked?
- Is input being polled?
- Is media being read?
- Is data being consumed?
- Is DMA active?
- Are video commands submitted?
- Are commands only clears/setup?
- Is real geometry submitted?
- Are pixels written outside the active display?
- Is the frontend presenting stale or blank frames?
- Does a longer fast scan eventually reach visible output?

## Performance Checklist

- Headless benchmark exists.
- Emulated cycles per second are measured.
- Realtime percentage is measured.
- One-core utilization is measured.
- UI FPS is measured separately.
- Frame posts, ticks, drops, and renderer type are visible.
- Allocation rate and GC counts are visible.
- Hot-path tracing can be disabled.
- Frame buffers and audio buffers are reused.
- Uncapped and real-speed modes exist.

## Compatibility Pass Checklist

- Pick one target and one hypothesis.
- Run a baseline capture.
- Add a targeted stop condition if needed.
- Make the smallest plausible fix.
- Add a unit or regression test.
- Re-run the target.
- Run adjacent titles.
- Save artifacts.
- Update compatibility notes.

## Milestone Exit Criteria

A milestone is ready when:

- The target behavior is observable.
- The commands and artifacts are saved.
- Focused tests pass.
- Broad enough regression tests pass.
- Known remaining risks are written down.
- The next blocker is named.

