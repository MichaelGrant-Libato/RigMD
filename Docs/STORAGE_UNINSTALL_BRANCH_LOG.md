# Storage uninstall branch change log

Branch: `codex/storage-app-uninstall`

Comparison base: `main` at `c8fc16e3f80e95074b45c329cf246a4b217d5a41`. Direct uninstall is implemented in `3890d6a`, based on `1ed5ea9` from `codex/software-inventory-usability`.

## Direct uninstall

**My Device Info → Storage → Installed apps** offers **Uninstall…** in the expanded row of an eligible app. The confirmation displays the app name and version, explains the machine-wide scope, and identifies WinGet as the launcher. The product code and exit code are available in expandable details. RigMD refreshes the list after the operation and preserves the search, drive filter, and sort.

For this version, eligibility requires a machine-wide MSI registration with one valid product code, a corresponding installed product in Windows Installer, and Microsoft's App Installer package. Protected registrations show their reason. Per-user installations, packaged apps, and apps whose installation RigMD cannot verify use **Manage in Windows**. Driver records remain informational and link to Device Manager.

| Change | Implementation |
| --- | --- |
| Inventory and reasons | The app inventory adds `uninstallAvailability` and `uninstallReason`. The Windows provider checks MSI registration, installer removal flags, package protection, and App Installer availability. |
| Review | `POST /api/software-inventory/apps/uninstall-review` accepts the inventory app ID. It verifies the current registration and that WinGet supports the required options, then issues a three-minute approval ID for that app and version. |
| Execution | `POST /api/software-inventory/apps/uninstall` accepts the approval ID. It consumes the ID, rechecks the selected registration, and permits one active uninstaller per RigMD API process. |
| WinGet | RigMD resolves WinGet from the registered Microsoft package. It launches the exact MSI product code in machine scope with the native interactive uninstaller. The request supplies neither a command line nor an executable path. |
| Outcome | RigMD shows **Removed** only when WinGet succeeds and the Windows Installer record and machine uninstall entries are gone. It distinguishes cancellation, restart, unavailable match, failure, and unverified removal when WinGet exposes enough information. The app shows the exit code when available. |
| Trust boundary | The endpoints use RigMD's existing authenticated local API. The approval stays in process memory. This is a manual desktop action outside the ReAct tool registry, cloud sync, and saved reports. |

The UI changes are in `StorageSoftwareList.tsx`, `AppUninstallAction.tsx`, and `DeviceDetailModal.tsx`, including keyboard focus limited to visible controls. `SoftwareInventoryController.cs`, `IAppUninstallService.cs`, and `SoftwareInventory.cs` define the API and its contract. `WindowsSoftwareInventory.cs`, `MachineMsiRegistration.cs`, and `WindowsAppUninstallService.cs` implement eligibility, review, and execution. `Program.cs` registers the service.

## Inherited differences from main

| Commit | Change |
| --- | --- |
| `2253aad` | Adds the Home GPU card and adapter-specific live utilization for dedicated and integrated graphics. |
| `cca072e` | Reads supported GPU temperatures through existing display driver interfaces and tracks freshness per adapter. |
| `491882a` | Adds CPU-only LibreHardwareMonitor readings and optional signed PawnIO setup, with installer sources, licenses, and removal instructions. |
| `deab33c` | Displays live GPU temperature on Home, one reading per adapter on multi-GPU PCs. |
| `20471a1` | Adds Storage Overview, Installed apps, and Drivers with Windows inventory, drive mapping, and Settings or Device Manager handoffs. |
| `1ed5ea9` | Adds drive letters and free space, size sorting, local icons, clearer driver rows, and one inventory refresh after returning from Settings. |

Use `git diff --name-status main...HEAD` for the complete committed file list and `git diff --name-status 1ed5ea9...HEAD` for the uninstall work alone. These comparisons exclude local working edits and generated preview files.

## Documentation impact

| Document | Update when adopting this branch |
| --- | --- |
| [README](../README.md) | Describe the Storage navigation, direct uninstall eligibility, review flow, and Windows Settings fallback. Add Home GPU temperature and optional CPU temperature to the feature overview. |
| [System Architecture](ARCHITECTURE.md) | Add the four software inventory routes, including both uninstall requests. Describe the registry, Windows Installer, App Installer, approval, and post-operation verification flow. Add the per-adapter and optional CPU sensor sources. |
| [Remediation Policy](REMEDIATION_POLICY.md) | Record uninstall as a manual local action with app-specific consent, a fresh registration check, and observable results. Keep it outside the ReAct tool counts and autonomous approval workflow. |
| [SRS](RigMD%20-%20new%20SRS%20.pdf) | Add requirements for per-drive app discovery, unknown locations and sizes, conditional removal, confirmation, fallback, and verified result states. Drivers have a separate informational view. |
| [SDD](RigMD%20-new%20SDD.pdf) | Add the Storage interaction and uninstall sequence, eligibility rules, and failure paths. Include the telemetry source and freshness changes carried by this branch. |
| [CPU sensor setup](../installer/CPU-SENSORS.md) | Continue to use the inherited PawnIO opt-in and rollback instructions. WinGet availability affects direct uninstall separately from temperature monitoring. |

## Verification

The backend suite passed 830 tests and the frontend suite passed 131. The simulated uninstaller covers exact identity and arguments, expired and reused approvals, changed registrations, concurrent execution, reported outcomes, and local authentication. A browser test verified the protected and fallback rows, cancellation, confirmation, result details, and the refreshed list. The live check found 10 eligible, 48 protected, and 95 Settings-only entries out of 153 installed app records on the development PC. Live WinGet verification stopped at review and help output.

References: [WinGet uninstall options](https://learn.microsoft.com/en-us/windows/package-manager/winget/uninstall), [Windows Installer product information](https://learn.microsoft.com/en-us/windows/win32/api/msi/nf-msi-msigetproductinfoexw), and [installer removal flags](https://learn.microsoft.com/en-us/windows/win32/msi/arpnoremove).
