# Remediation Policy

## Overview
This document defines the boundaries of the Autonomous Engine, dictating what RigMD is allowed to fix automatically versus what requires explicit user consent or manual intervention.

## 1. Remediation Tiers

### Tier 1: Safe & Non-Destructive (Full Autonomy Permitted)
Actions that do not alter user data, persist across reboots in a destructive way, or risk system stability.
- **Examples**: Restarting non-critical background services, clearing temporary cache files, resetting network adapters, querying system logs.
- **Policy**: The engine may execute these automatically with a notification.

### Tier 2: Configuration Changes (User Consent Required)
Actions that modify system settings or driver states.
- **Examples**: Reinstalling/updating device drivers, modifying registry keys, altering startup programs.
- **Policy**: The engine will stage the fix and prompt the user for explicit "1-Click" approval. Rollback data must be strictly captured.

### Tier 3: High Risk / Hardware (Advisory Only)
Issues related to physical hardware failure, BIOS/UEFI changes, or potential data loss.
- **Examples**: Failing hard drive detection, CPU thermal throttling due to thermal paste degradation.
- **Policy**: **Strictly Advisory**. The engine will never attempt to autonomously resolve these. It will generate a report for a human technician.

## 2. Transparency & Logging
Every autonomous action taken by the engine must be logged in the `DiagnosticSession` with:
- The exact timestamp.
- The state before the action.
- The action executed.
- The verification result.
