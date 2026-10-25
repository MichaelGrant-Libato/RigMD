import { getReadingPresentation, type DiagnosticReading } from './diagnosticLanguage.ts';
import { readingPercent, readingTone } from './diagnosticReadingVisuals.ts';

const unassessedChecks: Record<string, { prefix: string; explanation: string }> = {
  battery_drain: { prefix: 'Charge is not capacity or drain rate.', explanation: 'This scan reads battery charge, but does not measure battery wear or how quickly it drains over time.' },
  startup_impact: { prefix: 'Startup entries and current workload do not measure', explanation: 'This scan does not time Windows startup or measure each startup app\'s impact.' },
  os_integrity: { prefix: 'Windows version and power plan do not verify system files.', explanation: 'This scan does not run a Windows system-file integrity test.' },
  audio_playback: { prefix: 'Device status does not test volume', explanation: 'Device status does not test what you can hear or record. This scan does not perform a sound test.' },
  peripheral_function: { prefix: 'Device status does not test cables', explanation: 'This scan does not physically test USB ports, cables or connected accessories.' },
  display_picture: { prefix: 'A detected monitor does not confirm', explanation: 'Detecting a screen does not confirm its picture, cable or panel. This scan does not perform a visual display test.' },
  fan_operation: { prefix: 'This scan does not measure fan speed', explanation: 'This scan does not measure fan speed, airflow or noise. A temperature reading alone cannot verify the cooling system.' },
};

export function getFindingDetails(reading: DiagnosticReading) {
  const view = getReadingPresentation(reading);
  const tone = readingTone(view.status);
  const capability = unassessedChecks[reading.metric_id ?? ''];
  const notAssessed = tone === 'unknown' && Boolean(capability && reading.meaning?.startsWith(capability.prefix));
  const statusLabel = { attention: 'Needs attention', unknown: notAssessed ? 'Not assessed' : 'Reading unavailable', normal: 'No warning in this reading', info: 'Information only' }[tone];
  const percent = tone === 'unknown' ? null : readingPercent(reading);
  const valueSummary = tone === 'unknown' ? statusLabel : percent === null ? (view.value.length > 80 ? statusLabel : view.value) : `${percent}%`;
  let explanation = view.meaning || 'This is a saved observation from the check, not a diagnosis of the cause.';
  let nextStep = 'Review the recorded evidence before making changes. Check again if the problem continues.';
  if (tone === 'unknown') {
    explanation = notAssessed ? `${capability.explanation} This does not mean the part has failed.` : 'RigMD could not obtain a verified reading. This does not tell us whether this part is working normally.';
    nextStep = 'If you notice a problem with this part, use the relevant Windows or manufacturer check. Another scan may not provide an unsupported reading.';
  } else if (reading.metric_id === 'memory_usage' || reading.metric_id === 'memory_app_warning') {
    explanation = tone === 'attention'
      ? reading.metric_id === 'memory_app_warning'
        ? 'Windows reported a memory problem for an app. This is not a test of the RAM hardware and does not establish the cause of another problem.'
        : 'Running apps are using a large share of memory (RAM). This can slow your computer, but does not prove that the RAM is damaged or establish the cause of another problem.'
      : tone === 'normal' ? 'Memory use was below the review level in this saved reading. This is not a test for damaged RAM.'
        : 'This saved memory reading did not include a verified warning status. It does not establish whether memory pressure was high.';
    nextStep = tone === 'attention'
      ? 'Save your work, then review apps you no longer need. Closing an app can lose unsaved work. Check memory use again afterward.'
      : tone === 'normal' ? 'No app-closing action is needed for this reading. If the computer is still slow, check again while the problem happens.'
        : 'Review the recorded evidence and take a new reading before deciding whether to close apps.';
  } else if (reading.metric_id === 'cpu_usage') {
    explanation = 'This shows how busy the processor was during the check. Activity alone does not tell us its temperature or prove a hardware fault.';
  } else if (reading.metric_id === 'storage_usage') {
    explanation = 'This is storage used by Windows, apps and saved files. It is different from the working memory used by running apps and does not test drive health.';
    if (tone === 'attention') nextStep = 'Review files you no longer need and back up important files before removing anything. Recheck the available storage afterward.';
  } else if (/crash_history$/.test(reading.metric_id ?? '')) {
    explanation = reading.window_kind === 'since_diagnosis'
      ? 'This reading covers Windows crash records after the original check. No new record does not prove that an intermittent crash is fixed.'
      : 'Interpret crash records together with their count and checked period. A record alone does not establish the cause or confirm an ongoing problem.';
    nextStep = 'Review the affected app, event time and error details in Windows Reliability Monitor if crashes continue.';
  }
  return { ...view, tone: tone as 'attention' | 'unknown' | 'normal' | 'info', notAssessed, statusLabel, valueSummary, explanation, nextStep };
}

export function getCoverageMessage(readings: DiagnosticReading[]) {
  const missing = readings.map(getFindingDetails).filter(row => row.tone === 'unknown');
  const unassessed = missing.filter(row => row.notAssessed).length;
  const unavailable = missing.length - unassessed;
  return [unassessed ? `${unassessed} check${unassessed === 1 ? '' : 's'} not assessed by this scan.` : '',
    unavailable ? `${unavailable} reading${unavailable === 1 ? '' : 's'} unavailable.` : ''].filter(Boolean).join(' ');
}
