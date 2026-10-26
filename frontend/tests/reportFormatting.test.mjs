import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { buildReportText, buildReportDocument, buildReportFileName, reportDocumentText, sortSessionsNewestFirst } from '../src/lib/reportFormatting.ts';
import { buildReportHtml, reportBodyHtml, escapeReportHtml } from '../src/lib/reportHtml.ts';
import { copyReport } from '../src/lib/reportExports.ts';

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

for (const [timezone, cases] of [
  ['Asia/Singapore', [
    ['2026-10-26T15:59:59.999Z', '2026-10-26'],
    ['2026-10-26T16:00:00.000Z', '2026-10-27'],
    ['2026-10-31T15:59:59.999Z', '2026-10-31'],
    ['2026-10-31T16:00:00.000Z', '2026-11-01'],
    ['2026-12-31T15:59:59.999Z', '2026-12-31'],
    ['2026-12-31T16:00:00.000Z', '2027-01-01'],
  ]],
  ['America/Los_Angeles', [
    ['2026-10-27T06:59:59.999Z', '2026-10-26'],
    ['2026-10-27T07:00:00.000Z', '2026-10-27'],
    ['2027-01-01T07:59:59.999Z', '2026-12-31'],
    ['2027-01-01T08:00:00.000Z', '2027-01-01'],
  ]],
]) {
  test(`PDF and text filename dates match Generated at local boundaries in ${timezone}`, () => {
    // Separate processes prevent timezone changes leaking into other report tests.
    const result = execFileSync(process.execPath, ['--input-type=module', '-e', `
      import { buildReportFileName, buildReportDocument, reportDocumentText, formatDate } from ${JSON.stringify(new URL('../src/lib/reportFormatting.ts', import.meta.url).href)};
      import { buildReportHtml } from ${JSON.stringify(new URL('../src/lib/reportHtml.ts', import.meta.url).href)};
      const selected = ${JSON.stringify(original)};
      const before = JSON.stringify(selected);
      const results = ${JSON.stringify(cases)}.map(([instant]) => {
        const generatedAt = new Date(instant);
        const document = buildReportDocument({ stats: null, dashboard: ${JSON.stringify(dashboard)},
          sessions: [selected], selectedSession: selected, hardwareUpdatedAt: null,
          historyUnavailable: false, includeHistory: false, generatedAt });
        const generated = document.sections[0].fields.find(field => field.label === 'Generated').value;
        const parts = new Intl.DateTimeFormat('en-US', { year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(generatedAt);
        const part = type => parts.find(part => part.type === type).value;
        const base = buildReportFileName(selected.session_code, generatedAt);
        const text = reportDocumentText(document);
        return { pdf: base + '.pdf', text: base + '.txt', localDate: part('year') + '-' + part('month') + '-' + part('day'),
          sameGenerated: generated === formatDate(generatedAt) && text.includes(generated) && buildReportHtml(document).includes(generated),
          checkDate: document.sections.find(section => section.title === 'Selected saved check').fields.find(field => field.label === 'Check date').value,
          originalDate: formatDate(selected.created_at), savedReadingPreserved: text.includes('88.1% (13.8 / 15.7 GB)'),
          unchanged: JSON.stringify(selected) === before && generatedAt.toISOString() === instant };
      });
      process.stdout.write(JSON.stringify(results));
    `], { env: { ...process.env, TZ: timezone }, encoding: 'utf8' });
    const results = JSON.parse(result);
    cases.forEach(([, expected], index) => {
      const actual = results[index];
      assert.equal(actual.pdf, `RigMD-Device-Report-S-100-${expected}.pdf`);
      assert.equal(actual.text, `RigMD-Device-Report-S-100-${expected}.txt`);
      assert.equal(actual.localDate, expected);
      assert.equal(actual.sameGenerated, true);
      assert.equal(actual.checkDate, actual.originalDate);
      assert.equal(actual.savedReadingPreserved, true);
      assert.equal(actual.unchanged, true);
    });
  });
}

test('export names keep the frozen generation date and sanitize saved check IDs', () => {
  const generatedAt = new Date(2026, 9, 27, 0, 1);
  assert.equal(buildReportFileName('S-124', generatedAt), 'RigMD-Device-Report-S-124-2026-10-27');
  assert.equal(buildReportFileName('../S-124\\?', generatedAt), 'RigMD-Device-Report-S-124-2026-10-27');
  for (const code of [null, undefined, '', '../']) {
    assert.equal(buildReportFileName(code, generatedAt), 'RigMD-Device-Report-device-2026-10-27');
  }
});

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
  assert.match(text, /Reported drive status\s+: OK\nMounted-volume usage\s+: Not available/);
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

