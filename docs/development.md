# dcSharp Development Runbook

This project is currently optimized around legal KallistiOS fixtures, deterministic CLI runs, and small emulator slices that can be regression-tested.

## Toolchain

KallistiOS is installed in WSL:

```bash
~/kos
~/kos-ports
~/sh-elf
```

Verify the SDK:

```bash
wsl -e bash tools/kos/verify-kos.sh
```

## Build Fixtures

Build every sample referenced by `fixtures/kos.json`:

```bash
wsl -e bash tools/kos/build-fixtures.sh
```

Build individual KOS samples:

```bash
wsl -e bash tools/kos/build-sample.sh samples/kos/minimal
wsl -e bash tools/kos/build-sample.sh samples/kos/hello
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_read
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_toc
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_status
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_sector_mode
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_no_media
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_status_no_media
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_toc_no_media
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_out_of_range
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_raw_multisector
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_multitrack_toc_read
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_gdi_2352_toc_read
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_file
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_missing_file
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_dir
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_nested
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_multisector
wsl -e bash tools/kos/build-sample.sh samples/kos/gdrom_seek
wsl -e bash tools/kos/build-sample.sh samples/kos/timer
wsl -e bash tools/kos/build-sample.sh samples/kos/timer_callback
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_controller
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_controller_script
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_controller_b
wsl -e bash tools/kos/build-sample.sh samples/kos/maple_irqb_accept
wsl -e bash tools/kos/build-sample.sh samples/kos/framebuffer
wsl -e bash tools/kos/build-sample.sh samples/kos/video_mode
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_registers
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_polygon
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_polygon_green
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_real_polygon
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_real_modes
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_strip_continuation
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_mixed_strip_sprite
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_sprite
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_sprite_skewed
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_sprite_skewed_texture_rgb565
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_sprite_texture_rgb565
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_sprite_texture_twiddled_rgb565
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_sprite_texture_alpha_argb4444
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_sprite_texture_shading
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_culling
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_depth
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_blend
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_list_types
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_texture_alpha_argb4444
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_texture_argb1555
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_texture_argb4444
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_texture_rgb565
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_texture_twiddled_rgb565
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_texture_uv_modes
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_texture_shading
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_texture_filter
wsl -e bash tools/kos/build-sample.sh samples/kos/pvr_texture_size
wsl -e bash tools/kos/build-sample.sh samples/kos/asic_irqb
wsl -e bash tools/kos/build-sample.sh samples/kos/asic_events
wsl -e bash tools/kos/build-sample.sh samples/kos/aica_registers
wsl -e bash tools/kos/build-sample.sh samples/kos/aica_playback_position
wsl -e bash tools/kos/build-sample.sh samples/kos/aica_playback_loop
wsl -e bash tools/kos/build-sample.sh samples/kos/aica_playback_pcm8
wsl -e bash tools/kos/build-sample.sh samples/kos/aica_stereo_pan
wsl -e bash tools/kos/build-sample.sh samples/kos/aica_adpcm_metadata
```

Generated ELF files are copied to `artifacts/kos/`, which is intentionally ignored by git. `tools/kos/build-fixtures.sh` also generates legal local GD-ROM fixture media under `artifacts/media/`; to refresh only the media, run `wsl -e bash tools/kos/build-fixture-media.sh`.

## Run Fixtures

Text summary:

```bash
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_probe.elf --instructions 50000000 --trace-tail 40
```

JSON summary:

```bash
dotnet run --project src/DcSharp.Cli -- run artifacts/kos/dcsharp_timer.elf --instructions 50000000 --trace-tail 40 --json
```

Manifest regression run:

```bash
dotnet run --project src/DcSharp.Cli -- fixtures fixtures/kos.json --validate-only
dotnet run --project src/DcSharp.Cli -- fixtures fixtures/kos.json
dotnet run --project src/DcSharp.Cli -- fixtures fixtures/kos.json --filter input_idle
dotnet run --project src/DcSharp.Cli -- fixtures fixtures/kos.json --report-json artifacts/reports/kos-fixtures.json
dotnet run --project src/DcSharp.Cli -- media inspect path/to/disc.cue
dotnet run --project src/DcSharp.Cli -- media extract-boot path/to/disc.cue --out artifacts/retail/1ST_READ.BIN
dotnet run --project src/DcSharp.Cli -- media analyze-boot artifacts/retail/1ST_READ.BIN --out-descrambled artifacts/retail/1ST_READ.descrambled.BIN
dotnet run --project src/DcSharp.Cli -- media boot-smoke path/to/disc.cue --instructions 2000 --trace-tail 16 --stop-on-unmapped
```

Useful run options:

- `--instructions <count>` sets the execution budget.
- `--trace-tail <count>` controls how many final SH-4 steps are retained.
- `--vblank-interval <instructions>` controls the current synthetic VBlank cadence.
- `--vblank-interval 0` disables synthetic VBlank.
- `--controller a0:start,a,joyx=-12,joyy=13,ltrig=40,rtrig=80` maps a static controller state to a Maple address.
- `--controller-script "a0:0:none;200000:start,a"` maps an instruction-indexed controller script to a Maple address.
- `--controller-a start,a,joyx=-12,joyy=13,ltrig=40,rtrig=80` and `--controller-b b,ltrig=7` are compatibility shorthands for A0 and B0.
- `--controller-a-script "0:none;200000:start,a"` is a compatibility shorthand for A0 scripts.
- `--dump-framebuffer artifacts/video/framebuffer.png --framebuffer-size 320x240` writes the current RGB565 VRAM snapshot as a PNG.
- `--pixel-format rgb565` is accepted explicitly; RGB565 is currently the only framebuffer dump format.
- `--audio-wav artifacts/audio/probe.wav` writes a deterministic stereo PCM16 WAV from modeled AICA PCM16/PCM8 playback diagnostics plus Yamaha-style 4-bit ADPCM channel decode.
- `--media path` accepts raw 2048-byte sector images, 2352-byte CD-sector dumps, simple CUE sheets, and GDI descriptors with local data tracks.
- `media inspect path` loads raw/CUE/GDI media, reports synthesized GD-ROM geometry, lists CUE tracks when present, scans early candidate sectors for Dreamcast IP.BIN-style boot metadata, and prints fields such as area, product, version, boot file, software maker, and title. For CUE inputs it also scans adjacent `.bin` files in the same directory and reports boot-sector candidates, which helps diagnose dumps whose CUE points at a merged or incomplete file while numbered track files are also present. Use `--scan-sectors <count>` to widen or narrow the boot-sector scan and `--json` for structured output.
- `media extract-boot path --out output.bin` inspects the media, finds the IP.BIN boot filename such as `1ST_READ.BIN`, opens the ISO9660 filesystem from the selected media or an adjacent CUE boot candidate, resolves split-track boot extents into later adjacent track files when needed, and writes the boot file to disk. Use `--json` for scriptable metadata including source path, ISO extent, title, volume identifier, and byte count.
- `media analyze-boot path` accepts a CUE/GDI descriptor or an already extracted boot binary, compares the original bytes with the KallistiOS/Marcus Comstedt descrambled layout, reports ELF/startup-stub/opcode-sample heuristics, assumes the BIOS boot load address `0x8C010000`, and can write the descrambled candidate with `--out-descrambled`.
- `media boot-smoke path` accepts a CUE/GDI descriptor or an already extracted boot binary, selects the analyzed original/descrambled layout with `--layout auto|original|descrambled`, maps it at the BIOS boot address `0x8C010000`, seeds the full 16-sector IP.BIN at `0x8C008000` when media metadata is available, enters the IP.BIN executable bootstrap when present, and runs the normal emulator loop with the same run options as `dcsharp run`. For CUE directory fallback candidates, IP.BIN is rebuilt from each sector's 2048-byte payload instead of copied as contiguous raw 2352-byte CD data. Media IP.BIN entry also seeds a BIOS-like initial SR (`0x400000F0`) and an initial VBlank event; override with `--initial-sr` or tune event cadence with `--vblank-interval` when probing boot waits. It also reports the final general-purpose registers, dumps labeled top stack words for the current BIOS soft-reset checkpoint, reports writes into key boot regions, and can stop at the first selected diagnostic boundary with `--stop-on-unmapped` or `--stop-on-device-domain <domain>`.
- Use `--memory-write-log <path>` / `--memory-read-log <path>` with optional `--memory-write-address <start-end>` / `--memory-read-address <start-end>` and `--memory-write-limit <count>` / `--memory-read-limit <count>` to capture filtered guest memory accesses after program load and firmware stub installation. Runtime reads and writes include the current instruction PC when available. This is useful for retail checkpoints where the device log is quiet but a stack/work-area field should change. For example, watching `0x7E000FDC-0x7E000FDF` on the IP.BIN path captures only the initial `completion-flag` zero write. Watching DOA2's retail idle state word `0x8C30CA2C-0x8C30CA2F` shows it is initialized at `0x8C113312/33C`, then set to `5` at `0x8C12EB10` and `0x8C12EB1E`, read as `5` at `0x8C12EC62` and `0x8C12ED22`, and never written to the expected `6` before the current wait.
- `--trace-log artifacts/logs/trace.txt --trace-pc 0x8C010000-0x8C010100 --trace-log-limit 4096` writes a bounded filtered SH-4 trace. Trace capture preserves every instruction that matches the requested PC filter, while CPU fast-forwards can still collapse uncaptured loops outside the filter.
- `--device-log artifacts/logs/devices.txt --device-domain pvr --device-kind Write` writes filtered device accesses.
- `--json` or `--summary-json` emits structured output for scripts and regression checks.
- `fixtures --validate-only` parses and validates a fixture manifest without requiring built ELF artifacts.
- `fixtures --filter <name>` runs or validates only fixtures whose names contain the filter text.
- `fixtures --report-json artifacts/reports/kos-fixtures.json` writes a structured fixture report while keeping the text summary on stdout.
- `tools/probe-retail.ps1` reruns the local ignored retail-disc boot-smoke probes when `retail_discs/` is populated. It checks that DOA2, Rayman, and Legacy advance through IP.BIN framebuffer setup to the current system BIOS soft-reset checkpoint without unmapped stops. Legacy also verifies the former `0x330441F0` corrupted-IP.BIN pointer/table blocker stays cleared under a longer local retail budget.

