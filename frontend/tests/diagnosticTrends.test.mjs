import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import ts from 'typescript';
import { buildTrendGroups, buildTrendPoints, trendChange, buildTrendObservations, getTrendReadingOptions, getTrendScopeOptions,
  trendCheckCount, getTrendStatusColor, TREND_COMPONENT_IDS, TREND_SCENARIO_IDS } from '../src/lib/diagnosticTrends.ts';

const time = index => new Date(Date.UTC(2026, 9, 25, 10, index)).toISOString();
const reading = (index, value = '92%', extra = {}) => ({ metric_id: 'memory_usage', captured_at: time(index), value,
  status: 'high', evidence_kind: 'measurement', rule_version: 'v1', ...extra });
const session = (index, extra = {}) => ({ session_id: `session-${index}`, session_code: `S-${index}`, device_id: 'computer-a',
  created_at: time(index), diagnosis_mode: 'full', proof: [reading(index)], ...extra });
const pointsFor = (sessions, metric = 'memory_usage') => buildTrendPoints(buildTrendGroups(sessions).groups[0], metric);

test('chart colors describe saved status, not the metric or a claim of hardware health', () => {
  assert.equal(getTrendStatusColor('Needs attention'), '#fcd34d');
  assert.equal(getTrendStatusColor('No warning in this reading'), '#34d399');
  for (const status of ['Not verified', 'Not recorded', 'Conflicting saved readings', 'Information only', '']) {
    assert.equal(getTrendStatusColor(status), '#94a3b8');
  }
  const storage = pointsFor([session(1, { proof: [reading(1, '76.6% used', { metric_id: 'storage_usage', status: 'normal' })] })], 'storage_usage');
  assert.equal(getTrendStatusColor(storage[0].status), '#34d399');
  assert.equal(getTrendStatusColor(pointsFor([session(1)])[0].status), '#fcd34d');
});

test('orders immutable saved snapshots and describes usage change, not a repair', () => {
  const sessions = [session(2, { proof: [reading(2, '70%', { status: 'normal' })] }), session(1)];
  const before = structuredClone(sessions);
  const points = pointsFor(sessions);
  assert.deepEqual(points.map(point => point.value), [92, 70]);
  assert.equal(points[0].segment, points[1].segment);
  assert.equal(trendChange(points, 'memory_usage'), 'Lower use: 92% to 70% in the latest comparable readings.');
  assert.doesNotMatch(trendChange(points, 'memory_usage'), /fixed|repaired|health|improved/i);
  assert.deepEqual(sessions, before);
});

test('same metric from different computers and exact check scopes never share a group', () => {
  const sessions = [session(1), session(2, { device_id: 'computer-b' }),
    session(3, { diagnosis_mode: 'component', component_ids: '["memory"]' }),
    session(4, { diagnosis_mode: 'scenario', scenario_id: 'app-crashes' })];
  const model = buildTrendGroups(sessions);
  assert.equal(model.groups.length, 4);
  assert.ok(model.groups.every(group => buildTrendPoints(group, 'memory_usage')[0].segment !== null));
  assert.ok(model.groups.every(group => /two comparable/.test(trendChange(buildTrendPoints(group, 'memory_usage'), 'memory_usage'))));
});

test('component order is canonical but mixed and malformed component sets remain distinct or excluded', () => {
  const model = buildTrendGroups([session(1, { diagnosis_mode: 'component', component_ids: '["cpu","memory"]' }),
    session(2, { diagnosis_mode: 'component', component_ids: '["memory","cpu"]' }),
    session(3, { diagnosis_mode: 'component', component_ids: '[broken]' }),
    session(4, { diagnosis_mode: 'component', component_ids: '' })]);
  assert.equal(model.groups.length, 1);
  assert.equal(model.groups[0].snapshots.length, 2);
  assert.equal(model.excludedSessions, 2);
});

