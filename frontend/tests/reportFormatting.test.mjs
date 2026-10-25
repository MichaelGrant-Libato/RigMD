import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { buildReportText, sortSessionsNewestFirst } from '../src/lib/reportFormatting.ts';

const time = '2026-10-23T12:35:29Z';
const missingLabels = ['Processor temperature', 'Graphics temperature', 'Startup duration and app impact', 'Battery wear and drain rate', 'Windows file integrity', 'Sound playback and recording', 'USB and peripheral function', 'Picture and display connection', 'Fan operation and airflow'];
const original = { session_id: 'full-id', session_code: 'S-100', created_at: time, diagnosis_mode: 'full',
  diagnosed_category: 'High Memory Pressure', action_category: 'Troubleshoot', confidence_label: 'Partial', resolution_status: 'open',
  proof: [{ metric_id: 'memory_usage', label: 'Working memory', value: '88.1% (13.8 / 15.7 GB)', status: 'high', evidence_kind: 'measurement', captured_at: time },
    ...missingLabels.map(label => ({ label, status: 'unknown', value: 'Unavailable', captured_at: time, evidence_kind: 'unavailable' }))] };
const volume = { drive: 'C:\\', mountpoint: 'C:\\', fstype: 'NTFS', disk_index: 0, total_gb: 475.3, used_gb: 353.7, usage_percent: 74.4 };
const stats = { device_name: 'Test Laptop', os_version: 'Windows 11', ram: { total_gb: 15.7, usage_percent: 87 },
  cpu: { name: 'Test CPU' }, gpu: { name: 'Test GPU' }, all_disks: [volume],
  storage_drives: [{ model: 'Test NVMe', type: 'NVMe SSD', disk_index: 0, size_gb: 476.9, used_gb: 353.7, usage_percent: 74.4, status: 'OK', volumes: [volume] }] };
const dashboard = { server_time: time, totals: { total_sessions: 100, this_month_count: 95, escalated_count: 0 }, recurring_issues_count: 6, warning_signs_active_count: 0 };
const report = (overrides = {}) => buildReportText({ stats, dashboard, hardwareUpdatedAt: new Date('2026-10-23T12:36:00Z'), sessions: [original], historyUnavailable: false, generatedAt: new Date(time), ...overrides });

test('new network warnings use the same evidence-based label in the selected check and history', () => {
  const network = { ...original, session_id: 'network-id', session_code: 'S-104', diagnosis_mode: 'component', component_ids: '["network"]',
    diagnosed_category: 'No active issue detected', action_category: 'Monitor', resolution_status: 'still_active', resolution_checked_at: time,
    resolution_summary: 'Still active.', proof: [
      { metric_id: 'network_gateway', label: 'Default IPv4 gateway', value: 'Reported', status: 'normal' },
      { metric_id: 'network_latency', label: 'Connection response time', value: '15 ms', status: 'normal' }],
    resolution_proof: [
      { metric_id: 'network_gateway', label: 'Default IPv4 gateway', value: 'Not reported', status: 'high', meaning: 'Selected adapter: vEthernet (WSL). No default IPv4 gateway was reported.' },
      { metric_id: 'network_latency', label: 'Connection response time', value: 'Unavailable', status: 'unknown' }] };
  const text = report({ sessions: [network], selectedSession: network });
  assert.match(text, /Latest follow-up\s+: New warnings detected/);
  assert.match(text, /Follow-up\s+: New warnings detected/);
  assert.match(text, /vEthernet \(WSL\)/);
  assert.match(text, /Could not verify: Connection response time/);
  assert.doesNotMatch(text, /Still active/i);
  assert.ok(text.includes('15 ms'));
});

test('full-system scope is distinct from its highest finding', () => {
  const text = report();
  assert.match(text, /Selected check\s+: Full device check/);
  assert.match(text, /Original finding\s+: Memory needs attention/);
  assert.doesNotMatch(text, /Selected check\s+: Memory needs attention/);
});

test('all nine unavailable checks and the original memory reading are preserved', () => {
  const text = report();
  assert.match(text, /Readings needing review\s+: 1/);
  assert.match(text, /Unavailable readings\s+: 9/);
  for (const label of missingLabels) assert.ok(text.includes(label));
  assert.ok(text.includes('88.1% (13.8 / 15.7 GB)'));
  assert.match(text, /unverified, not confirmed hardware faults/);
});

test('current hardware and saved readings have separate timestamps and values', () => {
  const text = report();
  assert.match(text, /Hardware snapshot time/);
  assert.match(text, /separate snapshot, not replacements/);
  assert.match(text, /87.0% in use/);
  assert.match(text, /88.1%/);
  assert.match(text, /Recorded\s+: (?!Not available)/);
});

test('physical capacity never supplies the denominator for file-free space', () => {
  const text = report();
  assert.match(text, /Physical storage capacity\s+: 476.9 GB/);
  assert.match(text, /353.7 GB used \/ 121.6 GB free \(475.3 GB mounted capacity; 74.4% in use\)/);
  assert.doesNotMatch(text, /123.2 GB free|123.3 GB free|74.2%/);
  assert.match(text, /Reported drive status\s+: OK/);
  assert.doesNotMatch(text, /Status \(S.M.A.R.T.\)/);
});

