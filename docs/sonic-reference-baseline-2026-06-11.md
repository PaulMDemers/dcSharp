# Sonic Reference Baseline - 2026-06-11

This note captures the first Flycast reference frames and normalized dcSharp SA2 long-probe checkpoints taken after syncing the root Dreamcast BIOS files into Flycast v2.6.

## Flycast Reference Frames

Reference PNGs are generated artifacts under `artifacts\reference-frames\` and are intentionally not committed.

| Game | Capture | Result |
| --- | --- | --- |
| Sonic Adventure | `sa1-bios-8s.png` | Dreamcast BIOS swirl. |
| Sonic Adventure 2 | `sa2-bios-8s-dpi.png` | Dreamcast BIOS/title startup frame. |
| Sonic Shuffle | `shuffle-bios-8s.png` | Dreamcast BIOS swirl. |
| Sonic Adventure | `sa1-reference-35s.png` | Opening city video. |
| Sonic Adventure 2 | `sa2-reference-35s.png` | Title-logo fade. |
| Sonic Shuffle | `shuffle-reference-35s.png` | Opening video. |

Capture command shape:

```powershell
powershell -ExecutionPolicy Bypass -File tools\capture-flycast-frame.ps1 -Game SA2 -DelaySeconds 35 -OutputPath artifacts\reference-frames\sa2-reference-35s.png
```

## dcSharp SA2 Checkpoints

All probes used the Release CLI, `tools\probe-long.ps1`, `TraceTail=16` or `24`, and profile logging enabled. Outputs are generated under `artifacts\long-probes\`.

| Budget | Elapsed | PC | Stop | PVR regs | GD-ROM reads | CPU fast-forward | Artifact |
| ---: | ---: | --- | --- | ---: | ---: | ---: | --- |
| 50M | 11s | `0x8C15C7F4` | `InstructionLimit` | 2,255 | 0 | 48,723,337 | `sa2-baseline-50m-20260611-141554` |
| 120M | 1m28s | `0x8C15B77A` | `InstructionLimit` | 23,656 | 0 | 105,512,407 | `sa2-baseline-120m-20260611-141614` |
| 300M | 4m17s | `0x8C15BF8E` | `InstructionLimit` | 78,685 | 0 | 251,541,116 | `sa2-baseline-300m-20260611-141751` |
| 1B | 12m21s | `0x8C14C884` | `InstructionLimit` | 292,687 | 0 | 819,430,926 | `sa2-1b-20260611-130319` |

The same hot AICA/name-channel cluster dominates all checkpoints:

- `0x8C15B92E`
- `0x8C15C564`
- `0x8C15C56E`
- `0x8C15B8EC`
- `0x8C15B938`
- `0x8C15B68C-0x8C15B68E`

The current same-runner baseline does not reach game GD-ROM reads within 1B instructions. The next implementation pass should therefore stay focused on the AICA work/name/channel setup loop and the mailbox/driver-completion model rather than jumping to PVR TA rendering yet.
