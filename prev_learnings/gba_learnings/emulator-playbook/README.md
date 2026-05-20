# Emulator Development Playbook

This playbook captures reusable lessons from building `gbaSharp`. It is written for a future emulator project, possibly for a different console, so the advice focuses on method, tooling, risk management, and debugging habits rather than Game Boy Advance specifics.

The main lesson: emulator development is not a straight line from CPU to graphics to audio. It is a loop of building small faithful subsystems, creating tools that expose failure, running representative software, and reducing each incompatibility to a concrete hardware contract.

## Files

| File | Purpose |
| --- | --- |
| [01-architecture.md](01-architecture.md) | How to structure an emulator core so it stays testable and debuggable. |
| [02-implementation-sequence.md](02-implementation-sequence.md) | A practical build order from first opcode to commercial software. |
| [03-testing-and-compatibility.md](03-testing-and-compatibility.md) | Test ROMs, retail probes, compatibility sweeps, save checks, and visual baselines. |
| [04-debugging-techniques.md](04-debugging-techniques.md) | Traces, watchpoints, crash triage, generated code, timing bugs, and comparison work. |
| [05-tooling.md](05-tooling.md) | CLI tools, batch runners, scripts, reports, and safety features worth building early. |
| [06-lessons-learned.md](06-lessons-learned.md) | Hard-won habits, traps, and design principles that transferred out of `gbaSharp`. |

## How To Use This

Start with architecture and implementation sequence before writing large amounts of code. The fastest path is not the shortest code path; it is the path where every new subsystem can be tested, inspected, and compared.

Once commercial games or complex programs start booting, switch from "implement more" to "measure, classify, and reduce." Broad compatibility work should produce structured data, not just anecdotes. Every crash should become one of:

- a missing hardware behavior,
- an incorrect CPU or memory rule,
- a timing or interrupt ordering bug,
- a save/peripheral protocol issue,
- a renderer/audio approximation,
- or bad input/script/archive noise.

Keep the loop tight: observe, hypothesize, add instrumentation, reproduce, patch, regression-test, document.
