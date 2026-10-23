import { REVIEW_THRESHOLDS, NETWORK_THRESHOLDS, percentReviewStatus } from './reviewThresholds.ts';
import { memoryWarningLabel } from './repairSupport.ts';

export interface DiagnosticReading {
  metric_id?: string;
  captured_at?: string | null;
  evidence_kind?: string;
  rule_version?: string;
  window_kind?: string | null;
  window_start?: string | null;
  window_end?: string | null;
  label?: string;
  value?: string;
  meaning?: string;
  status?: string;
}

export function expandDiagnosticReadings(readings: DiagnosticReading[]): DiagnosticReading[] {
  return readings.flatMap(reading => {
    if (reading.metric_id) return [reading];
    // Older saved checks combined workload and temperature; split only the known format.
    if (/^(?:processor \(cpu\)|processor activity)$/i.test(reading.label ?? '')) {
      const match = /^(\d+(?:\.\d+)?)%\s+load,\s*(?:(?:CPU|processor) temperature unavailable|Temp:\s*(\d+(?:\.\d+)?)\s*°C)$/i.exec(reading.value ?? '');
      if (match) return [
        { ...reading, metric_id: 'cpu_usage', label: 'Processor activity', value: `${match[1]}%`, status: 'info', meaning: 'Saved activity reading. Its separate workload status was not recorded.' },
        { ...reading, metric_id: 'cpu_temperature', label: 'Processor temperature', value: match[2] ? `${match[2]}°C` : 'Unavailable', status: match[2] ? 'info' : 'unknown' },
      ];
    }
    return [reading];
  });
}

export function proofShowsNoNewCrashes(proof?: DiagnosticReading[]) {
  return Boolean(proof?.some(reading => {
    const label = reading.label ?? '';
    const value = (reading.value ?? '').trim();
    const status = (reading.status ?? '').toLowerCase();
    return /crash/i.test(label) && /since|after|new/i.test(label) && !/unknown|unavailable|attention|active|high|elevated/.test(status) &&
      /^(?:0(?:\s+(?:found|events?|crashes?|records?))?|none(?:\s+found)?)$/i.test(value);
  }));
}

export function getDiagnosticCoverageNotice(check: { proof?: DiagnosticReading[]; primary_result?: string }) {
  const missing = check.proof?.some(reading => /unknown|unavailable/i.test(reading.status ?? '') ||
    /unavailable|could not be (?:read|checked)|not available/i.test(reading.value ?? ''));
  return missing || /temperature unavailable|temperature could not be checked/i.test(check.primary_result ?? '')
    ? 'Some readings could not be checked. Review the unavailable items in this result; a normal result for the available readings does not confirm that those items are normal.' : '';
}

export function plainDiagnosticText(value?: string | null): string {
  return (value ?? '')
    .replace(/\bprocessor\s*\(\s*(?:CPU|processor)\s*\)/gi, 'Processor')
    .replace(/\bmemory\s*\(\s*(?:RAM|memory)\s*\)/gi, 'Memory')
    .replace(/\btelemetry\b/gi, 'device readings')
    .replace(/\bremediation\b/gi, 'action')
    .replace(/\butilization\b/gi, 'use')
    .replace(/\bCPU\b/g, 'processor')
    .replace(/\bRAM\b/g, 'memory')
    .replace(/\bGPU\b/g, 'graphics')
    .replace(/\bDNS\b/g, 'website address lookup')
    .replace(/\bPnP\b/gi, 'device detection')
    .replace(/\bS\.M\.A\.R\.T\b\.?|\bSMART\b/g, 'drive-health')
    .replace(/\bTier 0\b/gi, 'read-only')
    .replace(/\bTier [12]\b/gi, 'approval-required');
}

