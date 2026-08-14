# Autonomous Engine

## Overview
As RigMD evolves from an advisory system, the Autonomous Engine is introduced to handle automated issue remediation on Windows environments. The engine operates under a strict "Controlled Autonomy" paradigm to prevent system degradation.

## Core Loop
1. **Diagnosis**: Identify the root cause with high confidence.
2. **Proposal**: Formulate a remediation action.
3. **Execution**: Apply the fix (if permitted by policy).
4. **Verification**: Validate if the fix successfully resolved the symptom.
5. **Decision (Pivot or Rollback)**: If verification fails, either rollback the change or pivot to an alternative action.

## 1. Verification Strategy
Every autonomous action must have a measurable verification step.
- *Example*: If the engine restarts the Windows Audio service, the verification step checks if the service state transitions to `Running` and queries the audio endpoint for activity.

## 2. Rollback Capability
The engine must record the previous system state before mutating any configuration.
- Actions that cannot be rolled back (e.g., deleting user data) are **strictly prohibited** from autonomous execution.
- If an executed fix fails verification, the engine automatically triggers the rollback sequence.

## 3. Pivot Logic
If a remediation attempt fails and is rolled back, the engine "pivots" to the next most probable cause and associated action, escalating the severity level or eventually falling back to manual advisory output.