The fixture manifest keeps sample paths, artifact names, instruction budgets, optional `mediaPath`, static `controllers`, instruction-indexed `controllerScripts`, and expected serial/video/audio checks together. Fixture text and JSON reports include Maple transfer counts, grouped PVR TA list summaries, assembled PVR TA strip summaries across opaque, punch-through, and translucent lists, GD-ROM status/sector-mode/TOC/read summaries, plus scheduler VBlank, hardware tick, hardware batch, max batch, idle advance, idle wake, CPU fast-forward, and controller-script change diagnostics for timing comparisons. Manifests can also set optional Maple thresholds such as `minMapleTransfers`, `minMapleDeviceInfoTransfers`, `minMapleGetConditionTransfers`, `minMapleDmaBatches`, and `requireNoMapleDescriptorLimitHits`, GD-ROM thresholds and filters with `minGdromStatusCommands`, `minGdromSectorModeCommands`, `minGdromTocCommands`, `minGdromReadCommands`, `minGdromBytesRead`, `gdromStatuses`, `gdromSectorModes`, `gdromTocs`, and `gdromReads`, scheduler thresholds such as `minVblankEvents`, `minHardwareAdvanceTicks`, `minHardwareAdvanceBatches`, `maxHardwareAdvanceBatch`, `minIdleAdvanceTicks`, `minIdleAdvanceBatches`, `maxIdleAdvanceBatch`, `minIdleTimerWakes`, `minIdleVBlankWakes`, `minIdleInputWakes`, `minCpuFastForwardInstructions`, `minCpuFastForwardBatches`, `maxCpuFastForwardBatch`, and `minControllerScriptChanges`, CPU register expectations with `cpu`, device-domain thresholds with `minDeviceAccessDomains`, ASIC expectations with `requireNoAsicPendingInterrupt`, `asicPendingInterrupt`, and `asicEventRegisters`, timer expectations with `requireNoTimerPendingInterrupt`, `timerPendingInterrupt`, and `timerChannels`, current PVR register values with `pvrRegisters`, current AICA register values with `aicaRegisters`, and decoded AICA channel state/playback counters with `aicaChannels`. Use `--artifacts <path>` with the `fixtures` command when testing a different artifact directory.

`fixtures/kos.json` declares the local `fixtures/kos.schema.json` schema for editor validation and autocomplete. Keep the schema in sync whenever a new manifest field becomes part of the supported fixture contract.

KOS fixtures are usually unstripped. When `.symtab` or `.dynsym` is present, text and JSON summaries include nearest function names for stop PCs and trace-tail entries.

Generated framebuffer, trace, and device logs belong under `artifacts/` and stay out of git.

Run summaries also include CPU diagnostics for `GBR`, `VBR`, `SPC`, `SSR`, `FPSCR`, `TRA`, `EXPEVT`, and `INTEVT`, plus TMU channel diagnostics for current counter/constant/control/priority/pending state, plus scheduler diagnostics for synthetic VBlank events, hardware advancement ticks, coalesced hardware batches, max hardware batch size, idle-advance ticks/batches/wake reasons, CPU fast-forwarded instructions/batches, and controller-script state changes. The runner currently uses this batching after SH-4 `sleep` instructions, side-effect-free self-branch waits, and narrow taken backward `bt`/`bf` waits with read-only polling bodies to advance hardware directly to the next timer, enabled VBlank, or controller-script boundary. It also fast-forwards a narrow masked `dt`/`bf/s` counted-delay loop shape, including simple `nop` and `add #imm,rn` delay slots, plus known side-effect-free IP.BIN fill/copy/wait loops when those exact loop instructions are outside the active trace filter.

Current local retail probes for DOA2, Rayman 2, and Legacy of Kain pass the shared IP.BIN splash/reset handoff into the loaded boot binaries. BIOS system function `0` is modeled as a soft-reset handoff to `0x8C010000` with a high-RAM stack, so the probes now run until their current self-branch idle loops inside retail code: DOA2 at `0x8C12ED28/2A`, Rayman 2 at `0x8C0DEC68/6A`, and Legacy of Kain at `0x8C0388C8/CA`. No GD-ROM read commands are issued yet; the next compatibility blocker is the retail boot code's PVR/interrupt-style wait after substantial system-RAM and PVR setup. DOA2 now has a concrete state-machine breadcrumb: `0x8C30CA2C` remains `5`; `0x8C12EC62` and `0x8C12ED22` expect it to become `6`. Earlier focused traces showed the IP.BIN wait's `[R15+0x0C]` `completion-flag` only shortens the splash delay, because both the flag-set and timeout exits fall through to the BIOS system reset vector.

