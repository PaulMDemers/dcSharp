# Long Probes and Reference Frames

This workflow is for expensive retail probes and reference-emulator frame capture while targeting the Sonic intro milestone. Generated probe output, local retail media, BIOS files, and downloaded emulator binaries stay under ignored directories.

## Long dcSharp Probes

Build the CLI in Release before a long run:

```powershell
dotnet build src\DcSharp.Cli -c Release
```

Run a one-billion-instruction Sonic Adventure 2 probe:

```powershell
powershell -ExecutionPolicy Bypass -File tools\probe-long.ps1 -Game SA2 -Instructions 1000000000 -ProfileLimit 200
```

Run all current Sonic target games:

```powershell
powershell -ExecutionPolicy Bypass -File tools\probe-long.ps1 -Game All -Instructions 1000000000 -RunName sonic-intro
```

Outputs are written to `artifacts\long-probes\<run>\`:

- `<game>-output.txt`: full CLI text or JSON output.
- `<game>-profile.txt`: hot PC profile from `--pc-profile-log`.
- `<game>-summary.json`: only when `-Json` is supplied.

The current Release runner has been moving 300M SA2 probes in minutes on this machine, so 1B instructions should be a minutes-scale probe, not an overnight job. A June 11, 2026 SA2 Release run reached 1B instructions in 12m21s and wrote its output under `artifacts\long-probes\sa2-1b-20260611-130319\`. Use the artifact output instead of terminal scrollback as the source of truth when comparing long runs.

Useful extra diagnostics:

```powershell
powershell -ExecutionPolicy Bypass -File tools\probe-long.ps1 -Game SA2 -Instructions 1000000000 -ExtraRunArgs @('--pvr-ta-sprite-log','artifacts\long-probes\sa2-sprites.txt','--pvr-ta-sprite-log-limit','128')
```

## Flycast Reference Setup

Download the current Windows x64 Flycast release into the ignored `dreamcast-downloads` tree:

```powershell
powershell -ExecutionPolicy Bypass -File tools\setup-flycast-reference.ps1
```

If `bios\dc_boot.bin` and `bios\dc_flash.bin` exist at the repo root, the setup script also copies them into Flycast's standalone `data` directory. Override either path when needed:

```powershell
powershell -ExecutionPolicy Bypass -File tools\setup-flycast-reference.ps1 -BiosRoot D:\Dreamcast\bios
```

The script writes the resolved executable path to:

```text
dreamcast-downloads\flycast\current.txt
```

Launch the target game manually or from PowerShell with the media path:

```powershell
& (Get-Content dreamcast-downloads\flycast\current.txt) "retail_discs\Sonic Adventure 2 (USA) (EnJaFrDeEs)\Sonic Adventure 2 (USA) (En,Ja,Fr,De,Es).cue"
```

Or use the target-game launcher:

```powershell
powershell -ExecutionPolicy Bypass -File tools\run-flycast-reference.ps1 -Game SA2
powershell -ExecutionPolicy Bypass -File tools\run-flycast-reference.ps1 -Game SA1 -PrintOnly
```

Keep reference screenshots and videos under `artifacts\reference-frames\`. The useful comparison set for the current milestone is:

- First visible license/BIOS transition frame.
- Sonic Adventure 2 first title/movie-intro frame.
- Sonic Adventure first title/movie-intro frame.
- Sonic Shuffle first title/movie-intro frame.

## AICA Mailbox Direction

KallistiOS documents the SH4/AICA interface as a pair of queues in AICA RAM. `aica_queue_t` has `head`, `tail`, `size`, `valid`, `process_ok`, and `data`; `aica_cmd_t` packets carry `size`, `cmd`, `timestamp`, `cmd_id`, and payload data. The stock ARM driver initializes command and response queues, then repeatedly updates channel positions and processes pending SH4-to-AICA commands when `process_ok` is set.

For dcSharp, that gives us a better target than SA2-specific completion writes:

- Detect initialized AICA command/response queues in AICA RAM.
- Model packet consumption by advancing `tail` toward `head` with wraparound semantics.
- Implement a minimal command interpreter for no-op, ping/pong, sync-clock, and channel start/stop/update.
- Raise/clear the SPU interrupt path separately from G2 DMA completion; KOS names `ASIC_EVT_SPU_DMA = 0x000f` for G2 channel 0 DMA completion and `ASIC_EVT_SPU_IRQ = 0x0101` for SPU interrupt.
- Keep the existing SA2 `EXEC` mailbox shortcut as a temporary compatibility shim until the queue model explains the same progress.
