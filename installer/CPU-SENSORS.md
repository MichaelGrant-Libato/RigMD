# Optional CPU temperature provider

RigMD uses LibreHardwareMonitorLib 0.9.6 with only CPU monitoring enabled. It
uses the existing PawnIO driver when Windows grants access. Setup packages the
user-mode library. PawnIO installation and removal are separate from RigMD.

CPU temperature appears under My Device Info > Processor when a supported
sensor reports a reading. Machines without driver access can still use an
existing LibreHardwareMonitor or OpenHardwareMonitor WMI provider. An
unavailable sensor is shown as unavailable.

## Upstream source and licenses

- LibreHardwareMonitorLib 0.9.6, MPL 2.0 and upstream component notices.
  Matching source is at
  https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701
- PawnIO sensor modules 0.1.6, LGPL 2.1.
  Matching source is at
  https://github.com/namazso/PawnIO.Modules/tree/4aa792beb020a14c8261072e9786d2dfb38489d9

The upstream source archives include their copyright notices and build files.
The official PawnIO driver is available separately from its publisher at
https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0