test('missing identity, scope, or timezone-aware date is excluded without inventing a computer', () => {
  const bad = [session(1, { device_id: '' }), session(2, { diagnosis_mode: '' }), session(3, { created_at: '2026-10-25T10:03:00' }),
    session(4, { created_at: null }), session(5, { created_at: 'not a date' }), session(6, { session_id: '' })];
  const model = buildTrendGroups(bad);
  assert.equal(model.groups.length, 0);
  assert.equal(model.excludedSessions, bad.length);
});

test('original and latest follow-up stay separate and use their own captured times', () => {
  const points = pointsFor([session(1, { resolution_device_id: 'computer-a', resolution_checked_at: time(3),
    resolution_proof: [reading(3, '68%', { status: 'normal' })] })]);
  assert.deepEqual(points.map(point => point.value), [92, 68]);
  assert.deepEqual(points.map(point => point.kind), ['Original check', 'Latest follow-up']);
  assert.ok(points.every(point => point.sessionId === 'session-1'));
  assert.equal(points[1].at, Date.parse(time(3)));
});

test('follow-ups never borrow the original computer identity', () => {
  const base = session(1, { resolution_checked_at: time(3), resolution_proof: [reading(3, '68%')] });
  assert.equal(buildTrendGroups([base]).groups[0].snapshots.length, 1);
  const model = buildTrendGroups([{ ...base, resolution_device_id: 'computer-b' }]);
  assert.equal(model.groups.length, 2);
  assert.ok(model.groups.every(group => group.snapshots.length === 1));
  const legacy = buildTrendGroups([{ ...base, device_id: '', resolution_device_id: 'computer-b' }]);
  assert.equal(legacy.groups.length, 1);
  assert.equal(legacy.groups[0].snapshots[0].kind, 'Latest follow-up');
});

test('missing and contradictory values are gaps, including numeric values marked unavailable', () => {
  for (const invalid of [reading(2, 'Unavailable', { status: 'unknown' }), reading(2, '15%', { evidence_kind: 'unavailable' }),
    reading(2, '15%', { status: 'unknown' }), reading(2, '101%'), reading(2, '-1%'), reading(2, '15% not verified'),
    reading(2, '50% (1 / 16 GB)'), reading(2, '50%', { captured_at: null }), reading(2, '50%', { window_kind: 'since_diagnosis' })]) {
    const points = pointsFor([session(1), session(2, { proof: [invalid] }), session(3)]);
    assert.equal(points[1].value, null);
    assert.notEqual(points[0].segment, points[2].segment);
  }
});

test('missing metrics, context evidence, and duplicate metric identities cannot become measurements', () => {
  for (const proof of [[reading(2, '23%', { metric_id: 'cpu_usage' })], [reading(2, '23%', { evidence_kind: 'context' })],
    [reading(2), reading(2, '40%')]]) {
    const points = pointsFor([session(1), session(2, { proof }), session(3)]);
    assert.equal(points[1].value, null);
    assert.notEqual(points[0].segment, points[2].segment);
  }
});

test('latest missing reading does not silently reuse an older good value', () => {
  const points = pointsFor([session(1), session(2, { proof: [reading(2, 'Unavailable', { status: 'unknown' })] })]);
  assert.match(trendChange(points, 'memory_usage'), /latest saved reading could not be verified/);
});

test('rule changes and missing rule versions break connections and change summaries', () => {
  for (const version of ['v2', '', 123]) {
    const points = pointsFor([session(1), session(2, { proof: [reading(2, '60%', { rule_version: version })] })]);
    assert.notEqual(points[0].segment, points[1].segment);
    assert.match(trendChange(points, 'memory_usage'), /two comparable/);
  }
});

test('same-time observations remain separate but cannot establish direction', () => {
  const points = pointsFor([session(1), session(2, { created_at: time(1), proof: [reading(1, '60%')] })]);
  assert.equal(points.length, 2);
  assert.ok(points.every(point => point.segment === null));
  assert.match(trendChange(points, 'memory_usage'), /two comparable/);
});

test('duplicate session IDs are excluded rather than counted twice', () => {
  const duplicate = session(1);
  const model = buildTrendGroups([duplicate, { ...duplicate, proof: [reading(1, '30%')] }]);
  assert.equal(model.groups.length, 0);
  assert.equal(model.excludedSessions, 2);
});

