import { memoryWarningLabel } from './repairSupport.ts';
import type { DiagnosticReading } from './diagnosticLanguage.ts';

export function getActionDecision(tool: string, readings: DiagnosticReading[] = []) {
  const closesApps = tool === 'close_selected_app' || tool === 'terminate_processes';
  if (closesApps) return {
    why: memoryWarningLabel(readings)
      ? 'This check includes a memory warning. Closing apps you no longer need may reduce memory use. It does not test or repair the memory hardware.'
      : 'Review the recorded findings before closing apps. App memory use alone does not prove that an app or the memory hardware is faulty.',
    check: 'RigMD checks whether matching app processes remain, then requests a separate memory recheck. An app may stay in the background. Lower memory use does not prove a repair or that closing the app caused the change.',
  };
  const decisions: Record<string, { why: string; check: string }> = {
    clear_temp_files: { why: 'Removing unused temporary files may help when storage space is low. It does not directly free the working memory used by apps.', check: 'Review the storage space recovered, then check the original warning again. Files removed is not proof that a device problem is fixed.' },
    clear_browser_cache: { why: 'Saved website files can sometimes contribute to website problems. Clearing them is not the same as closing tabs or repairing the browser.', check: 'Try the affected website again and review the original warning. Removing cached files alone does not confirm the problem is fixed.' },
    clear_windows_update_cache: { why: 'This removes downloaded update files for Windows to fetch again. It is relevant only to the supported update problem being investigated.', check: 'Review the action result, then check Windows Update again. Clearing files is not proof that an update installed successfully.' },
    flush_dns_cache: { why: 'Refreshing saved website-address answers may help with an address-lookup problem. It does not repair Wi-Fi, cables or every internet problem.', check: 'Check website-address lookup and the affected website again. A completed refresh alone does not prove that internet access was restored.' },
    restart_windows_explorer: { why: 'Restarting the Windows desktop process may help with an unresponsive taskbar or desktop. It is not a general hardware repair.', check: 'Check whether the taskbar and desktop respond again, then review the original warning. Restarting the process alone does not establish the cause.' },
    rescan_plug_and_play_devices: { why: 'Asking Windows to detect connected devices again may help it recognize a device. Detection does not test cables or physical function.', check: 'Review the new Windows device status and test the affected device. A detected device is not proof that it works correctly.' },
    run_system_file_checker: { why: 'This checks protected Windows files. A crash or device warning alone does not confirm those files are damaged.', check: 'Review the Windows file-check result and any restart message. A separate device recheck cannot prove that every file was repaired.' },
  };
  if (tool === 'clear_user_temp_files' || tool === 'run_disk_cleanup') return decisions.clear_temp_files;
  return decisions[tool] ?? {
    why: 'Review the supporting finding and exact proposed change before deciding. A proposed action is not a confirmed repair.',
    check: 'Review the action result and compare new readings with the original warning. Missing measurements remain unverified.',
  };
}
