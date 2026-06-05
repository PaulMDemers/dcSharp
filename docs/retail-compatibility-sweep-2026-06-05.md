# Retail Compatibility Sweep - 2026-06-05

This sweep covers the local user-provided retail images under `retail_discs/`.
The disc images themselves are not repository artifacts.

## Method

- Built Release binaries with `dotnet build -c Release`.
- Ran broad smoke probes with:

```powershell
dotnet src\DcSharp.Cli\bin\Release\net10.0\DcSharp.Cli.dll media boot-smoke <descriptor> --scan-sectors 1024 --instructions 50000000 --trace-tail 0 --stop-on-unmapped --json
```

- Preferred GDI descriptors when available, then reran CUE fallbacks for GDI extraction failures.
- Captured focused trace tails for the distinct failure classes.

## Summary

- 11 retail titles were probed.
- 6 titles reached the 50M instruction cap without an unsupported opcode or unmapped stop.
- 2 titles stopped on concrete unmapped device reads.
- 2 titles stopped on bad/unsupported execution after boot handoff or firmware callback behavior.
- 1 title exited through the BIOS CD-menu path.
- 5 GDI descriptors report usable IP.BIN metadata through `media inspect` but fail boot-file extraction; their CUE fallbacks are currently the practical probe path.

The generic smoke path did not observe GD-ROM sector reads or TA command writes for this new batch. That makes the next broad-compatibility work less about rendering first and more about GDI extraction, remaining bootstrap layout selection, and missing device-region modeling.

## Results

| Title | Descriptor Used | 50M Result | Frontier |
| --- | --- | --- | --- |
| Crazy Taxi (USA) | CUE | `DeviceAccessStop` at `PC=0xAC0040BA` after 7,341,356 instructions | Fixed missing SH-4 `ldc r0,ssr` decode first; next blocker is byte read from `0xA30100C0`. |
| Dead or Alive 2 (USA) | CUE | `InstructionLimit`, `PC=0x8C129E4A` | Generic smoke reaches the budget; existing title-specific probes remain the deeper rendering path. |
| Gauntlet Legends (USA) | CUE fallback | `InstructionLimit`, `PC=0x8C037A34` | GDI extraction fails to find `1ST_READ.BIN`; CUE fallback reaches execution budget. |
| The Grinch (USA) | CUE fallback | `FirmwareExit`, `PC=0x8C0000E8` after 8,672,579 instructions | BIOS CD-menu request, `function=2`; GDI extraction also fails. |
| Legacy of Kain - Soul Reaver (USA) | CUE | `InstructionLimit`, `PC=0x8C038934` | Reaches generic execution budget. |
| Rayman 2 - The Great Escape (USA) | CUE | `InstructionLimit`, `PC=0x8C0D18B0` | Reaches generic execution budget. |
| Sega Rally 2 (USA) | CUE | `UnsupportedInstruction` at `PC=0x8C010002` after 8,887,171 instructions | Handoff target contains invalid-looking startup words; forcing descrambled layout still produces invalid execution. Treat as media extraction/layout selection before CPU work. |
| Sonic Adventure (USA, Rev A) | CUE fallback | `InstructionLimit`, `PC=0x8C66598E` | GDI extraction fails; CUE fallback reaches execution budget. |
| Sonic Adventure 2 (USA) | CUE | `UnsupportedInstruction` at `PC=0x8C000000` after 44,590,162 instructions | Guest jumps through pointer `0x8C17EC60`, which currently contains zero; likely firmware callback/table state. |
| Sonic Shuffle (USA) | CUE fallback | `DeviceAccessStop` at `PC=0x8C03101E` after 11,983,316 instructions | Byte read from `0xA0600004`; likely external/G2-style device-region modeling. GDI extraction also fails. |
| Wetrix+ (USA) | CUE fallback | `InstructionLimit`, `PC=0x8C164F16` | GDI extraction fails; CUE fallback reaches execution budget. |

## Focused Findings

### SH-4 Control Register Decode

Crazy Taxi stopped at `0xAC004018` on opcode `0x403E` while executing cached bootstrap code:

```text
stc sr,r0
and r1,r0
ldc r0,ssr
```

The SH-4 control-register group includes saved status/program-counter transfers (`SSR`/`SPC`) alongside `SR`, `GBR`, and `VBR`. Implementing `ldc Rn,SSR`, `ldc Rn,SPC`, `stc SSR,Rn`, and `stc SPC,Rn` moves Crazy Taxi to the next device access frontier.

### GDI Extraction Gap

`media inspect` finds Dreamcast boot sectors and `1ST_READ.BIN` names in the affected GDI images, but `boot-smoke` cannot extract the boot file. The likely bug is in choosing or translating the high-density ISO9660 data track for GDI boot-file lookup, because CUE fallback succeeds for the same titles.

Affected GDI descriptors:

- Gauntlet Legends
- The Grinch
- Sonic Adventure
- Sonic Shuffle
- Wetrix+

### Device Frontiers

Crazy Taxi and Sonic Shuffle now give clean unmapped stops instead of vague compatibility failures:

- Crazy Taxi: `0xA30100C0`, byte read, cached bootstrap region.
- Sonic Shuffle: `0xA0600004`, byte read after setting a small external-style pointer.

Both should be classified and probed through narrow memory-map tests before modeling behavior.

### Bootstrap/Firmware Frontiers

- Sega Rally 2 appears to load an invalid or wrongly transformed first boot word at `0x8C010000`; this should be investigated with extraction/analyze tooling before adding CPU instructions.
- Sonic Adventure 2 jumps through a zero callback/table pointer to `0x8C000000`, which points at missing firmware initialization state or a callback-registration side effect.
- The Grinch asks for the BIOS CD menu with system function `2`, which usually means the disc/authentication/media path did not satisfy the title's boot expectation.

## Next Work

1. Fix GDI boot-file extraction against high-density data tracks, then rerun the five GDI-capable titles using their GDI descriptors.
2. Add device-domain classification/tests for `0xA0600004` and `0xA30100C0`, then decide whether they need open-bus returns, modem/G2 handling, or focused HLE.
3. Trace Sega Rally 2 extraction with `media extract-boot` and `media analyze-boot` artifacts to determine whether the boot binary is selected, biased, or descrambled incorrectly.
4. Trace Sonic Adventure 2 around the writer/initializer for pointer `0x8C17EC60`.
5. Re-run the full sweep after each fix and keep this report as the baseline.
