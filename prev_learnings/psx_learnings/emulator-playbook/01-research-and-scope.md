# Research And Scope

## Start With Primary Sources

For any console, collect references before writing code:

- Hardware register maps, memory maps, and timing docs.
- CPU manuals and errata.
- Existing mature emulator source code.
- Existing emulator documentation and compatibility notes.
- Homebrew SDK docs and small test programs.
- Known public test ROMs or legal homebrew tests.
- Community writeups for obscure hardware behavior.

Use mature emulators as behavioral references, not as code to copy. The most useful pattern is to ask: "What does this emulator consider important enough to instrument, test, or special-case?"

## Scope By Boot Milestones

Avoid planning by subsystem completion alone. Subsystems are never really "done" in an emulator. Plan by observable software milestones:

1. Reset vector executes.
2. CPU can run a tiny synthetic program.
3. Memory map and basic I/O do not crash the BIOS or monitor ROM.
4. Homebrew executable boots without firmware.
5. Homebrew can draw a pixel or play a sound.
6. Firmware boots and reaches a menu.
7. Firmware can boot a disc or cartridge.
8. First retail title reaches visible output.
9. First retail title accepts input.
10. First retail title reaches gameplay.
11. Compatibility sweep exposes repeated failure classes.
12. Performance reaches real-time with frame pacing.

These milestones keep the project grounded in evidence.

## Choose The First Test Programs Carefully

Use a ladder:

- Tiny synthetic CPU tests.
- Generated homebrew that uses one hardware feature at a time.
- Known homebrew demos with simple rendering.
- Firmware menu.
- One retail title with simple requirements.
- One demanding retail title that stresses the console's signature hardware.
- A broad compatibility set.

Do not jump straight from CPU tests to a complicated retail game and assume the first failure identifies the real blocker.

## Define Compatibility Levels

Use labels that describe observable behavior:

- `No boot`: does not leave firmware or loader.
- `Boots`: executable starts and runs CPU code.
- `Audio/video init`: hardware is configured, but no useful display.
- `Visible`: user-recognizable frame appears.
- `Interactive`: input affects the program.
- `Gameplay`: reaches controllable gameplay or equivalent workload.
- `Playable`: sustained use without obvious blocking bugs.
- `A+ target`: game-specific polish, timing, audio, graphics, save, input, and frontend behavior are all strong.

The label should always be backed by artifacts: logs, frame dumps, counters, or screenshots.

## Keep The Plan Honest

Early plans should identify:

- What is low-level and must be implemented eventually.
- What is a safe stub.
- What is a dangerous stub.
- What can be HLE temporarily.
- What should never be faked because software relies on side effects.

The plan should change when compatibility evidence changes. In `psxSharp`, black screens were sometimes GPU issues, sometimes BIOS handoff issues, sometimes CD behavior, sometimes just long loading paths. The process only worked once diagnostics could distinguish those cases.

