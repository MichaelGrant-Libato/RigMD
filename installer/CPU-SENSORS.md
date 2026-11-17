# Optional CPU temperature monitoring

RigMD uses LibreHardwareMonitor 0.9.6 with the official signed PawnIO 2.2.0
driver. Only CPU hardware is enabled in LibreHardwareMonitor. GPU temperatures
continue to use the installed Windows display driver.

This is a third-party kernel driver. It replaces the old WinRing0 approach;
it does not guarantee that every antivirus product will accept the application.
RigMD does not change Defender, Secure Boot, Memory Integrity, or driver signing
settings. The monitoring loop never downloads or installs drivers.

The installer offers an unchecked **Enable CPU temperature readings** option.
Installing it needs Windows administrator approval. RigMD Desktop already
requests administrator rights; a separately hosted API also needs access to the
PawnIO device. Unsupported or inaccessible sensors remain **Not available**.
AMD and Intel desktops and laptops use the same CPU-only provider, but individual
models and firmware can limit access. No other monitoring app needs to run.

## Development setup

From the repository root:

```powershell
powershell.exe -NoProfile -File installer\Manage-PawnIO.ps1 -Action Status
powershell.exe -NoProfile -File installer\Manage-PawnIO.ps1 -Action Install
```

Restart RigMD after installation. The setup script pins the official release's
SHA-256 and verifies its Authenticode signer before execution. It never uses the
unrestricted PawnIO edition.

## Undo

Close RigMD and any other app using PawnIO, then run:

```powershell
powershell.exe -NoProfile -File installer\Manage-PawnIO.ps1 -Action Uninstall
```

For an installed copy, the script is under `Support` in the RigMD installation
directory. PawnIO can also be removed through Windows Installed Apps. Restart
Windows if its uninstaller requests it. RigMD's normal uninstaller leaves this
shared driver installed because other monitoring apps may use it.

The code is isolated on `codex/cpu-temperature-pawnio`. Switching to the previous
branch removes the code integration; removing the installed driver is a separate
operation. Preserve unrelated working changes before switching branches.

## Packaging

`build-installer.ps1` verifies the pinned driver installer and prepares matching
upstream source archives. Licenses, source archives, and these instructions ship
under `Support`. Component versions and source locations are listed in
`licenses/CPU-SENSOR-NOTICES.md`.

## Verification on November 17, 2026

- Ryzen 5 5600X: four live CPU samples, 53.4–55.8 degrees Celsius, using
  `/amdcpu/0/temperature/2` (`Core (Tctl/Tdie)`).
- Existing Radeon RX 6600 temperature readings remained available.
- Microsoft Defender reported no threats in the official installer, installed
  PawnIO driver, and CPU probe build at the time of testing.
- Physical Intel and laptop testing remains necessary before broad release.