Structured run summaries include aggregate device-access counts by domain and access kind, plus recent device accesses. Device domains currently include `pvr`, `aica`, `maple`, `asic`, `holly`, `scif`, `tmu`, `sh4`, `unmapped`, and `other`.

GD-ROM summaries include whether media is loaded, media sector size/count, leadout FAD, loaded track mappings, aggregate read command counts, successful/failed counts, total bytes read, and recent firmware read requests with parameter address, LBA, sector count, destination, bytes requested/read, success, and status text, plus recent TOC requests with buffer address, first/last track, data-track FAD, leadout FAD, success, and status text. The firmware syscall HLE also tracks queued command ids, reports completed reads with transferred byte counts, records `CMD_GETTOC2` diagnostics, and maps no-media read failures into the status word KOS uses for `ERR_NO_DISC`. These diagnostics are shown in CLI fixture output and the desktop GD-ROM diagnostics tab.

ASIC summaries include current event ACK registers, IRQ9/IRQB/IRQD masks, per-level pending masks, and the currently deliverable ASIC interrupt event/level/source bit. Unit tests cover A/B/C event-bank source decoding, independent ACK clearing, TMU/ASIC interrupt arbitration, SH-4 `trapa` and illegal-instruction event-register entry, slot-illegal PC/SR-changing delay-slot exceptions, and SH-4 external interrupt acceptance against `SR.BL`, `SR.IMASK`, branch delay slots, and `rte` delay slots.

PVR summaries include current named register values plus recent register accesses. PVR TA writes are classified into diagnostic command kinds such as `PolygonHeader`, `Vertex`, `VertexEndOfStrip`, `SpriteHeader`, `ModifierVolume`, `UserClip`, and `YuvConverterData`, then grouped into TA list summaries by region/list type with header, vertex, and end-of-strip counts. Recent TA writes also include parameter-header diagnostics from `DreamcastPvrTaParameterDecoder`, including parameter type, list type, end-of-strip, whether a real payload length is known, and decoded polygon command fields such as color format, texture enable, gouraud, clipping, and strip length. The derived TA stream view replays writes through known payload lengths and labels each recent write as `Control` or `Payload`, including named polygon payload slots `Mode1`, `Mode2`, `Mode3`, and `Parameter0` through `Parameter3`, sprite header slots `Mode1`, `Mode2`, `Mode3`, `Argb`, `OffsetArgb`, `Dummy0`, and `Dummy1`, sprite geometry slots `Ax` through `Dy` plus untextured dummy or textured UV tail slots, modifier-volume slots `Mode1` and `Dummy0` through `Dummy5`, plus diagnostic user-clip slots `Clip0` through `Clip6`; this is useful for spotting fixture-only shortcut packets that would be payload words in a real header stream. Real-shaped polygon header payloads are also aggregated into decoded `mode1`, `mode2`, and `mode3` summaries with depth/culling, blend/fog/alpha/texture-size, and texture-base/pixel-format/VQ/mipmap fields. Real-shaped vertex payloads after complete opaque, punch-through, or translucent polygon headers are decoded into float X/Y/Z/U/V, rounded preview X/Y, ARGB8888, derived RGB565, offset-color, and end-of-strip summaries while still ignoring older fixture-only shortcut packets. The current renderable-list strip assembler records the header value, optional decoded real header payload mode fields, RGB565 color, and either a diagnostic fixture-only vertex packet made of a `Vertex` or `VertexEndOfStrip` control word followed by signed 16.16 X, signed 16.16 Y, and RGB565 color payload words, or a real-shaped `pvr_vertex_t` packet with float X/Y/Z/U/V and ARGB8888 color payload words. The sprite path records KOS-shaped sprite headers with face ARGB plus a following 16-word sprite geometry packet, emits `pvrTaSprites` summaries, sorts the four preview points into a cyclic quad, and renders it as two tiny triangles instead of filling the whole bounds. The fixture preview renderer currently applies immediate TA write order across strips and sprites, continuation triangle rasterization for multi-vertex strips, real header culling, depth compare/write, alpha blend, punch-through alpha discard, encoded texture dimensions, nearest/bilinear RGB565/ARGB1555/ARGB4444 texture sampling for non-twiddled and twiddled texture layouts, texture coordinate clamp/repeat/flip mode bits, texture shading replace/modulate/decal/modulate-alpha mode bits, and ARGB1555/ARGB4444 texture alpha as blend source alpha before its tiny solid-color triangle rasterization. `DreamcastPvrTaParameterDecoder` remains the separate skeleton for real TA parameter control decoding; it classifies control fields and known 32-byte header payload lengths. Fixture `pvrTaCommands` entries can assert minimum counts by kind alone or add filters for `region`, `listTypeName`, `endOfStrip`, and exact command `value`; `pvrTaStreamWrites` entries assert recent stream role matches by `role`, `region`, `kind`, `value`, `controlKind`, `controlValue`, `payloadWordIndex`, `payloadWordsRemaining`, `payloadWordName`, and `minCount`; `pvrTaPolygonHeaderPayloads` entries assert aggregated real polygon-header payload matches by raw mode/parameter words and decoded fields such as `depthCompareName`, `cullingName`, `blendSrcName`, `textureShadingName`, `filterModeName`, `textureUSizeName`, `textureVSizeName`, `alphaEnabled`, `textureBase`, `nonTwiddled`, and `pixelFormatName`; `pvrTaRealVertexPayloads` entries assert decoded real vertex payload matches by raw coordinate/color words, rounded X/Y, ARGB8888, derived RGB565, offset-color, end-of-strip, and `minCount`; `pvrTaParameterHeaders` entries assert recent decoded parameter-header matches by `kind`, `region`, `parameterType`, `listTypeName`, `endOfStrip`, `value`, `expectedPayloadWords`, `hasKnownPayloadLength`, decoded polygon command fields such as `textureEnabled`, `colorFormatName`, `clipModeName`, `stripLengthName`, and `autoStripLength`, and `minCount`; `pvrTaLists` entries assert grouped TA list minimums for `minCommands`, `minPolygonHeaders`, `minVertices`, and `minVertexEndOfStrip`; `pvrTaStrips` entries assert assembled strip matches by `region`, `listTypeName`, optional real header mode fields, `rgb565`, `minVertices`, exact ordered `vertices`, and `minCount`; `pvrTaSprites` entries assert assembled sprite matches by `region`, `listTypeName`, `headerValue`, `controlValue`, `argb`, `rgb565`, `minVertices`, exact ordered `vertices`, and `minCount`; `videoSamples` may assert both covered and zero-valued preview pixels. The current polygon and sprite paths are fixture-backed tiny preview rasterizers for known renderable TA sequences; broader PVR rendering remains diagnostic-only.

