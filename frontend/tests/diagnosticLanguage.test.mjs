import test from 'node:test';
import assert from 'node:assert/strict';
import { getDiagnosticOverview, friendlyToolName, getActionExplanation, friendlyVerification, getSnapshotReadings, getReadingPresentation, plainDiagnosticText, friendlyReadingStatus, friendlyActionCategory, getScopedCheckExplanation, proofShowsNoNewCrashes, getDiagnosticCoverageNotice, expandDiagnosticReadings } from '../src/lib/diagnosticLanguage.ts';

test('stable metric identity wins over ambiguous labels, and missing temperature never looks normal', () => {
  const graphics = getReadingPresentation({ metric_id: 'gpu_temperature', label: 'Storage and Drivers', value: 'Unavailable', status: 'normal' });
  assert.equal(graphics.label, 'Graphics temperature');
  assert.equal(graphics.status, 'unknown');
  const legacy = getReadingPresentation({ label: 'Graphics & Drivers', value: 'Intel graphics, GPU temperature unavailable', status: 'normal' });
  assert.doesNotMatch(legacy.label, /storage|drive health/i);
  assert.equal(legacy.status, 'unknown');
  assert.equal(getReadingPresentation({ metric_id: 'cpu_usage', label: 'Temperature', value: '22%', status: 'normal' }).label, 'Processor activity');
  assert.equal(plainDiagnosticText('Memory (RAM) is at 87.3%'), 'Memory is at 87.3%');
});

test('old combined CPU evidence is split without inventing a separate historical status', () => {
  const rows = expandDiagnosticReadings([{ label: 'Processor (CPU)', value: '22% load, CPU temperature unavailable', status: 'unknown' }]);
  assert.equal(rows.length, 2);
  assert.equal(rows[0].metric_id, 'cpu_usage');
  assert.equal(rows[0].value, '22%');
  assert.equal(rows[0].status, 'info');
  assert.equal(rows[1].value, 'Unavailable');
  assert.equal(rows[1].status, 'unknown');
  assert.equal(expandDiagnosticReadings([{ metric_id: 'cpu_usage', label: 'Processor', value: '22%' }]).length, 1);
});

test('no-new-crash labels require an explicit zero, never digits embedded in another count', () => {
  for (const value of ['10 found', '20', '100 events', '0.5 events', 'Unavailable', '0 of 12', 'No data'])
    assert.equal(proofShowsNoNewCrashes([{ label: 'Application crash events since diagnosis', value, status: 'normal now' }]), false, value);
  for (const value of ['0', '0 found', '0 events', 'None found'])
    assert.equal(proofShowsNoNewCrashes([{ label: 'Application crash events since diagnosis', value }]), true, value);
  assert.equal(proofShowsNoNewCrashes([{ label: 'Crash events', value: '0 found', status: 'unknown' }]), false);
  assert.equal(proofShowsNoNewCrashes([{ label: 'Crash events in the previous 48 hours', value: '0 found' }]), false);
});

test('partial checks disclose missing readings without inventing missing values', () => {
  assert.match(getDiagnosticCoverageNotice({ proof: [{ label: 'CPU temperature', value: 'Unavailable', status: 'unknown' }] }), /does not confirm/);
  assert.match(getDiagnosticCoverageNotice({ primary_result: 'CPU at 28%, CPU temperature unavailable.' }), /could not be checked/);
  assert.equal(getDiagnosticCoverageNotice({ proof: [{ label: 'CPU load', value: '28%', status: 'normal' }] }), '');
  assert.equal(getDiagnosticCoverageNotice({}), '');
});

test('plain wording removes duplicate processor terms and stray drive-health punctuation', () => {
  assert.equal(plainDiagnosticText('Processor (CPU) is normal. S.M.A.R.T. healthy.'), 'Processor is normal. drive-health healthy.');
  assert.equal(plainDiagnosticText('Processor (processor) at 28%'), 'Processor at 28%');
});

test('crash record presentation retains scope and time window; failures are not zero', () => {
  const readings = getSnapshotReadings('query_windows_event_logs', { eventFocus: 'ApplicationCrash', eventCount: 0, sinceUtc: '2026-01-01T00:00:00Z', hoursBack: 48 });
  assert.equal(readings[0].label, 'App crash or fault records');
  assert.equal(readings[0].value, '0');
  assert.match(readings[1].value, /^Since /);
  assert.equal(getSnapshotReadings('query_windows_event_logs', { eventCount: 12, hoursBack: 48 })[1].value, 'Previous 48 hours');
  assert.equal(getSnapshotReadings('query_windows_event_logs', { eventCount: 0, warning: 'Access denied' })[0].value, 'Not available');
  for (const count of [-1, 0.5, null]) assert.equal(getSnapshotReadings('query_windows_event_logs', { eventCount: count })[0].value, 'Not available');
  const reading = { label: 'Application crash events since diagnosis', value: '0 found', meaning: 'No new matching records since the saved diagnosis.' };
  assert.equal(getReadingPresentation(reading).label, reading.label);
  assert.equal(getReadingPresentation(reading).meaning, reading.meaning);
});

test('findings use plain language without claiming unavailable readings are healthy', () => {
  for (const category of ['High Memory Pressure', 'Elevated CPU Utilization', 'Network issue', 'Storage health behavior', 'Driver conflict', 'Thermal condition', 'Battery power condition', 'OS performance degradation', 'Boot and startup failure', 'Application crash history requires review', 'System crash or stop error requires review']) {
    const view = getDiagnosticOverview(category);
    assert.ok(view.title && view.meaning);
    assert.doesNotMatch(view.title, /ReAct|telemetry|remediation|utilization|Tier \d/);
  }
  assert.match(getDiagnosticOverview('Memory telemetry unavailable').meaning, /Missing readings do not mean everything is normal/);
  assert.match(getDiagnosticOverview('Application stability risk needs review').meaning, /does not confirm/);
  assert.match(getDiagnosticOverview('No Active Issue Detected').meaning, /areas checked/);
});

