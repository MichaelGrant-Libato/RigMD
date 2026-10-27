import {
  AppWindow, Battery, Computer, Cpu, FileCheck, Globe, HardDrive,
  History, Info, MemoryStick, Monitor, Power, Repeat2, ScrollText,
  ShieldCheck, Trash2, Usb, Wifi, Wrench,
  type LucideIcon,
} from 'lucide-react';
import type { RegisteredAgentTool } from '../services/autonomyService';

type ToolGuide = { title: string; summary: string; note?: string; icon: LucideIcon; history?: boolean };

const guides: Record<string, ToolGuide> = {
  inspect_cpu_and_thermals: {
    title: 'Processor activity and temperature', icon: Cpu,
    summary: 'Checks how busy your processor is and reads its temperature when a supported sensor is available.',
    note: 'Missing temperatures stay marked as unavailable.',
  },
  inspect_memory_and_processes: {
    title: 'Memory use and running apps', icon: MemoryStick,
    summary: 'Shows how much working memory is in use and which apps are using the most.',
    note: 'This check does not close apps or test whether the memory hardware is damaged.',
  },
  inspect_storage_health: {
    title: 'Storage space and drive warnings', icon: HardDrive,
    summary: 'Checks available drive space, reported drive warnings, and the size of temporary files.',
    note: 'This is not a full drive-health test. Some temporary files may not be removable.',
  },
  inspect_gpu_and_displays: {
    title: 'Graphics and connected screens', icon: Monitor,
    summary: 'Reads information about your graphics hardware and detected screens, including temperatures when available.',
    note: 'This does not test the picture on your screen or put the graphics hardware under load.',
  },
  inspect_network_connectivity: {
    title: 'Network and internet connection', icon: Wifi,
    summary: 'Checks your connection settings, Wi-Fi signal, website address lookup, and replies from a test address.',
    note: 'A successful reply does not mean every website or service is reachable.',
  },
  inspect_battery_and_power: {
    title: 'Battery charge and power settings', icon: Battery,
    summary: 'Reads the available battery charge, charging status, and Windows power settings.',
    note: 'Charge level does not measure battery wear or how long it will last.',
  },
  query_windows_event_logs: {
    title: 'Recent Windows error records', icon: ScrollText,
    summary: 'Looks for recent error and warning records that may help explain crashes or other problems.',
    note: 'A recorded error does not confirm what caused the problem.',
  },
  query_startup_apps: {
    title: 'Apps that start with Windows', icon: Power,
    summary: 'Lists apps and commands set to open when Windows starts.',
    note: 'This does not disable apps or measure how much each app slows startup.',
  },
  inspect_full_device_profile: {
    title: 'Computer information', icon: Computer,
    summary: 'Reads your Windows version, hardware information, available readings, and reported device warnings.',
    note: 'This does not test every part or confirm overall computer health.',
  },
  query_past_checks_and_remediations: {
    title: 'Past checks and actions', icon: History, history: true,
    summary: 'Reads saved checks and action results from this PC so you can review what happened before.',
    note: 'An action finishing does not by itself confirm a repair.',
  },
  query_recurring_problems_and_warnings: {
    title: 'Problems reported more than once', icon: Repeat2, history: true,
    summary: 'Looks for repeated warnings and reported symptoms in your saved checks.',
    note: 'Repeated warnings do not prove separate failures or a damaged part.',
  },
  terminate_processes: {
    title: 'Close apps you choose', icon: AppWindow,
    summary: 'Tries to close reviewed apps normally to reduce their memory use.',
    note: 'Save your work first. Closing apps can lose unsaved work; some may keep running. Protected system apps are excluded.',
  },
  clear_temp_files: {
    title: 'Remove unused temporary files', icon: Trash2,
    summary: 'Removes temporary files that are not in use to free storage space.',
    note: 'This removes files. It does not free the working memory used by running apps.',
  },
  clear_browser_cache: {
    title: 'Clear temporary browser files', icon: Globe,
    summary: 'Removes saved copies of web files from supported browsers to free storage space.',
    note: 'Pages may load more slowly at first. Passwords, bookmarks, cookies, and browsing history are not targeted.',
  },
  flush_dns_cache: {
    title: 'Refresh saved website addresses', icon: Wifi,
    summary: 'Clears the website addresses Windows remembers so it can look them up again.',
    note: 'This may help with an outdated address. It does not guarantee an internet connection.',
  },
  restart_windows_explorer: {
    title: 'Restart the taskbar and desktop', icon: Monitor,
    summary: 'Restarts the Windows app that runs the desktop, taskbar, and File Explorer.',
    note: 'The taskbar may disappear briefly and File Explorer windows may close.',
  },
  clear_windows_update_cache: {
    title: 'Clear downloaded Windows update files', icon: Wrench,
    summary: 'Clears downloaded update files, which may help when Windows updates are stuck.',
    note: 'Administrator permission is required. The update service pauses during cleanup; Windows may download the files again.',
  },
  run_system_file_checker: {
    title: 'Check and repair Windows system files', icon: FileCheck,
    summary: 'Runs the built-in Windows check that can repair damaged protected system files.',
    note: 'Administrator permission is required. This can take time and does not repair physical hardware.',
  },
  rescan_plug_and_play_devices: {
    title: 'Look again for connected devices', icon: Usb,
    summary: 'Asks Windows to look again for connected devices that may not have been detected.',
    note: 'This does not install drivers or confirm that a device works correctly.',
  },
};