Textured sprite coverage decodes the packed 16-bit UV tail words from KOS-shaped sprite geometry, carries optional sprite `u`/`v` expectations in fixture manifests, nearest-samples non-twiddled or twiddled RGB565 sprite textures across rectangular and skewed sprite quads, applies ARGB4444 texture alpha source blending, and covers texture shading plus coordinate clamp/repeat/flip modes in the tiny preview path.

AICA summaries include current named register values plus recent register accesses. Channel summaries decode sample format, compressed/streamed metadata, sample stride, loop enable, sample address, loop points, pitch, pan/send nibbles, derived left/right balance, volume, key-on state, playback sample/byte counters, and active channel count while remaining silence-safe. ADPCM formats intentionally report a zero byte stride but advance sample positions and packed byte counters at two 4-bit samples per byte; optional WAV dumps synthesize modeled PCM16, PCM8, and Yamaha-style 4-bit ADPCM channels from those diagnostics.

Maple summaries capture DMA command/response names, destination labels, receive buffers, response sizes, decoded controller state for `GetCondition` responses, and per-DMA descriptor traversal diagnostics including malformed chains that hit the descriptor guard.

## Tests

Run normal tests:

```powershell
dotnet test dcSharp.slnx
```

Run the fast local check, including whitespace diff checks, fixture-manifest validation, and the unit suite:

```powershell
.\tools\check.ps1
```

GitHub CI runs the same fast path on `windows-latest`: restore, build, fixture-manifest validation, and the unit suite. It does not build KallistiOS samples or require generated ELF artifacts.

Run long KOS fixture checks:

```powershell
$env:DCSHARP_RUN_KOS_FIXTURES='1'
dotnet test dcSharp.slnx --filter DreamcastKosFixtureTests
```

Or run the same fast local check plus the full CLI fixture manifest:

```powershell
.\tools\check.ps1 -KosFixtures
```

Use `-FixtureFilter <name>` with `-KosFixtures` to run only matching CLI fixtures after the fast checks:

```powershell
.\tools\check.ps1 -KosFixtures -FixtureFilter input_idle
```

The fixture checks assume the corresponding ELF files already exist under `artifacts/kos/`. The test suite and CLI runner both read `fixtures/kos.json`.

## Current Fixture Expectations