export function getDiagnosticOverview(category?: string | null, readings: DiagnosticReading[] = []) {
  const text = (category ?? '').trim().toLowerCase();
  if (text.includes('application stability risk') && memoryWarningLabel(readings))
    return { title: memoryWarningLabel(readings)!, meaning: 'Review the memory warning and apps you no longer need. This finding does not confirm that an app crashed or identify a crash cause.' };
  if (!text) return { title: 'Result not available', meaning: 'There is not enough information to explain this result yet.' };
  if (text.includes('component not present'))
    return { title: 'This part was not detected', meaning: 'The selected part was not found in this check. For example, a desktop may not have a battery. Not being detected does not, by itself, mean a part is broken.' };
  if (text.includes('no active issue') || text.includes('no issue detected'))
    return { title: 'No warning in the checked readings', meaning: 'No warning was found in the measurements for the areas checked. This does not certify overall component health or rule out an intermittent fault.' };
  if (text.includes('unavailable') || text.includes('could not verify'))
    return { title: text.includes('thermal') ? 'Temperature could not be checked' : text.includes('memory') ? 'Memory readings could not be checked' : 'Some readings could not be checked',
      meaning: 'Missing readings do not mean everything is normal. This result needs another check when the required information is available.' };
  if (text.includes('storage health') || text.includes('smart'))
    return { title: 'A drive-health warning needs attention', meaning: 'Windows reported a storage-health warning. Back up important files before attempting repairs or further cleanup.' };
  if (text.includes('severe system resource'))
    return { title: 'Your computer is under heavy resource pressure', meaning: 'Several readings were high at the same time. Review the affected parts before choosing an action.' };
  if (text.includes('thermal') || text.includes('overheat'))
    return { title: 'Cooling needs attention', meaning: 'A temperature or heat-related warning was reported. Processor workload alone is not a temperature reading.' };
  if (text.includes('elevated cpu') || text.includes('cpu load'))
    return { title: 'The processor is very busy', meaning: 'Running tasks are using a large share of the processor. Other apps may respond more slowly while this continues.' };
  if (text.includes('memory') || text.includes('high ram'))
    return { title: 'Memory needs attention', meaning: 'Working memory is the space running apps use. The check found high memory use, a heavy app workload, or an application memory warning. This is different from space used by saved files.' };
  if (text.includes('application stability risk'))
    return { title: 'Apps may need a closer look', meaning: 'Current readings show risk signals that may affect apps. This does not confirm that an app has crashed.' };
  if (text.includes('application crash') || text.includes('app crash'))
    return { title: 'App crash information needs review', meaning: 'Review the recorded app errors and current readings together. An old error does not necessarily mean the problem is happening now.' };
  if (text.includes('system crash') || text.includes('stop error') || text.includes('blue screen'))
    return { title: 'Windows crash or restart information needs review', meaning: 'Windows error records or current risk signals need review. This result alone does not prove which part caused a restart or crash.' };
  if (text.includes('device status warning') || text.includes('driver') || text.includes('display') || text.includes('pnp') || text.includes('device manager'))
    return { title: 'A device or display needs attention', meaning: 'Windows reported device or display information that needs review. Check which device is affected before changing its software or settings.' };
  if (text.includes('network') || text.includes('internet') || text.includes('dns'))
    return { title: 'The connection needs attention', meaning: 'The connection check found a warning. The details can help distinguish a weak connection from a problem looking up website addresses.' };
  if (text.includes('storage') || text.includes('disk'))
    return { title: 'Storage space needs attention', meaning: 'The check found a storage-space concern. Storage holds Windows and your files; it is different from the working memory used by open apps.' };
  if (text.includes('boot') || text.includes('startup'))
    return { title: 'Windows startup needs a closer look', meaning: 'Resource readings may help explain slow startup. They do not, by themselves, confirm a Windows startup failure.' };
  if (text.includes('battery') || text.includes('power'))
    return { title: 'Battery or power use needs attention', meaning: 'Review the battery charge, power settings, and running apps. A power-use warning does not necessarily mean the battery is damaged.' };
  if (text.includes('performance') || text.includes('slow') || text.includes('freez') || text.includes('stutter'))
    return { title: 'Apps or Windows may be slowing down', meaning: 'The check found resource pressure or unusual app behavior that may affect responsiveness. Review the readings before deciding what to change.' };
  return { title: plainDiagnosticText(category), meaning: 'The recorded information needs review before a cause or next step can be confirmed.' };
}

