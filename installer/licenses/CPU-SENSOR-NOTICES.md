# CPU sensor components

These upstream components are used without local modifications. RigMD calls
LibreHardwareMonitor with only CPU monitoring enabled. The upstream library
contains other sensor modules, which RigMD does not enable.

| Component | License | Matching source |
| --- | --- | --- |
| LibreHardwareMonitorLib 0.9.6 | MPL 2.0; additional upstream notices in its `Licenses` directory | `sources/LibreHardwareMonitor-0.9.6-source.zip`, commit `3d331e3370efb858411f19511373eff65a218701` |
| PawnIO 2.2.0 | GPL 2.0 or later, with the upstream IOCTL communication exception | `sources/PawnIO-2.2.0-source.zip`, commit `5cdf470831fdfff3f7f1d06363ca6b230f3bf35a` |
| PawnPP, PawnIO's pinned submodule | See its included upstream notices | `sources/PawnPP-source.zip`, commit `e64e4c37b2d8ba0d8ee57205faf8183aee12c438` |
| PawnIO.Modules 0.1.6, embedded by LibreHardwareMonitor 0.9.6 | LGPL 2.1 | `sources/PawnIO-Modules-0.1.6-source.zip`, commit `4aa792beb020a14c8261072e9786d2dfb38489d9` |

The driver archive includes the driver, PawnIOLib, utility, Pawn interpreter,
and build instructions. To rebuild, extract the PawnPP source into the driver's
`PawnPP` directory and follow the upstream CMake instructions and tool requirements.
Module source archives include their compiler sources and build scripts. Upstream
signing keys are not distributed; rebuilding is not a promise of a signed binary.

Full license texts and the PawnIO license exception accompany this notice. The
source archives retain upstream copyright notices, licenses, and build files.

Upstream projects:

- https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6
- https://github.com/namazso/PawnIO/tree/2.2.0
- https://github.com/namazso/PawnPP/tree/e64e4c37b2d8ba0d8ee57205faf8183aee12c438
- https://github.com/namazso/PawnIO.Modules/tree/0.1.6
- Official driver installer: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0
