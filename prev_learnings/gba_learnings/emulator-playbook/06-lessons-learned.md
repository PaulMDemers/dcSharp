# Lessons Learned

These are the broad lessons from `gbaSharp` that should transfer to another console emulator.

## Accuracy Bugs Hide Behind Plausible Progress

Many games will boot with incomplete hardware behavior. That is good motivation, but it can create false confidence. A game reaching its title screen does not prove:

- interrupt timing,
- DMA ordering,
- save protocols,
- audio timing,
- peripheral behavior,
- edge-case CPU decoding,
- or long gameplay stability.

Treat boot as the first compatibility tier, not the finish line.

## Broad Sweeps Need Triage

Full-library numbers are useful only after classification. Archive collections include duplicates, hacks, bad dumps, demos, virtual-console injects, videos, and special carts. Separate "the emulator failed retail software" from "the archive contains strange inputs."

The useful result of a full sweep is not just a pass percentage. It is a ranked list of bug clusters.

## Build Tools Before You Need Them

The most valuable tools were:

- compatibility phases with CSV output,
- frame-scaled step budgets,
- trace tail,
- trace ranges,
- watchpoints,
- memory dumps,
- DMA/IRQ tracing,
- save probes,
- visual baselines,
- chunked/resumable sweep runners,
- failure analyzers.

Each tool paid for itself by turning a confusing game failure into a concrete subsystem bug.

## Avoid Loose Detection

Emulators often need to infer hardware from ROM/media content. Loose string matching or broad heuristics can silently route software to the wrong device.

Prefer:

- concrete markers,
- known headers,
- database overrides,
- conservative defaults,
- explicit user override paths,
- tests for ambiguous media.

## Hardware Mirrors And Wrapping Matter

Managed languages are good at catching out-of-range array access. Real hardware often wraps, mirrors, ignores, or returns open bus. A managed bounds exception in emulator code may indicate that the hardware model is too strict, not that the game did something impossible.

When retail software indexes past a region, check the hardware mirror/wrap rule.

## Generated Code Is Normal

If a console allows executable RAM, games may run generated or copied code. Do not overfit the CPU decoder to "normal-looking" ROM code. Trace who wrote RAM code and validate the actual hardware decode behavior.

## No-Firmware Paths Are Compatibility Traps

Clean-room boot and firmware HLE are convenient, but games can depend on details:

- initial registers,
- stack values,
- interrupt trampoline layout,
- firmware mirror flags,
- system-call side effects,
- return register clobbering,
- timing.

If one game works with real firmware and fails without it, the no-firmware path is a prime suspect.

## Input Probes Are Not Gameplay

Generic input catches many crashes, but it also misses game-specific paths and can create odd paths no player would take. Build both:

- generic probes for broad crash detection,
- scripted inputs for meaningful milestones.

Manual play and reference comparison still matter.

## Save Support Has Two Levels

Backend support means the protocol can store and retrieve data. Gameplay save support means a game can reach its save flow, write meaningful data, reload it, and continue correctly.

Keep those levels separate in reports.

## Fixes Should Leave Artifacts

Every meaningful fix should leave at least one of:

- a unit regression test,
- a focused compat rerun CSV,
- a visual baseline,
- a save roundtrip row,
- a status doc note with command and failure signature.

Without artifacts, the same bug class will be rediscovered later.

## Resist Game-Specific Core Hacks

Sometimes a targeted compatibility guard is practical, especially while building toward a larger subsystem. But every guard should be:

- documented,
- narrow,
- easy to remove,
- tied to a known missing subsystem,
- covered by a higher-level test.

The healthier long-term path is to model the hardware contract that explains the game behavior.

## Keep Reports Human-Readable

CSV is for grouping. Markdown is for decisions. Keep both.

Good status docs answer:

- what works,
- what fails,
- how confident we are,
- what changed recently,
- what bug cluster is next,
- what command reproduces it.

## The Best Next Task Is Usually The Smallest Concrete Failure

When compatibility is broad but imperfect, avoid vague goals like "improve timing." Pick an anchor:

- one ROM,
- one phase,
- one frame,
- one bad PC,
- one wrong pixel region,
- one save byte mismatch.

Reduce that. The general subsystem improvement will emerge from specific failures.
