# Retail Compatibility Sweep - 2026-06-05

This sweep covers the local user-provided retail images under `retail_discs/`.
The disc images themselves are not repository artifacts.

## Method

- Built Release binaries with `dotnet build -c Release`.
- Ran broad smoke probes with:

```powershell
dotnet src\DcSharp.Cli\bin\Release\net10.0\DcSharp.Cli.dll media boot-smoke <descriptor> --scan-sectors 1024 --instructions 50000000 --trace-tail 0 --stop-on-unmapped --json
```

- Preferred GDI descriptors when available, then reran CUE fallbacks for GDI extraction failures. After the high-density GDI ISO-volume fix, the five formerly failing GDI descriptors were rerun from GDI.
- Captured focused trace tails for the distinct failure classes.

## Summary

- 11 retail titles were probed.
- 6 titles reached the 50M instruction cap without an unsupported opcode or unmapped stop.
- 1 title stopped on a concrete unmapped device read.
- 3 titles stopped on bad/unsupported execution after boot handoff or firmware callback behavior.
- 1 title exited through the BIOS CD-menu path.
- The GDI extraction blocker is fixed for the five affected descriptors. They now extract `1ST_READ.BIN` from the later high-density ISO9660 volume rather than the low-density session volume.

The generic smoke path did not observe GD-ROM sector reads or TA command writes for this new batch. That makes the next broad-compatibility work less about rendering first and more about remaining bootstrap layout selection, firmware callback/table state, and missing device-region modeling.

## Results

| Title | Descriptor Used | 50M Result | Frontier |
| --- | --- | --- | --- |
| Crazy Taxi (USA) | CUE | `DeviceAccessStop` at `PC=0xAC0040BA` after 7,341,356 instructions | Fixed missing SH-4 `ldc r0,ssr` decode first; next blocker is byte read from `0xA30100C0`. |
| Dead or Alive 2 (USA) | CUE | `InstructionLimit`, `PC=0x8C129E4A` | Generic smoke reaches the budget; existing title-specific probes remain the deeper rendering path. |
| Gauntlet Legends (USA) | GDI | `InstructionLimit`, `PC=0x8C037A3A` | GDI extraction now succeeds from volume `GAUNT1`, boot extent `0x8595B`, 3,761,284 bytes. |
| The Grinch (USA) | GDI | `FirmwareExit`, `PC=0x8C0000E8` after 1,386,943 instructions | GDI extraction now succeeds from volume `GRINCH`; still requests BIOS CD menu, `function=2`. |
| Legacy of Kain - Soul Reaver (USA) | CUE | `InstructionLimit`, `PC=0x8C038934` | Reaches generic execution budget. |
| Rayman 2 - The Great Escape (USA) | CUE | `InstructionLimit`, `PC=0x8C0D18B0` | Reaches generic execution budget. |
| Sega Rally 2 (USA) | CUE | `UnsupportedInstruction` at `PC=0x8C010002` after 8,887,171 instructions | Handoff target contains invalid-looking startup words; forcing descrambled layout still produces invalid execution. Treat as media extraction/layout selection before CPU work. |
| Sonic Adventure (USA, Rev A) | GDI | `UnsupportedInstruction` at `PC=0x8C000000` after 46,589,874 instructions | GDI extraction now succeeds from volume `SONIC_ADV`; unlike the CUE fallback, the GDI path exposes a zero-PC firmware/callback-style frontier. |
| Sonic Adventure 2 (USA) | CUE | `UnsupportedInstruction` at `PC=0x8C000000` after 44,590,162 instructions | Guest jumps through pointer `0x8C17EC60`, which currently contains zero; likely firmware callback/table state. |
| Sonic Shuffle (USA) | GDI | `InstructionLimit`, `PC=0x8C02A0C0` | GDI extraction now succeeds from volume `SONIC_SHUFFLE`; the CUE fallback still exposes the `0xA0600004` unmapped read. |
| Wetrix+ (USA) | GDI | `InstructionLimit`, `PC=0x8C16652C` | GDI extraction now succeeds from volume `WETRIXPLUS`, boot extent `0x85DB7`, 1,475,872 bytes. |

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

`media inspect` found Dreamcast boot sectors and `1ST_READ.BIN` names in the affected GDI images, but `boot-smoke` originally could not extract the boot file. The cause was ISO9660 volume selection: retail GDI dumps can contain a valid low-density session ISO without the game boot binary plus a later high-density ISO volume that actually contains `1ST_READ.BIN`. `Iso9660FileSystem` now tries later data-track volumes first.

Fixed GDI descriptors:

- Gauntlet Legends
- The Grinch
- Sonic Adventure
- Sonic Shuffle
- Wetrix+

### Device Frontiers

Crazy Taxi and the Sonic Shuffle CUE fallback give clean unmapped stops instead of vague compatibility failures:

- Crazy Taxi: `0xA30100C0`, byte read, cached bootstrap region.
- Sonic Shuffle CUE fallback: `0xA0600004`, byte read after setting a small external-style pointer. The GDI path now reaches the instruction limit instead, so this should be treated as a descriptor/layout-sensitive frontier.

Both should be classified and probed through narrow memory-map tests before modeling behavior.

### Bootstrap/Firmware Frontiers

- Sega Rally 2 appears to load an invalid or wrongly transformed first boot word at `0x8C010000`; this should be investigated with extraction/analyze tooling before adding CPU instructions.
- Sonic Adventure 2 jumps through a zero callback/table pointer to `0x8C000000`, which points at missing firmware initialization state or a callback-registration side effect.
- Sonic Adventure now does the same from the fixed GDI path, while the CUE fallback reaches the instruction budget. Compare CUE/GDI IP.BIN work-area and high-density media state before treating it as a CPU issue.
- The Grinch asks for the BIOS CD menu with system function `2`, which usually means the disc/authentication/media path did not satisfy the title's boot expectation.

## Next Work

1. Add device-domain classification/tests for `0xA0600004` and `0xA30100C0`, then decide whether they need open-bus returns, modem/G2 handling, or focused HLE.
2. Trace Sega Rally 2 extraction with `media extract-boot` and `media analyze-boot` artifacts to determine whether the boot binary is selected, biased, or descrambled incorrectly.
3. Trace Sonic Adventure and Sonic Adventure 2 around their zero-PC firmware/callback frontiers and compare GDI versus CUE work-area state.
4. Re-run the full sweep after each fix and keep this report as the baseline.