test('cleanup is described as storage recovery, never guaranteed RAM relief', () => {
  const copy = getActionExplanation('clear_temp_files');
  assert.match(copy.bytesLabel, /storage/);
  assert.match(copy.caution, /does not.*free.*working memory/);
  assert.match(getActionExplanation('close_selected_app').caution, /unsaved/);
  assert.equal(friendlyToolName('flush_dns_cache'), 'Refresh saved website addresses');
  assert.equal(friendlyToolName('unrecognized_machine_id'), 'Proposed action');
});

test('action completion is not presented as verified resolution', () => {
  assert.equal(friendlyVerification('Completed'), 'Action finished');
  assert.equal(friendlyVerification('Unknown'), 'Could not verify');
  assert.equal(friendlyVerification(0), 'Issue no longer detected');
  assert.equal(friendlyVerification('Unresolved'), 'Still needs attention');
});

test('actual tool payloads retain numbers and missing readings are not zero', () => {
  assert.equal(getSnapshotReadings('inspect_cpu_and_thermals', { usagePercent: 22, capturedAtUtc: '2026-10-18T00:41:00Z' })[0].captured_at, '2026-10-18T00:41:00Z');
  assert.equal(getSnapshotReadings('inspect_cpu_and_thermals', { usagePercent: 12.5, temperatureCelsius: null })[1].value, 'Not available');
  assert.equal(getSnapshotReadings('inspect_cpu_and_thermals', { temperatureCelsius: 0 })[1].value, 'Not available');
  assert.equal(getSnapshotReadings('inspect_full_device_profile', {})[0].value, 'Not available');
  assert.equal(getSnapshotReadings('inspect_network_connectivity', { liveProbe: { pingLatencyMs: 24 } })[0].value, '24 ms');
  assert.match(getSnapshotReadings('inspect_storage_health', { volumes: [{ drive: 'C:', usagePercent: 85, freeGb: 42 }] })[0].value, /85%.*42 GB/);
  assert.match(getSnapshotReadings('inspect_memory_and_processes', { memoryLeakWarning: 'An app needs review' })[2].value, /An app needs review/);
  assert.deepEqual(getSnapshotReadings('inspect_memory_and_processes', '{bad json'), []);
});

test('action previews retain measured warnings instead of showing neutral memory and CPU rows', () => {
  const memory = getSnapshotReadings('inspect_memory_and_processes', { memory: { usagePercent: 86.2 }, browserSummary: { browserMemoryMb: 1914.11 } });
  assert.equal(memory[0].metric_id, 'memory_usage');
  assert.equal(memory[0].status, 'high');
  assert.equal(memory[1].status, 'info');
  const cpu = getSnapshotReadings('inspect_cpu_and_thermals', { usagePercent: 100, temperatureCelsius: null });
  assert.equal(cpu[0].status, 'high');
  assert.equal(cpu[1].status, 'unknown');
  assert.equal(getSnapshotReadings('inspect_battery_and_power', { querySucceeded: false, hasBattery: null })[0].status, 'unknown');
});

test('presentation preserves evidence objects and exact numbers', () => {
  const reading = { label: 'Physical Memory (RAM)', value: '90.5% (14.2 / 15.7 GB)', meaning: 'Original evidence', status: 'high' };
  const original = structuredClone(reading);
  assert.equal(getReadingPresentation(reading).value, '90.5% (14.2 / 15.7 GB)');
  assert.deepEqual(reading, original);
  assert.match(getReadingPresentation({ ...reading, status: 'unknown' }).meaning, /not a normal or zero reading/);
  assert.match(plainDiagnosticText('CPU utilization: 80%. RAM: 82%.'), /processor use: 80%. memory: 82%./);
});

test('plain labels preserve warning severity and do not diagnose an absent part', () => {
  assert.equal(friendlyReadingStatus('critical'), 'Needs urgent attention');
  assert.equal(friendlyReadingStatus('unknown'), 'Could not be checked');
  assert.equal(friendlyReadingStatus('new_backend_status'), 'new_backend_status');
  assert.equal(friendlyActionCategory('Troubleshoot'), 'Review possible actions');
  assert.match(getDiagnosticOverview('Component Not Present').meaning, /does not.*mean a part is broken/);
  assert.match(getDiagnosticOverview('Storage health behavior').meaning, /Back up important files/);
  assert.equal(getReadingPresentation({ label: 'Browser memory', value: '4359 MB' }).label, 'Browser memory');
  assert.equal(getReadingPresentation({ label: 'Storage space D:', value: '85% used' }).label, 'Storage space D:');
  assert.equal(getReadingPresentation({ label: 'S.M.A.R.T.', value: 'Healthy', status: 'normal' }).label, 'Drive health');
  assert.match(getReadingPresentation({ label: 'Memory', value: '', status: 'unavailable' }).meaning, /not a normal or zero reading/);
});

test('a clear crash check never claims all performance readings are normal', () => {
  assert.match(getScopedCheckExplanation({ scenario_id: 'blue-screen-crash', diagnosed_category: 'No Active Issue Detected' }), /not overall performance/);
  assert.match(getScopedCheckExplanation({ diagnosis_mode: 'component', diagnosed_category: 'No Active Issue Detected' }), /Other parts.*may still need attention/);
  assert.equal(getScopedCheckExplanation({ scenario_id: 'blue-screen-crash', diagnosed_category: 'System crash or stop error requires review' }), '');
});
