# Tooling

Good emulator tooling turns vague compatibility into searchable evidence. Build the tools as part of the project, keep outputs structured, and make long-running jobs safe to resume.

## Command-Line Runner

A headless runner should exist before the GUI. Minimum commands:

- run one ROM/media item,
- run compatibility phases over a directory,
- summarize compatibility CSVs,
- dump a frame,
- verify a frame against a baseline,
- probe save backends,
- run test ROMs with success/failure PCs.

Useful common options:

- max steps,
- max seconds,
- stop frame,
- BIOS/firmware path,
- save file,
- save read-only mode,
- input keys,
- frame input script,
- stop on invalid PC,
- stop on requested PC and hit count,
- trace tail,
- trace PC ranges,
- trace frame range,
- watch read/write addresses and ranges,
- watch limit,
- memory dumps,
- print state,
- trace interrupts/DMA/system calls,
- debug video layer.

## Compatibility CSV

Prefer appendable, plain CSV reports. Include enough fields to analyze without rerunning:

- index/path/title/media id,
- phase,
- status and classification,
- frames/steps/cycles,
- frame change metrics,
- first/last frame hash,
- CPU state,
- key video/interrupt state,
- save type,
- error message,
- capture path.

Keep full stack traces optional. They are useful in focused reruns but make huge library sweeps noisy.

## Batch Runners

PowerShell or shell scripts are fine for orchestration. They should:

- split work into chunks,
- support resume,
- set low process priority,
- enforce wall-clock timeout,
- kill child process trees on timeout,
- merge chunk CSVs,
- retry timeouts separately,
- fold best retry rows into a best-results CSV,
- produce summary CSVs and markdown summaries.

Use small chunks on developer machines. A full emulator sweep can stress CPU, memory, disk, and thermal limits.

## Analyzer Scripts

After a broad sweep, run analyzers that create:

- failures-only CSV,
- grouped errors,
- grouped statuses,
- failures by index block,
- failures by media path class,
- failures by save type,
- failures by phase,
- per-ROM best/worst summaries,
- markdown summary.

Triage scripts should mark likely archive noise separately from high-signal retail failures.

## Visual Tools

Build:

- binary framebuffer dumps,
- exact framebuffer compare,
- diff image generation,
- write-baseline mode,
- debug layer capture,
- manifest-driven visual snapshot runner.

Store baselines in a stable folder. For changed baselines, require a deliberate update command so accidental renderer regressions do not silently rewrite expectations.

## Save Tools

Build:

- save backend probe,
- size-correct fixture generator,
- save roundtrip runner,
- save byte statistics,
- save hash recording,
- read-only save verification.

For save stats, record:

- size,
- bytes changed from erased/default,
- zero bytes,
- erased bytes,
- unique byte count,
- hash.

## Input Tooling

Use text input scripts. Features worth having:

- frame ranges,
- key combinations,
- comments,
- named scripts checked into source,
- shared parser for run, visual, and save tools.

For deep gameplay scripts, keep scripts domain-specific but the runner generic. It is okay to have a `pokemon-ruby-post-truck-dialogue.input` equivalent in a future project; it is not okay for the emulator core to know about that script.

## Safety Features

Long compatibility sweeps can make a workstation miserable if unmanaged. Add:

- process timeout,
- process-tree kill,
- low priority,
- chunk resume,
- output existence checks,
- capture filters,
- no unbounded full tracing in archive sweeps,
- clear distinction between emulator exit code and runner failure.

Emulator crash should be data. Runner crash should be exceptional.

## Documentation As Tooling

Keep status docs close to the reports:

- current compatibility grade,
- known failing anchors,
- fixed bug list,
- remaining risks,
- commands used for important reruns,
- report paths.

This helps future work continue from evidence rather than rediscovery.