- `dcsharp_minimal.elf`: reaches `main()` and exits through the firmware-exit trap.
- `dcsharp_probe.elf`: reaches default KOS `main()`, prints probe text, shuts down, and reports `ProgramExit`.
- `dcsharp_trap_exception.elf`: bare-metal SH fixture that sets `VBR`, executes `trapa #0x2A`, returns through `rte`, writes a serial marker, stops on an unsupported opcode, and exposes `SPC`, `SSR`, `TRA`, `EXPEVT`, and `INTEVT` diagnostics.
- `dcsharp_illegal_instruction.elf`: bare-metal SH fixture that sets `VBR`, executes the defined undefined opcode `0xFFFD` both outside and inside a delay slot, resumes through an `rte` handler, writes a serial marker, stops on an unsupported opcode, and exposes the final slot-illegal `EXPEVT`.
- `dcsharp_slot_illegal_branch.elf`: bare-metal SH fixture that places a `bra` instruction in a delay slot, resumes through an `rte` handler, writes a serial marker, stops on an unsupported opcode, and exposes slot-illegal `EXPEVT`.
- `dcsharp_gdrom_read.elf`: reads one sector from generated local GDI media through `cdrom_read_sectors()`, observes the `DCSH` sentinel in RAM, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_toc.elf`: reads the TOC from generated local GDI media through `cdrom_read_toc()`, verifies the first/last track, data track FAD, leadout FAD, and `cdrom_locate_data_track()` result, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_status.elf`: calls `cdrom_get_status()` with generated local GDI media loaded, verifies KOS sees standby status and the current XA disc type, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_sector_mode.elf`: calls `cdrom_reinit_ex()` with 2048-byte data-area sector mode against generated local GDI media, verifies KOS sees success, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_no_media.elf`: attempts a raw `cdrom_read_sectors()` call without loaded media, observes the KOS-visible failure, verifies the destination buffer is unchanged, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_status_no_media.elf`: calls `cdrom_get_status()` without loaded media, verifies KOS sees no-disc status and the no-disc disc type, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_toc_no_media.elf`: attempts `cdrom_read_toc()` without loaded media, observes the KOS-visible failure, verifies the TOC buffer is unchanged, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_out_of_range.elf`: attempts a raw `cdrom_read_sectors()` call past the generated media leadout, observes the KOS-visible failure, verifies the destination buffer is unchanged, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_raw_multisector.elf`: reads three contiguous sectors from generated local GDI media with one `cdrom_read_sectors()` call, verifies `BIG.BIN` bytes and zero padding, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_multitrack_toc_read.elf`: reads the TOC from generated two-track local GDI media, discovers the last data track through `cdrom_locate_data_track()`, reads its sentinel sector at FAD 45150, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_gdi_2352_toc_read.elf`: reads the TOC from generated local GDI media whose track 5 uses 2352-byte source sectors plus a file offset, discovers FAD 45200, reads the extracted user-data sentinel, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_file.elf`: opens `/cd/README.TXT` from the generated local ISO9660-in-GDI media, reads the file text through KOS `fs_iso9660`, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_missing_file.elf`: attempts to open a missing `/cd/MISSING.TXT`, observes the expected VFS failure, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_dir.elf`: enumerates `/cd`, observes `readme.txt` and `data` through KOS `fs_iso9660` directory traversal, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_nested.elf`: enumerates `/cd/DATA`, opens `/cd/DATA/SECOND.TXT`, reads the nested file text through KOS `fs_iso9660`, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_multisector.elf`: opens `/cd/DATA/BIG.BIN`, reads a 5000-byte file across three ISO sectors in odd-sized chunks, verifies EOF and byte content, shuts down, and reports `ProgramExit`.
- `dcsharp_gdrom_seek.elf`: seeks within `/cd/DATA/BIG.BIN`, reads across sector boundaries, rereads from later offsets, verifies tail/EOF behavior, shuts down, and reports `ProgramExit`.
- `dcsharp_timer.elf`: wakes from `thd_sleep()`, prints timer ticks, shuts down, and reports `ProgramExit`.
- `dcsharp_timer_callback.elf`: chains the KOS TMU0 primary timer callback, observes three wakeups, shuts down, and reports `ProgramExit`.
- `dcsharp_timer_vblank.elf`: runs the KOS TMU0 primary timer callback while ASIC VBlank IRQ9 is enabled, observes callback progress while the scheduler raises VBlank events, clears the VBlank source, shuts down, and reports `ProgramExit`.
- `dcsharp_maple_controller.elf`: detects `dcSharp Virtual Controller`, reads neutral or scripted input state, shuts down, and reports `ProgramExit`.
- `dcsharp_maple_controller_script.elf`: performs two raw Maple condition reads, observes a neutral first read and scripted second read, shuts down, and reports `ProgramExit`.
- `dcsharp_input_idle.elf`: performs two raw Maple condition reads around repeated SH-4 `sleep` idle points, observes a scripted controller transition, exposes idle input wake diagnostics, shuts down, and reports `ProgramExit`.
- `dcsharp_maple_controller_b.elf`: probes raw B0 Maple device-info and condition responses, covers absent B0 and configured B0 state, shuts down, and reports `ProgramExit`.
- `dcsharp_maple_irqb_accept.elf`: triggers a raw Maple DMA completion with ASIC IRQB enabled, accepts `INTEVT=0x0360` through `VBR+0x600`, ACKs and masks off the source, and reports `UnsupportedInstruction`.
- `dcsharp_framebuffer.elf`: writes a 320x240 RGB565 quadrant pattern into VRAM, exposes non-zero VRAM diagnostics, shuts down, and reports `ProgramExit`.
- `dcsharp_video_mode.elf`: sets 640x480 RGB565 video mode, writes sentinel VRAM pixels, exposes PVR/video diagnostics, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_registers.elf`: writes named PVR framebuffer/TA registers plus TA command/YUV apertures, exposes PVR diagnostics, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_polygon.elf`: writes a minimal opaque polygon-style TA command sequence, exposes TA list/register diagnostics, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_polygon_green.elf`: writes a second opaque polygon-style TA command sequence with a wider green preview triangle, exposes TA strip/list/register diagnostics, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_real_polygon.elf`: writes a real-shaped 32-byte polygon header followed by three `pvr_vertex_t`-style 32-byte vertices, exposes TA stream/list/register diagnostics plus a tiny preview triangle, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_real_modes.elf`: writes a real-shaped 32-byte polygon header with nonzero mode and parameter payload words followed by three `pvr_vertex_t`-style vertices, exposes decoded TA polygon payload diagnostics plus a tiny blue preview triangle, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_strip_continuation.elf`: writes a four-vertex real-shaped strip, exposes continuation triangle diagnostics, leaves preview pixels covered by both strip triangles, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_mixed_strip_sprite.elf`: writes a red real-shaped strip followed by a green sprite over the same preview pixels, exposes mixed primitive TA diagnostics, leaves the later sprite visible, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_sprite.elf`: writes a real-shaped 32-byte sprite header plus one 16-word sprite geometry packet, exposes TA stream/sprite diagnostics plus a tiny green preview rectangle, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_sprite_skewed.elf`: writes a real-shaped 32-byte sprite header plus one skewed 16-word sprite geometry packet, exposes TA stream/sprite diagnostics plus a tiny green diamond preview, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_sprite_skewed_texture_rgb565.elf`: writes a tiny non-twiddled RGB565 texture, emits a skewed textured sprite with packed UV tail words, exposes sprite UV diagnostics, leaves sampled diamond preview pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_sprite_texture_rgb565.elf`: writes a tiny non-twiddled RGB565 texture, emits a textured real-shaped sprite with packed UV tail words, exposes sprite UV diagnostics, leaves sampled texture colors in preview pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_sprite_texture_twiddled_rgb565.elf`: writes a tiny twiddled RGB565 texture, emits the same textured real-shaped sprite, exposes sprite UV/twiddled texture diagnostics, leaves sampled texture colors in preview pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_sprite_texture_alpha_argb4444.elf`: writes a green sprite followed by a half-alpha ARGB4444 textured sprite with `SrcAlpha`/`InverseSrcAlpha`, exposes sprite UV/alpha diagnostics, leaves blended preview pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_sprite_texture_shading.elf`: writes a tiny white RGB565 texture, emits a green textured sprite with modulate texture shading, exposes sprite UV/shading diagnostics, leaves modulated green preview pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_culling.elf`: writes two real-shaped opaque strips with opposite culling modes over the same preview pixels, exposes decoded strip culling state, leaves only the accepted green strip visible, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_depth.elf`: writes three overlapping real-shaped opaque strips with `Greater`, `Less`, and rejected `Greater` depth compares, exposes decoded depth state, leaves the passing red strip visible, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_blend.elf`: writes an opaque green real-shaped strip followed by a half-alpha red strip with `SrcAlpha`/`InverseSrcAlpha`, exposes decoded blend state, leaves blended yellow-orange preview pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_list_types.elf`: writes opaque, punch-through, and translucent real-shaped strips over the same preview pixels, exposes list-aware strip diagnostics, discards transparent punch-through pixels, blends translucent pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_texture_alpha_argb4444.elf`: writes an opaque green real-shaped strip followed by a half-alpha ARGB4444 textured strip with `SrcAlpha`/`InverseSrcAlpha`, exposes decoded texture blend state, leaves texture-alpha blended preview pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_texture_argb1555.elf`: writes a tiny non-twiddled ARGB1555 texture into VRAM, emits a textured real-shaped strip with UV corner coordinates, exposes decoded texture state, leaves RGB565-converted sampled texture colors in preview pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_texture_argb4444.elf`: writes a tiny non-twiddled ARGB4444 texture into VRAM, emits a textured real-shaped strip with UV corner coordinates, exposes decoded texture state, leaves RGB565-converted sampled texture colors in preview pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_texture_rgb565.elf`: writes a tiny non-twiddled RGB565 texture into VRAM, emits a textured real-shaped strip with UV corner coordinates, exposes decoded texture state, leaves sampled texture colors in preview pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_texture_twiddled_rgb565.elf`: writes a tiny twiddled RGB565 texture into VRAM, emits the same textured strip, exposes decoded twiddled texture state, leaves sampled texture colors in preview pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_texture_uv_modes.elf`: writes a tiny non-twiddled RGB565 texture into VRAM, emits a textured strip with U/V clamp and flip bits set, exposes decoded texture-coordinate mode state, leaves flipped sampled colors in preview pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_texture_shading.elf`: writes a tiny non-twiddled white RGB565 texture into VRAM, emits a green textured strip with modulate texture shading, exposes decoded texture shading state, leaves modulated green preview pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_texture_filter.elf`: writes four center RGB565 texels into a non-twiddled texture, emits a textured strip with bilinear filtering, exposes decoded filter state, leaves the blended midpoint preview pixel visible, shuts down, and reports `ProgramExit`.
- `dcsharp_pvr_texture_size.elf`: writes sentinel texels into a 16x16 non-twiddled RGB565 texture, emits a textured strip with encoded 16x16 dimensions, exposes decoded texture-size state, leaves corner sampled preview pixels, shuts down, and reports `ProgramExit`.
- `dcsharp_asic_irqb.elf`: triggers a raw Maple DMA completion with ASIC IRQB enabled, leaves the decoded pending source observable, exits through the firmware-exit trap, and reports `FirmwareExit`.
- `dcsharp_asic_events.elf`: masks SH-4 interrupts, enables ASIC VBlank IRQ9, observes the raw ACK bit, clears it, disables the mask, shuts down, and reports `ProgramExit`.
- `dcsharp_asic_irq9_masked.elf`: masks SH-4 interrupts, enables ASIC VBlank IRQ9, waits until the raw ACK bit is pending, leaves the pending IRQ9 source observable, exits through the firmware-exit trap, and reports `FirmwareExit`.
- `dcsharp_interrupt_nesting.elf`: enables ASIC VBlank IRQ9 with interrupts unmasked, enters the external interrupt vector, deliberately leaves the VBlank source pending while `SR.BL` is set, clears the source, returns through `rte`, and exposes `INTEVT=0x0320`.
- `dcsharp_timer_asic_arbitration.elf`: masks SH-4 interrupts, makes TMU0 and VBlank IRQ9 pending together, unmasks interrupts, records TMU0 `INTEVT=0x0400` before IRQ9 `INTEVT=0x0320`, clears both sources, and reports `UnsupportedInstruction`.
- `dcsharp_timer_irq_masked.elf`: masks SH-4 interrupts, leaves TMU0 underflow pending with priority 10, reports pending timer diagnostics, and keeps `INTEVT=0x00000000`.
- `dcsharp_timer_irq_accept.elf`: accepts a pending TMU0 interrupt at `VBR+0x600`, records `INTEVT=0x0400`, clears the underflow bit, returns through `rte`, and reports no pending timer interrupt.
- `dcsharp_vblank_idle.elf`: masks SH-4 interrupts, enables ASIC VBlank IRQ9, spins in a read-only ACK polling loop until synthetic VBlank, exposes idle VBlank wake diagnostics, clears the ACK bit, shuts down, and reports `ProgramExit`.
- `dcsharp_aica_registers.elf`: writes AICA channel/global registers and sound RAM, exposes silent audio diagnostics, shuts down, and reports `ProgramExit`.
- `dcsharp_aica_playback_position.elf`: writes a short PCM16 sample into AICA RAM, keys on channel 0, sleeps long enough for the silence-safe playback stepper to reach loop end, exposes playback position/sample counters, shuts down, and reports `ProgramExit`.
- `dcsharp_aica_playback_loop.elf`: writes a short PCM16 sample into AICA RAM, keys on channel 0 with loop mode enabled, sleeps long enough to wrap through the loop window, exposes playback position/sample counters, shuts down, and reports `ProgramExit`.
- `dcsharp_aica_playback_pcm8.elf`: writes a short PCM8 sample into AICA RAM, keys on channel 0, sleeps long enough for the silence-safe playback stepper to reach loop end, exposes one-byte sample stride and byte counters, shuts down, and reports `ProgramExit`.
- `dcsharp_aica_stereo_pan.elf`: writes PCM16 sample data, keys on two channels with distinct pan/send and volume bytes, sleeps long enough for both silent playback counters to reach loop end, exposes per-channel pan/balance and byte counters, shuts down, and reports `ProgramExit`.
- `dcsharp_aica_adpcm_metadata.elf`: writes placeholder ADPCM bytes into AICA RAM, keys on channel 0 with ADPCM format and loop metadata, exposes compressed-format/pan/loop diagnostics plus packed playback counters, shuts down, and reports `ProgramExit`.

## Commit Hygiene

Commit source, docs, KOS sample source, and tests. Do not commit generated artifacts, build outputs, downloaded BIOS/media, or generated traces.