const TOOL_LABELS: Record<string, string> = {
  clear_temp_files: 'Clear temporary files', clear_user_temp_files: 'Clear temporary files',
  clear_browser_cache: 'Remove saved website files', clear_windows_update_cache: 'Clear downloaded update files',
  flush_dns_cache: 'Refresh saved website addresses', flush_dns: 'Refresh saved website addresses',
  restart_windows_explorer: 'Restart the desktop and taskbar',
  rescan_plug_and_play_devices: 'Look for connected devices again',
  run_system_file_checker: 'Check and repair Windows files', run_disk_cleanup: 'Clean up storage space',
  terminate_processes: 'Close the apps selected by RigMD', close_selected_app: 'Choose apps to close',
  inspect_memory_and_processes: 'Memory and running apps', inspect_cpu_and_thermals: 'Processor and cooling',
  inspect_gpu_and_displays: 'Graphics and screens', inspect_full_device_profile: 'Windows devices',
  inspect_storage_health: 'Storage space and drive health', inspect_network_connectivity: 'Internet connection',
  inspect_battery_and_power: 'Battery and power', query_windows_event_logs: 'Windows error records',
};

export function friendlyToolName(id?: string, fallback?: string): string {
  return TOOL_LABELS[id ?? ''] || (fallback && !fallback.includes('_') ? plainDiagnosticText(fallback) : 'Proposed action');
}

export function getActionExplanation(id?: string) {
  const descriptions: Record<string, [string, string]> = {
    clear_temp_files: ['Remove temporary files that are not currently in use.', 'This frees storage space. It does not close apps or directly free their working memory.'],
    clear_user_temp_files: ['Remove temporary files that are not currently in use.', 'This frees storage space, not the working memory used by running apps.'],
    clear_browser_cache: ['Remove saved website files from browser storage.', 'Websites may need to download these files again. This is not the same as closing browser tabs.'],
    clear_windows_update_cache: ['Clear downloaded Windows Update files.', 'Windows may need to download update files again.'],
    flush_dns_cache: ['Ask Windows to look up website addresses again instead of using saved answers.', 'This may help address-lookup problems, but it does not guarantee that the internet connection will be restored.'],
    restart_windows_explorer: ['Restart the Windows desktop and taskbar.', 'The desktop or taskbar may briefly disappear. Save your work and review the warnings first.'],
    rescan_plug_and_play_devices: ['Ask Windows to look for connected devices again.', 'This does not guarantee that a faulty device or its software will be repaired.'],
    run_system_file_checker: ['Ask Windows to check its protected files and attempt repairs.', 'This can take time. Review any administrator or restart requirements before continuing.'],
    terminate_processes: ['Try to close the apps selected by RigMD.', 'Save your work first. Closing an app can lose unsaved changes.'],
    close_selected_app: ['Try to close only the apps you select.', 'Save your work first. Closing an app can lose unsaved changes.'],
  };
  const [description, caution] = descriptions[id ?? ''] ?? ['Review the proposed change before deciding whether to run it.', 'The exact targets and safety warnings below still apply.'];
  const diskCleanup = ['clear_temp_files', 'clear_user_temp_files', 'clear_browser_cache', 'clear_windows_update_cache', 'run_disk_cleanup'].includes(id ?? '');
  const closesApps = ['terminate_processes', 'close_selected_app'].includes(id ?? '');
  return { description, caution, itemsLabel: closesApps ? 'Apps or app parts affected' : diskCleanup ? 'Files affected' : 'Items affected',
    bytesLabel: diskCleanup ? 'Estimated storage space to recover' : closesApps ? 'Memory used by these apps' : 'Estimated size affected' };
}

export function friendlyVerification(value?: string | number | null): string {
  const labels = ['Issue no longer detected', 'Still needs attention', 'Needs more help', 'Could not verify'];
  if (typeof value === 'number') return labels[value] ?? 'Status not available';
  const text = (value ?? '').trim().toLowerCase();
  if (['unresolved', 'not fixed', 'still_active'].includes(text)) return labels[1];
  if (['unknown', 'could not confirm', 'needs_recheck'].includes(text)) return labels[3];
  if (['resolved', 'fixed'].includes(text)) return labels[0];
  if (['worse', 'needs help', 'escalated'].includes(text)) return labels[2];
  if (['completed', 'success'].includes(text)) return 'Action finished';
  if (['failed', 'rejected', 'safety rejected'].includes(text)) return 'Action could not finish';
  return plainDiagnosticText(value == null ? 'Not checked yet' : String(value));
}