const groups = [
  { key: 'computer', title: 'Check your computer', description: 'Read information from this PC without changing its settings.', icon: Computer },
  { key: 'history', title: 'Review saved history', description: 'Look back at earlier checks and actions stored on this PC.', icon: History },
  { key: 'actions', title: 'Actions with your approval', description: 'When relevant to a finding, RigMD shows what will change and asks you to approve before running an action.', icon: ShieldCheck },
] as const;

export default function AvailableTools({ tools }: { tools: RegisteredAgentTool[] }) {
  return (
    <section aria-labelledby="available-tools-title" className="rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-5 sm:p-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="available-tools-title" className="flex items-center gap-3 text-lg font-bold text-white">
          <Wrench size={22} aria-hidden="true" className="text-cyan-300" /> What RigMD can do
        </h2>
        <span className="rounded-full border border-white/15 px-3 py-1 text-xs text-slate-300">{tools.length} tools</span>
      </div>
      <p className="mt-3 max-w-3xl text-sm leading-relaxed text-slate-300">
        RigMD uses these tools during checks and guided actions. This list is for information; opening details does not run a tool.
      </p>
      <div className="mt-5 flex items-start gap-3 rounded-xl border border-cyan-400/20 bg-cyan-400/5 p-4 text-sm leading-relaxed text-slate-300">
        <Info size={18} aria-hidden="true" className="mt-0.5 shrink-0 text-cyan-300" />
        <p>Start in <strong className="font-semibold text-white">Check My Device</strong>. Review the findings, then choose a recommended next step. You review and approve any action that changes your PC.</p>
      </div>
      {tools.length === 0 && <p className="mt-6 text-sm text-slate-400">No tools were listed by the app. Try loading Settings again.</p>}
      {groups.map((group) => {
        const items = tools.filter((tool) => {
          const key = tool.safetyTier !== 'Tier0_ReadOnly' ? 'actions' : guides[tool.name]?.history ? 'history' : 'computer';
          return key === group.key;
        });
        if (!items.length) return null;
        const GroupIcon = group.icon;
        return (
          <section key={group.key} aria-labelledby={`tools-${group.key}`} className="mt-7 border-t border-white/10 pt-6">
            <h3 id={`tools-${group.key}`} className="flex items-center gap-2 text-base font-semibold text-white">
              <GroupIcon size={18} aria-hidden="true" className="text-cyan-300" />{group.title}
              <span className="text-sm font-normal text-slate-400">({items.length})</span>
            </h3>
            <p className="mt-2 text-sm leading-relaxed text-slate-400">{group.description}</p>
            <div className="mt-4 grid grid-cols-1 items-start gap-4 md:grid-cols-2">
              {items.map((tool) => {
                const guide = guides[tool.name];
                const Icon = guide?.icon ?? Wrench;
                const readOnly = tool.safetyTier === 'Tier0_ReadOnly';
                return (
                  <article key={tool.name} className="min-w-0 rounded-xl border border-white/10 bg-[#0b1219] p-4 sm:p-5">
                    <div className="flex flex-wrap items-center gap-2">
                      <Icon size={20} aria-hidden="true" className="shrink-0 text-cyan-300" />
                      <h4 className="min-w-0 flex-1 text-sm font-semibold text-white">{guide?.title ?? 'Additional tool'}</h4>
                      <span className={`rounded-full border px-2 py-1 text-[11px] font-semibold ${readOnly ? 'border-slate-500/35 bg-slate-500/10 text-slate-300' : 'border-amber-400/30 bg-amber-400/10 text-amber-200'}`}>
                        {readOnly ? 'Read only' : 'Approval required'}
                      </span>
                    </div>
                    <p className="mt-3 text-sm leading-relaxed text-slate-300">{guide?.summary ?? 'See Technical details for the information supplied by this tool.'}</p>
                    {guide?.note && <p className="mt-2 text-xs leading-relaxed text-slate-400">{guide.note}</p>}
                    <details className="mt-4 border-t border-white/10 pt-3 text-xs">
                      <summary className="w-fit cursor-pointer rounded text-slate-400 transition hover:text-cyan-200 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-cyan-300">Technical details</summary>
                      <dl className="mt-3 space-y-3 break-words text-slate-400">
                        <div><dt className="font-semibold text-slate-300">Tool name</dt><dd className="mt-1">{tool.displayName}</dd></div>
                        <div><dt className="font-semibold text-slate-300">Internal identifier</dt><dd className="mt-1 break-all font-mono">{tool.name}</dd></div>
                        <div><dt className="font-semibold text-slate-300">Safety category</dt><dd className="mt-1 break-all">{tool.safetyTier}</dd></div>
                        <div><dt className="font-semibold text-slate-300">Full description</dt><dd className="mt-1 leading-relaxed">{tool.description}</dd></div>
                      </dl>
                    </details>
                  </article>
                );
              })}
            </div>
          </section>
        );
      })}
    </section>
  );
}
