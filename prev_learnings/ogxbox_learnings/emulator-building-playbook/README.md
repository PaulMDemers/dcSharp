# Emulator Building Playbook

This playbook captures the reusable lessons from building `ogxbSharp`. It is
not specific to the original Xbox. Use it as a starting point for a new emulator
project targeting a different console, handheld, arcade board, or computer.

The central lesson is simple: build the emulator and the investigation tooling
together. Compatibility does not come from a single grand implementation pass.
It comes from a tight loop of running software, stopping at meaningful
milestones, extracting evidence, implementing the smallest trustworthy behavior,
and re-running the same tests.

## Documents

- [01. Project Strategy](./01-project-strategy.md)
- [02. Research And Clean-Room Notes](./02-research-and-clean-room.md)
- [03. Observability And Test Harnesses](./03-observability-and-test-harnesses.md)
- [04. Compatibility Workflow](./04-compatibility-workflow.md)
- [05. HLE And Fast-Path Techniques](./05-hle-and-fast-path-techniques.md)
- [06. CPU, Memory, And Device Bring-Up](./06-cpu-memory-and-devices.md)
- [07. Graphics Bring-Up](./07-graphics-bring-up.md)
- [08. Safety, Performance, And Host Health](./08-safety-performance-host-health.md)
- [09. Handoff Checklist](./09-handoff-checklist.md)

## Working Principles

1. Make progress measurable before making it broad.
2. Prefer small deterministic milestones over long open-ended runs.
3. Treat every crash, hang, blank frame, and counter change as evidence.
4. Keep compatibility data in artifacts, not memory or vibes.
5. Add abstractions only after repeated patterns prove they exist.
6. Never let a guest workload endanger the host machine.

## Minimal First Milestone

For a new emulator, the first milestone should not be "boot a retail game." A
better first milestone is:

1. Parse a small executable or ROM image.
2. Map memory deterministically.
3. Execute or interpret enough instructions to hit a known stop point.
4. Log CPU state, memory faults, device reads/writes, and a progress summary.
5. Run the same input again and get the same result.

Once that loop is reliable, expand to homebrew, test ROMs, demos, and finally
retail software.