export function friendlyActionCategory(value?: string | null) {
  const labels: Record<string, string> = { monitor: 'Watch for changes', troubleshoot: 'Review possible actions', maintain: 'Review cleanup options', escalate: 'Get more help' };
  return labels[(value ?? '').toLowerCase()] || plainDiagnosticText(value || 'Review this check');
}

export function friendlyConfidence(value?: string | null) {
  const text = (value ?? '').trim().toLowerCase();
  const labels: Record<string, string> = {
    available: 'Evidence available', partial: 'Evidence partly available', unavailable: 'Evidence unavailable',
    contextonly: 'Context only', notapplicable: 'Not applicable',
    high: 'Unvalidated rating', medium: 'Unvalidated rating', low: 'Unvalidated rating',
  };
  return labels[text] ?? 'Evidence availability not recorded';
}

export function readingEvidenceLabel(reading: DiagnosticReading) {
  if (/unknown|unavailable/i.test(reading.status ?? '') || /^(unavailable|not available|not verified)$/i.test(reading.value ?? '')) return 'Not verified';
  const labels: Record<string, string> = {
    measurement: 'Measured reading', event_log: 'Windows event-log evidence', reported_status: 'Reported device status',
    context: 'Context only', unavailable: 'Not verified',
  };
  return labels[reading.evidence_kind ?? ''] ?? 'Saved reading; evidence type not recorded';
}

export function getScopedCheckExplanation(check: { scenario_id?: string; diagnosis_mode?: string; diagnosed_category?: string }) {
  const clear = /no active issue|no issue detected/i.test(check.diagnosed_category ?? '');
  if (!clear) return '';
  if (check.scenario_id === 'blue-screen-crash' || check.scenario_id === 'system-crashes')
    return 'This result is about Windows crashes, not overall performance. A busy processor alone does not confirm a crash. The readings describe the time of this saved check, not necessarily what is happening now.';
  if (check.scenario_id === 'app-crashes')
    return 'This result is about app crashes. It does not mean every app or device reading is normal. A problem that comes and goes may need another check when it happens.';
  if (check.diagnosis_mode === 'component' || check.diagnosis_mode === 'scenario')
    return 'No problem was detected in the selected area during this check. Other parts of the computer may still need attention.';
  return '';
}

export function friendlyReadingStatus(value: string) {
  const labels: Record<string, string> = {
    normal: 'Within the expected range', healthy: 'Within the expected range', ok: 'Within the expected range',
    high: 'Needs attention', elevated: 'Needs attention', warning: 'Warning reported', critical: 'Needs urgent attention',
    unknown: 'Could not be checked', unavailable: 'Could not be checked',
    info: 'Information only',
  };
  return labels[value.toLowerCase()] || plainDiagnosticText(value);
}

export function readingStatusLabel(reading: DiagnosticReading) {
  const status = getReadingPresentation(reading).status ?? '';
  if (status === 'normal' && (/^(application|system)_crash_history$/.test(reading.metric_id ?? '') || /crash.*(?:records|events)/i.test(reading.label ?? '')))
    return 'No matching records in this period';
  if (status === 'normal' && ['cpu_usage', 'memory_usage', 'storage_usage', 'cpu_temperature', 'gpu_temperature'].includes(reading.metric_id ?? ''))
    return 'Below the review threshold';
  return friendlyReadingStatus(status);
}