const documentReport = (overrides = {}) => buildReportDocument({
  stats, dashboard, hardwareUpdatedAt: new Date(time), sessions: [original],
  selectedSession: original, historyUnavailable: false, generatedAt: new Date(time), ...overrides,
});

test('text, printable body and standalone download retain every field from the shared document', () => {
  const document = documentReport();
  const text = reportDocumentText(document);
  const body = reportBodyHtml(document);
  const html = buildReportHtml(document);
  assert.ok(html.includes(body));
  for (const section of document.sections) {
    assert.ok(text.includes(section.title.toUpperCase()));
    assert.ok(body.includes(escapeReportHtml(section.title)));
    const fields = [...(section.fields ?? []), ...(section.entries ?? []).flatMap(entry => entry.fields)];
    for (const { label, value } of fields) {
      assert.ok(text.includes(label) && text.includes(value), label);
      assert.ok(body.includes(escapeReportHtml(label)) && body.includes(escapeReportHtml(value)), label);
    }
  }
  assert.ok(text.indexOf('SELECTED SAVED CHECK') < text.indexOf('HARDWARE SNAPSHOT'));
  assert.doesNotMatch(text, /={5,}|-{5,}| {8,}:/);
});

test('optional history omits unrelated checks in every format without omitting selected readings', () => {
  const unrelated = { ...original, session_id: 'unrelated', session_code: 'S-OTHER' };
  const document = documentReport({ sessions: [original, unrelated], includeHistory: false });
  for (const output of [reportDocumentText(document), buildReportHtml(document)]) {
    assert.ok(output.includes('88.1% (13.8 / 15.7 GB)'));
    assert.ok(output.includes('S-100'));
    assert.ok(!output.includes('S-OTHER'));
    assert.doesNotMatch(output, /Saved history summary|SAVED HISTORY SUMMARY|Recent saved checks/);
  }
  assert.ok(buildReportHtml(documentReport({ sessions: [original, unrelated], includeHistory: true })).includes('S-OTHER'));
});

test('HTML exports escape hostile device names and stored notes, leaving no executable content', () => {
  const hostile = '<img src=x onerror="alert(1)"> & </style><script>bad()</script>';
  const document = documentReport({ stats: { ...stats, device_name: hostile },
    selectedSession: { ...original, last_action_summary: hostile } });
  const html = buildReportHtml(document);
  assert.ok(html.includes(escapeReportHtml(hostile)));
  assert.ok(reportDocumentText(document).includes(hostile));
  assert.doesNotMatch(html, /<img|<script|onerror="alert/);
});

test('a partial follow-up labels omitted metrics without reusing original values', () => {
  const session = { ...original, proof: [
    { metric_id: 'memory_usage', label: 'Working memory', value: '98%', status: 'high' },
    { metric_id: 'cpu_usage', label: 'Processor activity', value: '83%', status: 'high' },
  ], resolution_status: 'resolved', resolution_checked_at: time, resolution_proof: [
    { metric_id: 'cpu_usage', label: 'Processor activity', value: '0%', status: 'normal', captured_at: time },
  ] };
  const document = documentReport({ selectedSession: session, includeHistory: false });
  const section = document.sections.find(section => section.title === 'Latest saved follow-up readings');
  const memory = section.entries.find(entry => entry.title === 'Working memory');
  assert.ok(memory.fields.some(field => field.value === 'Not recorded in follow-up'));
  assert.ok(!JSON.stringify(section).includes('98%'));
  assert.ok(JSON.stringify(section).includes('0%'));
  assert.ok(section.fields.some(field => field.label === 'Unavailable readings' && field.value === '1'));
  assert.match(reportDocumentText(document), /Latest follow-up : (?!Warning not detected)/);
});

class FakeClipboardItem {
  constructor(data) { this.data = data; }
}

test('rich copying includes HTML and plain text of the same frozen report', async () => {
  const document = documentReport({ includeHistory: false });
  let items;
  const format = await copyReport(document, { write: async value => { items = value; },
    writeText: async () => assert.fail('Rich copy succeeded') }, FakeClipboardItem);
  assert.equal(format, 'formatted');
  assert.equal(await items[0].data['text/plain'].text(), reportDocumentText(document));
  assert.equal(await items[0].data['text/html'].text(), buildReportHtml(document));
});

test('unsupported or rejected rich clipboard falls back to readable text and reports failures', async () => {
  const document = documentReport();
  for (const write of [undefined, async () => { throw Error('Unsupported'); }]) {
    let copied;
    assert.equal(await copyReport(document, { write, writeText: async value => { copied = value; } }, FakeClipboardItem), 'text');
    assert.equal(copied, reportDocumentText(document));
  }
  await assert.rejects(copyReport(document, { writeText: async () => { throw Error('Denied'); } }, FakeClipboardItem), /Denied/);
});