test('copied original proof and impossible follow-up dates cannot become fresh evidence', () => {
  const copied = pointsFor([session(1, { resolution_device_id: 'computer-a', resolution_checked_at: time(3), resolution_proof: [reading(1)] })]);
  assert.equal(copied[1].value, null);
  for (const checked of [time(0), time(1), '', '2026-10-25T10:03:00']) {
    const model = buildTrendGroups([session(1, { resolution_device_id: 'computer-a', resolution_checked_at: checked, resolution_proof: [reading(3)] })]);
    assert.equal(model.groups[0].snapshots.length, 1);
  }
});

test('future and stale captured timestamps leave gaps; offsets use actual instants', () => {
  for (const captured_at of [time(20), '2026-10-25T09:00:00Z', '2026-10-25T10:01:00']) {
    assert.equal(pointsFor([session(1, { proof: [reading(1, '92%', { captured_at })] })])[0].value, null);
  }
  assert.equal(pointsFor([session(1, { proof: [reading(1, '92%', { captured_at: '2026-10-25T18:01:00+08:00' })] })])[0].value, 92);
});

test('zero and full usage are real values rather than missing evidence', () => {
  const points = pointsFor([session(1, { proof: [reading(1, '0%', { status: 'normal' })] }), session(2, { proof: [reading(2, '100%')] })]);
  assert.deepEqual(points.map(point => point.value), [0, 100]);
  assert.match(trendChange(points, 'memory_usage'), /Higher use: 0% to 100%/);
});

test('aggregate storage is shown as snapshots, never as one matching physical drive', () => {
  const points = pointsFor([session(1, { proof: [reading(1, '75% used', { metric_id: 'storage_usage' })] }),
    session(2, { proof: [reading(2, '60% used', { metric_id: 'storage_usage' })] })], 'storage_usage');
  assert.deepEqual(points.map(point => point.value), [75, 60]);
  assert.ok(points.every(point => point.segment === null));
  assert.match(trendChange(points, 'storage_usage'), /individual snapshots only/);
});

test('crash observations are retained but never converted into resource percentages; label-only evidence stays excluded', () => {
  const model = buildTrendGroups([session(1, { proof: [reading(1, '3 records', { metric_id: 'app_crash_history', evidence_kind: 'event_log' })] }),
    session(2, { proof: [reading(2, '92%', { metric_id: undefined, label: 'Physical Memory' })] })]);
  assert.equal(model.groups.length, 1);
  assert.equal(buildTrendPoints(model.groups[0], 'memory_usage')[0].value, null);
  assert.equal(model.excludedSessions, 1);
});

test('impossible calendar dates cannot be silently normalized into another month', () => {
  const model = buildTrendGroups([session(1, { created_at: '2026-02-30T10:00:00Z' }), session(2, { created_at: '2026-13-02T10:00:00Z' })]);
  assert.equal(model.groups.length, 0);
  assert.equal(model.excludedSessions, 2);
});

test('a changed or newly missing recorded memory capacity breaks a percentage comparison', () => {
  for (const value of ['25% (8.0 / 32.0 GB)', '25%']) {
    const points = pointsFor([session(1, { proof: [reading(1, '50% (8.0 / 16.0 GB)')] }), session(2, { proof: [reading(2, value)] })]);
    assert.deepEqual(points.map(point => point.value), [50, 25]);
    assert.notEqual(points[0].segment, points[1].segment);
    assert.match(trendChange(points, 'memory_usage'), /two comparable/);
  }
});