export function getReadingPresentation(reading: DiagnosticReading) {
  const metricLabels: Record<string, [string, string]> = {
    cpu_usage: ['Processor activity', 'Shows how busy the processor was when sampled. Activity is not a temperature reading.'],
    cpu_temperature: ['Processor temperature', 'A separate processor sensor reading. Missing temperature does not mean cool or overheating.'],
    cpu_throttling: ['Heat-related slowdown report', 'A reported slowdown is separate evidence from processor activity.'],
    memory_usage: ['Working memory', 'Memory used by running apps, not storage used by saved files.'],
    storage_usage: ['Storage space', 'Space used by Windows and saved files. This is not a drive-health test.'],
    storage_health: ['Drive-health warnings', 'No reported warning does not guarantee that a drive is healthy.'],
    graphics_devices: ['Windows device-status check', 'Windows device-error information, limited to the selected device families. This does not verify functional health.'],
    graphics_context: ['Graphics hardware', 'Hardware information only, not a temperature measurement.'],
    gpu_temperature: ['Graphics temperature', 'A separate graphics sensor reading. Missing temperature cannot be rated normal.'],
  };
  const metric = metricLabels[reading.metric_id ?? ''];
  const unavailable = /^(?:unavailable|not available)$/i.test((reading.value ?? '').trim());
  const status = unavailable ? 'unknown' : reading.evidence_kind === 'context' && reading.status === 'normal' ? 'info' : reading.status;
  if (metric) return { label: metric[0], value: plainDiagnosticText(reading.value || 'Not available'),
    meaning: status === 'unknown' ? `${plainDiagnosticText(reading.meaning)} This measurement was not available. It is not zero or a normal reading.`.trim() : plainDiagnosticText(reading.meaning) || metric[1], status };
  const label = (reading.label ?? '').toLowerCase();
  const isDriveHealth = label.includes('health') || label.includes('smart') || label.includes('s.m.a.r.t');
  let title = plainDiagnosticText(reading.label || 'Reading');
  let meaning = plainDiagnosticText(reading.meaning);
  if (label.includes('browser')) {
    title = 'Browser memory'; meaning = 'Tabs and background parts of a browser share this working memory. The number of parts is not the number of open apps.';
  } else if (label.includes('memory') || label.includes('ram')) {
    title = label.includes('warning') ? 'App memory warning' : 'Working memory';
    meaning = label.includes('warning') ? 'Shows any app memory warning reported during this check. Review a reported warning before choosing an action.' : 'This is memory used by running apps, not space occupied by saved files.';
  } else if (label.includes('cpu') || label.includes('processor')) {
    title = label.includes('temperature') ? 'Processor temperature' : label.includes('throttl') ? 'Heat-related slowdown report' : 'Processor activity';
    meaning = label.includes('temperature') ? 'A temperature sensor reading, when available. Processor activity alone does not measure heat.' : label.includes('throttl') ? 'Shows whether heat-related slowing was reported; no report does not prove the processor is cool.' : 'Shows how busy the processor was during this check. A busy reading alone does not prove overheating.';
  } else if (label.includes('packet loss')) {
    title = 'Connection test data lost'; meaning = 'Shows how much test data did not arrive. Missing results do not mean zero loss.';
  } else if (label.includes('ping') || label.includes('latency')) {
    title = 'Connection response time'; meaning = 'How long a connection test took to receive a reply. A higher number means a longer delay.';
  } else if (label.includes('graphics') || /\bgpu\b/.test(label)) {
    title = label.includes('temperature') ? 'Graphics temperature' : 'Graphics and device information';
    meaning = /temperature unavailable/i.test(reading.value ?? '')
      ? 'Graphics temperature was not available. Hardware or device information does not confirm normal temperature.'
      : plainDiagnosticText(reading.meaning) || 'Graphics information from this check.';
  } else if (label.includes('storage') || label.includes('disk') || /\bdrives?\b/.test(label) || label.includes('smart') || label.includes('s.m.a.r.t')) {
    const drive = /\b[a-z]:/i.exec(reading.label ?? '')?.[0];
    title = isDriveHealth ? `Drive health${drive ? ` ${drive}` : ''}` : drive ? plainDiagnosticText(reading.label) : 'Storage space';
    meaning = isDriveHealth ? 'Information reported about drive health. If a warning is shown, back up important files before attempting repairs.' : 'Space used by Windows and saved files. This is separate from the working memory used by apps.';
  } else if (label.includes('crash') || label.includes('events')) {
    title = plainDiagnosticText(reading.label || 'Recorded Windows errors');
    meaning = plainDiagnosticText(reading.meaning) || 'These are recorded events, not a guarantee that the same problem is happening now.';
  } else if (label.includes('driver') || label.includes('device manager')) {
    title = 'Windows device warnings'; meaning = 'Windows reports these warnings for connected devices. Review the affected device before making changes.';
  }
  if (/unknown|unavailable/i.test(reading.status ?? '')) meaning = `${plainDiagnosticText(reading.meaning)} This information could not be verified. It is not a normal or zero reading.`.trim();
  const legacyMissingTemperature = /temperature unavailable/i.test(reading.value ?? '');
  return { label: title, value: plainDiagnosticText(reading.value || 'Not available'), meaning,
    status: legacyMissingTemperature ? 'unknown' : status };
}

