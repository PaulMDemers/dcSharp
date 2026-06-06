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
- 7 titles reached the 50M instruction cap without an unsupported opcode or unmapped stop.
- 0 titles in the current preferred-descriptor matrix stop on concrete unmapped device reads.
- 3 titles stopped on bad/unsupported execution after boot handoff or firmware callback behavior.
- 1 title exited through the BIOS CD-menu path.
- The GDI extraction blocker is fixed for the five affected descriptors. They now extract `1ST_READ.BIN` from the later high-density ISO9660 volume rather than the low-density session volume.

The generic smoke path did not observe GD-ROM sector reads or TA command writes for this new batch. That makes the next broad-compatibility work less about rendering first and more about remaining bootstrap layout selection and firmware callback/table state.

## Results

| Title | Descriptor Used | 50M Result | Frontier |
| --- | --- | --- | --- |
| Crazy Taxi (USA) | CUE | `InstructionLimit`, `PC=0xAC0047DA` | Fixed missing SH-4 `ldc r0,ssr`, then modeled the area-0 mirror/external-device read at `0xA30100C0`. Current frontier is a long cached-bootstrap loop polling SCIF status. |
| Dead or Alive 2 (USA) | CUE | `InstructionLimit`, `PC=0x8C129E4A` | Generic smoke reaches the budget; existing title-specific probes remain the deeper rendering path. |
| Gauntlet Legends (USA) | GDI | `InstructionLimit`, `PC=0x8C037A3A` | GDI extraction now succeeds from volume `GAUNT1`, boot extent `0x8595B`, 3,761,284 bytes. |
| The Grinch (USA) | GDI | `FirmwareExit`, `PC=0x8C0000E8` after 1,386,943 instructions | GDI extraction now succeeds from volume `GRINCH`; still requests BIOS CD menu, `function=2`. |
| Legacy of Kain - Soul Reaver (USA) | CUE | `InstructionLimit`, `PC=0x8C038934` | Reaches generic execution budget. |
| Rayman 2 - The Great Escape (USA) | CUE | `InstructionLimit`, `PC=0x8C0D18B0` | Reaches generic execution budget. |
| Sega Rally 2 (USA) | CUE | `InstructionLimit`, `PC=0x8C017968` at 50M instructions | WinCE `0WINCEOS.BIN` now follows the GD-ROM payload mapping: the first `0x800` bytes are skipped from the runtime payload, and the remaining image is loaded at `0x8C010000`. Sega Rally reaches real WinCE startup code, executes `ldtlb`, installs a low-virtual RAM mapping, handles the observed banked-register `ldc`, dispatches the first `WIN32.PerformCallBack` thunk, and now reaches the generic 50M budget inside a repeating WinCE scheduler/list-management path rather than faulting. Guarded WinCE scheduler fast-forwards collapse the stable timer-delta helper and scheduler return tail, so instruction-limit checkpoints can land a few opcodes earlier in the same loop than pre-fast-forward traces. |
| Sonic Adventure (USA, Rev A) | GDI | `UnsupportedInstruction` at `PC=0x8C000000` after 46,589,874 instructions | GDI extraction now succeeds from volume `SONIC_ADV`; unlike the CUE fallback, the GDI path exposes a zero-PC firmware/callback-style frontier. |
| Sonic Adventure 2 (USA) | CUE | `UnsupportedInstruction` at `PC=0x8C000000` after 44,590,162 instructions | Guest jumps through pointer `0x8C17EC60`, which currently contains zero; likely firmware callback/table state. |
| Sonic Shuffle (USA) | GDI | `InstructionLimit`, `PC=0x8C02A0C0` | GDI extraction now succeeds from volume `SONIC_SHUFFLE`; the CUE fallback now moves past modem and AICA RTC reads to a zero-PC firmware/callback-style frontier at 12,139,952 instructions. |
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

The first device-frontier pass retired the unmapped stops that were blocking Crazy Taxi and the Sonic Shuffle CUE fallback:

- Crazy Taxi: `0xA30100C0` normalizes through the documented `0x02000000-0x03FFFFFF` area-0 mirror to the `0x01000000-0x01FFFFFF` external-device window. Absent external devices now read as zero and log under the `external` domain.
- Sonic Shuffle CUE fallback: `0xA0600004` is in the `0x00600000-0x006007FF` modem window. Absent modem reads now return zero and log under the `modem` domain.
- Sonic Shuffle CUE fallback then reached `0xA0710000`, the AICA RTC control window. The AICA aperture now includes `0x00710000-0x0071000B`.

These changes make the current frontiers more specific: Crazy Taxi is waiting in cached bootstrap/SCIF code, and Sonic Shuffle CUE joins the zero-PC firmware/callback family instead of stopping on an unmapped read.

### WinCE Bootstrap Frontier

Sega Rally 2 uses `0WINCEOS.BIN` rather than the normal `1ST_READ.BIN` path. The analyzer now recognizes its WinCE-style header in the original layout:

- Header load address field: `0x0C010000`
- Entry offset field: `0x800`
- GD-ROM payload offset: `0x800`
- Suggested entry point: `0x8C010000`
- Entry trampoline target: `0x8C0120E0` / file offset `0x28E0`

The GD-ROM loader behavior matches the WinCE REIOS path used by Flycast: the header-sized first `0x800` bytes are parked separately, while the payload beginning at file offset `0x800` is loaded at `0x8C010000`. Running from IP.BIN now reaches the WinCE entry stub at the corrected payload base:

```text
0x8C010004: 0xD001  mov.l @(0x01,pc),r0 ; [0x8C01000C]=0x8C0120E0
0x8C010008: 0x402B  jmp @r0 ; target=0x8C0120E0
```

With the corrected file-offset mapping, target `0x8C0120E0` corresponds to file offset `0x28E0`, where executable startup code begins. The next observed WinCE bootstrap sequence writes `PTEH=0x00005800` and `PTEL=0x0C13194A`, executes `ldtlb`, and fetches through the resulting low virtual page (`0x00005B90 -> 0x0C131B90`). A minimal 1 KiB TLB entry path now covers that access, and SH-4 banked-register `ldc r14,r4_bank` is decoded for the following context setup.

The first odd negative thunk is `0xFFFFFD1F`, which decodes as `WIN32.PerformCallBack`. The observed `CALLBACKINFO` block at `0x8C137538` contains `hProc=0x0CEEEFE2`, `pfn=0x8C021FA0`, and `pvArg0=0x8C0116E0`. A narrow WinCE HLE path now transfers control to that callback with `pvArg0` as the first argument and leaves `PR` pointing back to the original caller. With that in place, Sega Rally reaches the generic 50M instruction budget. The current profile is a repeating WinCE scheduler/list-management path around `0x8C0176A8-0x8C017A00` plus low virtual helper calls such as `0x00005BC0`. The guarded fast-forward path preserves the actual TMU counter read at `0x8C02DB32`, then skips only exact opcode/state-matched post-read helper and scheduler-return tails; on the local 50M probe this reduced wall time from roughly 101 seconds to roughly 62 seconds. A 100M probe now completes in roughly 163 seconds and still stops in the same scheduler loop at `PC=0x8C0179D6`, with no GD-ROM reads, no TA work, and no additional WinCE syscall log entries.

Use `--wince-syscall-log` on longer probes to capture odd negative WinCE API thunks without a broad trace. Use `--wince-scheduler-log` on the same runs to capture the compact final scheduler/KData field snapshot without disabling fast-forwards. A 100M Sega Rally check currently logs only the initial `WIN32.PerformCallBack`, confirming the long hot loop is guest scheduler/list code plus TMU reads rather than repeated syscall HLE. A 50M scheduler snapshot stops at `PC=0x8C017968` with `scheduler-dispatch-state=0`, `runqueue-or-thread-list-next=1`, `timer-or-wait-list-head=1`, `current-wait-delta=7`, `kernel-tick-total=3275`, and `scheduler-tail-state=3287`; the 100M snapshot stops at `PC=0x8C0179D6` with the same queue/wait flags, `current-wait-delta=7`, and the two counters advanced to `7275` and `7287`. A narrow trace of `0x8C02E228-0x8C02E248` shows the tick handler acknowledging TMU status at `0xFFD80010`, adding `25` to `kernel-tick-total` at `0x8C131888`, and writing `25` to `kernel-tick-delta` at `0x8C13188C`. That points the next investigation at the WinCE scheduler's timebase/wait-list semantics rather than a missing GD-ROM read or an unserviced WinCE API. A narrower attempted fast-forward of the `0x8C017780` empty-list/low-helper return path was measured and rejected: it skipped more guest instructions but added enough guard/write overhead to make the 50M wall-clock probe slower.

### Bootstrap/Firmware Frontiers

- Sonic Adventure 2 jumps through a zero callback/table pointer to `0x8C000000`, which points at missing firmware initialization state or a callback-registration side effect.
- Sonic Adventure now does the same from the fixed GDI path, while the CUE fallback reaches the instruction budget. Compare CUE/GDI IP.BIN work-area and high-density media state before treating it as a CPU issue.
- Sonic Shuffle CUE now joins this family after modem/AICA RTC modeling, while Sonic Shuffle GDI reaches the instruction budget.
- The Grinch asks for the BIOS CD menu with system function `2`, which usually means the disc/authentication/media path did not satisfy the title's boot expectation.

## Next Work

1. Classify Sega Rally 2's WinCE scheduler timebase and wait-list fields around `0x8C131888`, `0x8C13188C`, `0x8C136540`, and `0x8C136664`, then trace which guest condition should clear the stable `current-wait-delta=7` loop before adding more fast-forwards.
2. Trace Sonic Adventure, Sonic Adventure 2, and Sonic Shuffle CUE around their zero-PC firmware/callback frontiers and compare GDI versus CUE work-area state.
3. Re-run the full sweep after each fix and keep this report as the baseline.