test('part and problem choices match the active choices in Check My Device, excluding commented-out tests', () => {
  const source = ts.createSourceFile('NewDiagnosisView.tsx', readFileSync(new URL('../src/pages/NewDiagnosisView.tsx', import.meta.url), 'utf8'), ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const collect = name => {
    const ids = [];
    const scan = node => {
      if (ts.isPropertyAssignment(node) && node.name.getText(source) === 'id' && ts.isStringLiteral(node.initializer)) ids.push(node.initializer.text);
      ts.forEachChild(node, scan);
    };
    const find = node => {
      if (ts.isVariableDeclaration(node) && node.name.getText(source) === name && node.initializer) scan(node.initializer);
      else ts.forEachChild(node, find);
    };
    find(source);
    return ids.sort();
  };
  assert.deepEqual([...TREND_COMPONENT_IDS].sort(), collect('COMPONENT_GROUPS'));
  assert.deepEqual([...TREND_SCENARIO_IDS].sort(), collect('DIAGNOSIS_SCENARIOS'));
});

test('every supported part and problem stays selectable with an explicit zero eligible-history count', () => {
  const model = buildTrendGroups([session(1)]);
  assert.equal(trendCheckCount(model.groups, 'full'), 1);
  assert.equal(trendCheckCount(model.groups, 'component'), 0);
  const parts = getTrendScopeOptions(model.groups, 'component');
  const problems = getTrendScopeOptions(model.groups, 'scenario');
  assert.equal(parts.length, 9);
  assert.equal(problems.length, 10);
  assert.ok([...parts, ...problems].every(option => option.count === 0));
  assert.ok(parts.some(option => option.value === 'component:gpu'));
  assert.ok(problems.some(option => option.value === 'scenario:app-crashes'));
});

test('saved combination scopes stay separate and follow-ups are not counted as new check sessions', () => {
  const model = buildTrendGroups([session(1, { diagnosis_mode: 'component', component_ids: '["memory","cpu"]',
    resolution_device_id: 'computer-a', resolution_checked_at: time(2), resolution_proof: [reading(2)] })]);
  const options = getTrendScopeOptions(model.groups, 'component');
  assert.equal(trendCheckCount(model.groups, 'component'), 1);
  assert.equal(options.find(option => option.value === 'component:cpu,memory').count, 1);
  assert.equal(options.find(option => option.value === 'component:memory').count, 0);
});

test('readings include all recorded areas, not only the first three usage metrics', () => {
  const proof = [reading(1), reading(1, 'Intel Graphics', { metric_id: 'graphics_context', label: 'Graphics', evidence_kind: 'context', status: 'info' }),
    reading(1, 'Not verified', { metric_id: 'battery_drain', label: 'Battery wear and drain rate', evidence_kind: 'unavailable', status: 'unknown' }),
    reading(1, 'Windows 11', { metric_id: 'os_context', label: 'Windows', evidence_kind: 'context', status: 'info' }),
    reading(1, 'Connected', { metric_id: 'network_link', label: 'Network connection', evidence_kind: 'reported_status', status: 'info' })];
  const group = buildTrendGroups([session(1, { proof })]).groups[0];
  assert.deepEqual(new Set(getTrendReadingOptions(group).map(item => item.value)), new Set(proof.map(item => item.metric_id)));
  assert.equal(getTrendReadingOptions(group).length, 5);
  group.snapshots[0].proof.push(reading(1, 'Unknown', { metric_id: 123 }));
  assert.equal(getTrendReadingOptions(group).length, 5);
});

test('status-only part checks are available even when there are no resource percentages', () => {
  const model = buildTrendGroups([session(1, { diagnosis_mode: 'component', component_ids: '["gpu"]',
    proof: [reading(1, 'Intel Graphics', { metric_id: 'graphics_context', label: 'Graphics', evidence_kind: 'context', status: 'info' })] })]);
  assert.equal(model.groups.length, 1);
  assert.equal(model.excludedSessions, 0);
  const point = buildTrendObservations(model.groups[0], 'graphics_context')[0];
  assert.equal(point.value, 'Intel Graphics');
  assert.equal(point.status, 'Information only');
  assert.equal(point.timeVerified, true);
});

test('unavailable, not assessed, missing, and duplicate observations do not become healthy outcomes', () => {
  const model = buildTrendGroups([session(1, { proof: [reading(1, 'Unavailable', { metric_id: 'gpu_temperature', status: 'unknown', evidence_kind: 'unavailable' })] }),
    session(2, { proof: [reading(2, 'Unavailable', { metric_id: 'fan_operation', label: 'Fan operation', status: 'unknown', evidence_kind: 'unavailable', meaning: 'This scan does not measure fan speed or airflow.' })] }),
    session(3, { proof: [reading(3, '70%', { metric_id: 'gpu_temperature' }), reading(3, '80%', { metric_id: 'gpu_temperature' })] })]);
  const group = model.groups[0];
  const observations = buildTrendObservations(group, 'gpu_temperature');
  assert.equal(observations[0].status, 'Reading unavailable');
  assert.equal(observations[1].status, 'Not recorded');
  assert.equal(observations[2].status, 'Conflicting saved readings');
  assert.equal(buildTrendObservations(group, 'fan_operation')[1].status, 'Not assessed');
  assert.ok(observations.every(point => !/normal|healthy|fixed/i.test(point.status)));
});

test('observations preserve raw values but mark copied or absent capture times unverified', () => {
  const model = buildTrendGroups([session(1, { proof: [reading(1, 'Windows 11', { metric_id: 'os_context', evidence_kind: 'context', status: 'info', captured_at: null })],
    resolution_device_id: 'computer-a', resolution_checked_at: time(3), resolution_proof: [reading(1, 'Windows 11', { metric_id: 'os_context', evidence_kind: 'context', status: 'info' })] })]);
  const observations = buildTrendObservations(model.groups[0], 'os_context');
  assert.ok(observations.every(point => point.value === 'Windows 11' && !point.timeVerified && point.status === 'Reading time not verified'));
});

test('crash original and follow-up periods are retained rather than turned into a progress percentage', () => {
  const original = reading(1, '4 records', { metric_id: 'application_crash_history', label: 'Application crash records', evidence_kind: 'event_log',
    window_kind: 'previous_7_days', window_start: '2026-10-18T10:01:00Z', window_end: time(1) });
  const followup = { ...original, value: '0 records', captured_at: time(3), window_kind: 'since_diagnosis', window_start: time(1), window_end: time(3), status: 'normal' };
  const group = buildTrendGroups([session(1, { diagnosis_mode: 'scenario', scenario_id: 'app-crashes', proof: [original],
    resolution_device_id: 'computer-a', resolution_checked_at: time(3), resolution_proof: [followup] })]).groups[0];
  const observations = buildTrendObservations(group, 'application_crash_history');
  assert.deepEqual(observations.map(point => point.value), ['4 records', '0 records']);
  assert.ok(observations.every(point => point.status === 'Recorded Windows events'));
  assert.equal(observations[0].window.sinceOriginal, false);
  assert.equal(observations[1].window.sinceOriginal, true);
  assert.notEqual(observations[0].window.start, observations[1].window.start);
  assert.ok(buildTrendPoints(group, 'memory_usage').every(point => point.value === null));
});

test('invalid event windows and failed event checks never look like a verified zero', () => {
  for (const extra of [{ window_start: time(4), window_end: time(1) }, { window_start: null }, { window_end: time(20) }, { status: 'unknown' }]) {
    const group = buildTrendGroups([session(1, { proof: [reading(1, '0 records', { metric_id: 'application_crash_history', label: 'Application crash records', evidence_kind: 'event_log',
      window_start: time(0), window_end: time(1), ...extra })] })]).groups[0];
    const point = buildTrendObservations(group, 'application_crash_history')[0];
    assert.equal(point.status, 'Event check not verified');
    assert.equal(point.value, '0 records');
  }
});

test('contradictory unavailable evidence and malformed sensor values never receive a normal observation label', () => {
  for (const extra of [{ value: '50C', evidence_kind: 'unavailable', status: 'normal' }, { value: '500C', status: 'normal' }]) {
    const group = buildTrendGroups([session(1, { proof: [reading(1, '50C', { metric_id: 'gpu_temperature', ...extra })] })]).groups[0];
    const point = buildTrendObservations(group, 'gpu_temperature')[0];
    assert.equal(point.status, 'Reading unavailable');
    assert.equal(point.attention, false);
  }
});