type Data = Record<string, unknown>;
function record(value: unknown): Data | null { return value && typeof value === 'object' && !Array.isArray(value) ? value as Data : null; }
function number(source: Data | null, key: string): number | null { const value = source?.[key]; return typeof value === 'number' && Number.isFinite(value) ? value : null; }
function metric(value: number | null, unit = '') { return value === null ? 'Not available' : `${Number(value.toFixed(2))}${unit}`; }

export function getSnapshotReadings(tool: string, input: unknown): DiagnosticReading[] {
  let data = input;
  if (typeof data === 'string') { try { data = JSON.parse(data); } catch { return []; } }
  const source = record(data);
  const captured = typeof source?.capturedAtUtc === 'string' && Number.isFinite(Date.parse(source.capturedAtUtc))
    ? source.capturedAtUtc : undefined;
  return readSnapshotReadings(tool, data).map(reading => ({ ...reading, captured_at: captured,
    rule_version: 'windows-review-v2',
    evidence_kind: reading.status === 'unknown' || /^(unavailable|not available|not verified)$/i.test(reading.value ?? '') ? 'unavailable'
      : reading.metric_id === 'memory_app_warning' ? 'reported_status'
      : tool === 'query_windows_event_logs' && reading.status ? 'event_log'
      : reading.metric_id ? 'measurement' : 'context',
  }));
}

