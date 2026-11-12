# RigMD 0.2.18 remediation corrections

Windows Update download-cache cleanup now requires the exact approved file snapshot, verified stopped service state, and restoration/verification of the original running/stopped state. It rejects changed/expired context, never expands the approved file list, and reports partial deletion and recovery failure as unsuccessful.

Explorer restart is bound to the current account/session's actual desktop-shell process identity. It targets no other Explorer process and requires a verified replacement shell window with the original process gone. Elevated shell restart is blocked; use normal RigMD for that operation. Legacy context-free restart/update cleanup cannot execute.

Heat, battery and elevated-CPU warnings provide specific investigation guidance rather than empty app-close proposals. Memory actions retain their independent supported scope. Valid mixed-result storage findings retain their existing verified action path. Crash scenario maps no longer advertise repairs forbidden by the review-only policy.

Production and registry tests share one 19-tool registration list. Documentation now records current snapshot/service behavior, desktop targeting and durable storage operation receipts.

Regression evidence, fresh full-suite results and package fingerprints accompany the frozen candidate. Fake service/process tests and read-only native API probes do not replace live installed acceptance on a disposable Windows environment. The splash from 0.2.17 is retained. No real system repair, service stop, desktop restart, uninstall or personal-file deletion was performed while preparing this candidate. Nothing was automatically staged, committed or deployed.