test('missing usage stays unavailable, while a measured empty volume remains zero', () => {
  const unknown = report({ stats: { ...stats, all_disks: [], storage_drives: [{ ...stats.storage_drives[0], volumes: [], used_gb: null }] } });
  assert.match(unknown, /Mounted-volume usage\s+: Not available/);
  assert.doesNotMatch(unknown, / GB free/);
  const empty = { ...volume, used_gb: 0, usage_percent: 0 };
  const text = report({ stats: { ...stats, all_disks: [empty], storage_drives: [{ ...stats.storage_drives[0], volumes: [empty] }] } });
  assert.ok(text.includes('0.0 GB used / 475.3 GB free'));
});

test('volume-only fallback does not claim a physical drive capacity or measured SMART status', () => {
  const text = report({ stats: { ...stats, storage_drives: [] } });
  assert.match(text, /Physical storage capacity\s+: Not available/);
  assert.ok(text.includes('121.6 GB free'));
  assert.match(text, /Physical-drive capacity and status were not recorded/);
});

test('invalid or incomplete volume sets never invent a healthy zero usage', () => {
  for (const used_gb of [undefined, null, NaN, -1, 999, Infinity]) {
    const invalid = { ...volume, used_gb };
    const text = report({ stats: { ...stats, all_disks: [invalid], storage_drives: [{ ...stats.storage_drives[0], volumes: [invalid] }] } });
    assert.match(text, /Mounted-volume usage\s+: Not available/);
    assert.doesNotMatch(text, /0.0% in use/);
  }
});

test('disk matching uses disk_index and does not attach disk 10 to disk 1', () => {
  const text = report({ stats: { ...stats, all_disks: [{ ...volume, disk_index: 10 }], storage_drives: [{ ...stats.storage_drives[0], disk_index: 1, volumes: [] }] } });
  assert.match(text, /Reported drive status\s+: OK\n {4}Mounted-volume usage\s+: Not available/);
});

test('saved warning-sign count is not labelled as the number of active scan warnings', () => {
  const text = report();
  assert.match(text, /Checks with warning signs\s+: 0/);
  assert.match(text, /not the current scan findings/);
  assert.doesNotMatch(text, /Active Warnings/);
});

test('older selected check stays selected, recent history is sorted and never clipped', () => {
  const newer = { ...original, session_id: 'new-id', created_at: '2026-10-23T12:40:00Z', diagnosis_mode: 'component', component_ids: '["storage"]', diagnosed_category: 'Storage capacity concern that must not be shortened in an exported support report' };
  const text = report({ sessions: [original, newer], selectedSession: original });
  assert.match(text, /Saved check ID\s+: S-100/);
  assert.match(text, /Selected check\s+: Full device check/);
  assert.ok(text.includes('Storage space needs attention'));
  assert.doesNotMatch(text, /\u2026/);
  assert.equal(sortSessionsNewestFirst([original, newer])[0].session_id, newer.session_id);
});

test('crash historical and follow-up records stay separate from supporting memory', () => {
  const crash = { ...original, diagnosis_mode: 'scenario', scenario_id: 'app-crashes', proof: [
    { metric_id: 'application_crash_history', label: 'Application crash records', value: '3 found', status: 'high', window_kind: 'rolling_history' }, original.proof[0]],
    resolution_status: 'resolved', resolution_checked_at: time, resolution_proof: [
      { metric_id: 'application_crash_history', label: 'Application crash records since diagnosis', value: '0 found', status: 'normal', window_kind: 'since_diagnosis' },
      { ...original.proof[0], value: '65.7%', status: 'normal' }] };
  const text = report({ selectedSession: crash });
  assert.match(text, /Selected check\s+: Application crashes check/);
  assert.match(text, /3 matching crash records/);
  assert.match(text, /Latest follow-up\s+: No new matching records/);
  assert.ok(text.includes('LATEST SAVED FOLLOW-UP READINGS'));
  assert.ok(text.includes('65.7%'));
  assert.match(text, /does not confirm/);
});

test('legacy missing proof and failed history loads are explicit', () => {
  const text = report({ stats: null, selectedSession: { ...original, proof: [] }, historyUnavailable: true });
  assert.match(text, /report may be incomplete/);
  assert.match(text, /Detailed readings were not saved/);
  assert.doesNotMatch(text, /Readings needing review\s+: 0/);
});

test('an explicitly empty selection cannot resurrect a deleted dashboard record', () => {
  const text = report({ sessions: [], selectedSession: null, dashboard: { ...dashboard, last_saved_session: original, last_diagnosis: original } });
  assert.match(text, /No saved check is available/);
  assert.doesNotMatch(text, /Saved check ID\s+: S-100/);
});

test('preview, copy, download and print share the same report text', () => {
  const source = readFileSync(new URL('../src/pages/ShareReportView.tsx', import.meta.url), 'utf8');
  assert.match(source, /clipboard.writeText\(reportText\)/);
  assert.match(source, /downloadTextFile\(reportFileName, reportText\)/);
  assert.match(source, /<pre>\{reportText\}<\/pre>/);
  assert.doesNotMatch(source, /reportText.split/);
  assert.doesNotMatch(source, /Report downloaded as a text file/);
  assert.match(source, /const \[snapshot, setSnapshot\]/);
  assert.match(source, /\.\.\.snapshot,/);
  assert.doesNotMatch(source, /\[activeSession, dashboard, hardwareUpdatedAt, sessions, stats/);
});