function readSnapshotReadings(tool: string, input: unknown): DiagnosticReading[] {
  let data = input;
  if (typeof data === 'string') { try { data = JSON.parse(data); } catch { return []; } }
  const source = record(data);
  if (!source) return [];
  if (tool === 'inspect_memory_and_processes') {
    const ram = record(source.memory); const browser = record(source.browserSummary);
    const rawUsage = number(ram, 'usagePercent');
    const usage = rawUsage !== null && rawUsage >= 0 && rawUsage <= 100 ? rawUsage : null;
    const rawBrowserMb = number(browser, 'browserMemoryMb');
    const browserMb = rawBrowserMb !== null && rawBrowserMb >= 0 ? rawBrowserMb : null;
    const readings: DiagnosticReading[] = [
      { metric_id: 'memory_usage', label: 'Memory use', value: metric(usage, '%'), status: percentReviewStatus(usage, REVIEW_THRESHOLDS.memory) },
      { label: 'Browser workload', value: metric(browserMb, ' MB'), status: browserMb === null ? 'unknown' : 'info' },
    ];
    if (typeof source.memoryLeakWarning === 'string' && source.memoryLeakWarning.trim())
      readings.push({ metric_id: 'memory_app_warning', label: 'App memory warning', value: source.memoryLeakWarning, status: 'high', meaning: 'A process reading for review, not proof of a memory leak.' });
    return readings;
  }
  if (tool === 'inspect_cpu_and_thermals') {
    const cpu = record(source.cpu) ?? source;
    const rawUsage = number(cpu, 'usagePercent'); const rawTemp = number(cpu, 'temperatureCelsius');
    const usage = rawUsage !== null && rawUsage >= 0 && rawUsage <= 100 ? rawUsage : null;
    const temp = rawTemp !== null && rawTemp > 0 && rawTemp <= 125 ? rawTemp : null;
    return [{ metric_id: 'cpu_usage', label: 'Processor activity', value: metric(usage, '%'), status: percentReviewStatus(usage, REVIEW_THRESHOLDS.cpu) },
      { metric_id: 'cpu_temperature', label: 'Processor temperature', value: metric(temp, '\u00b0C'), status: temp === null ? 'unknown' : temp >= REVIEW_THRESHOLDS.temperature ? 'high' : 'normal' }];
  }
  if (tool === 'inspect_storage_health') {
    const volumes = Array.isArray(source.volumes) ? source.volumes : [];
    const readings = volumes.map(record).filter((v): v is Data => Boolean(v)).map(v => {
      const raw = number(v, 'usagePercent');
      const usage = raw !== null && raw >= 0 && raw <= 100 ? raw : null;
      const free = number(v, 'freeGb');
      return { metric_id: `storage_usage_${typeof v.drive === 'string' ? v.drive : 'unidentified'}`,
        label: `Storage space ${typeof v.drive === 'string' ? v.drive : ''}`.trim(),
        value: `${metric(usage, '%')} used; ${metric(free !== null && free >= 0 ? free : null, ' GB')} free`,
        status: percentReviewStatus(usage, REVIEW_THRESHOLDS.storage) };
    });
    return readings.length ? readings : [{ metric_id: 'storage_usage', label: 'Storage space', value: 'Not available', status: 'unknown' }];
  }
  if (tool === 'inspect_network_connectivity') {
    const probe = record(source.liveProbe);
    const ping = number(probe, 'pingLatencyMs');
    const loss = number(source, 'packetLossPercent');
    const validPing = ping !== null && ping >= 0;
    const validLoss = loss !== null && loss >= 0 && loss <= 100;
    return [{ metric_id: 'network_latency', label: 'Connection response time', value: metric(validPing ? ping : null, ' ms'), status: !validPing ? 'unknown' : ping >= NETWORK_THRESHOLDS.latencyMs ? 'high' : 'normal' },
      { metric_id: 'network_loss', label: 'Connection test data lost', value: metric(validLoss ? loss : null, '%'), status: !validLoss ? 'unknown' : loss >= NETWORK_THRESHOLDS.lossPercent ? 'high' : 'normal' },
      { metric_id: 'network_dns', label: 'Website address lookup', value: typeof probe?.dnsResolutionSucceeded === 'boolean' ? probe.dnsResolutionSucceeded ? 'Test passed' : 'Test did not pass' : 'Not available',
        status: typeof probe?.dnsResolutionSucceeded !== 'boolean' ? 'unknown' : probe.dnsResolutionSucceeded ? 'normal' : 'high' }];
  }
  if (tool === 'inspect_battery_and_power') {
    if (source.querySucceeded !== true || typeof source.hasBattery !== 'boolean') return [{ label: 'Battery detection', value: 'Not available', status: 'unknown' }];
    if (source.hasBattery === false) return [{ label: 'Battery', value: 'No battery detected', status: 'info' }];
    const charge = number(record(source.battery), 'chargePercent');
    const valid = charge !== null && charge >= 0 && charge <= 100;
    return [{ metric_id: 'battery_charge', label: 'Battery charge', value: metric(valid ? charge : null, '%'), status: !valid ? 'unknown' : charge <= 20 ? 'high' : 'normal' }];
  }
  if (tool === 'inspect_full_device_profile' || tool === 'inspect_gpu_and_displays')
    return [{ label: 'Windows device warnings', value: tool === 'inspect_full_device_profile' ? metric(number(source, 'deviceErrorsCount')) : 'Not included in this check' },
      { label: 'Connected screens', value: metric(number(source, tool === 'inspect_full_device_profile' ? 'connectedDisplays' : 'connectedDisplaysCount')) }];
  if (tool === 'query_windows_event_logs') {
    const warning = typeof source.warning === 'string' ? source.warning.trim() : '';
    const count = number(source, 'eventCount');
    const valid = !warning && count !== null && Number.isInteger(count) && count >= 0;
    const label = source.eventFocus === 'ApplicationCrash' ? 'App crash or fault records'
      : source.eventFocus === 'BugCheck' ? 'Windows crash or restart records' : 'Recorded Windows errors';
    const since = typeof source.sinceUtc === 'string' ? source.sinceUtc : '';
    const hours = number(source, 'hoursBack');
    return [{ label, value: valid ? String(count) : 'Not available', status: valid ? count > 0 ? 'high' : 'normal' : 'unknown' },
      { label: 'Records checked', value: since && Number.isFinite(Date.parse(since))
        ? `Since ${new Date(since).toLocaleString()}` : hours !== null && hours > 0 ? `Previous ${hours} hours` : 'Time window not recorded' },
      ...(warning ? [{ label: 'Error-record check warning', value: warning, status: 'unknown' }] : [])];
  }
  return [];
}
