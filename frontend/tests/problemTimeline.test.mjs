import test from 'node:test';
import assert from 'node:assert/strict';
import { buildProblemTimelineGroups, buildProblemTimeline, getRelatedWarningTiming, parseProblemSessions, readProblemHistory, loadProblemHistories } from '../src/lib/problemTimeline.ts';

const at = index => new Date(Date.UTC(2026, 9, 26, 0, index)).toISOString();
const asOf = Date.parse(at(59));
const reading = (index, extra = {}) => ({ metric_id: 'memory_usage', label: 'Working memory', value: '90%', status: 'high',
  evidence_kind: 'measurement', captured_at: at(index), rule_version: 'v1', ...extra });
const session = (index, extra = {}) => ({ session_id: `s${index}`, session_code: `S-${index}`, device_id: 'computer-a',
  created_at: at(index), diagnosis_mode: 'full', proof: [reading(index)], ...extra });
const problem = (...ids) => ({ pattern_key: 'memory_usage', occurrences: ids.map(id => ({ id })) });
const groupFor = (sessions, reference = problem('s1', 's2')) => buildProblemTimelineGroups(sessions, reference, asOf).groups[0];
const detail = (source, runs = []) => ({ ...source, remediation_history: runs });
const run = (extra = {}) => ({ run_id: 'run1', status: 'Completed', created_at: at(3), completed_at: at(4),
  attempts: [{ action_code: 'close_selected_apps' }], ...extra });

test('latest related warning preserves its instant across the UTC to Singapore date boundary', () => {
  const timing = getRelatedWarningTiming([{ id: 's1' }, { id: 's2' }], {
    s1: '2026-10-25T17:15:00+00:00', s2: '2026-10-25T17:16:38+00:00',
  });
  assert.equal(timing.complete, true);
  assert.equal(timing.at, Date.parse('2026-10-26T01:16:38+08:00'));
  assert.equal(new Date(timing.at).toLocaleDateString('en-US', { timeZone: 'Asia/Singapore', month: 'short', day: '2-digit', year: 'numeric' }), 'Oct 26, 2026');
});

test('incomplete warning dates are explicit and cannot use a date-only server label as a timestamp', () => {
  const timing = getRelatedWarningTiming([{ id: 's1' }, { id: 's2' }, { id: 's3' }], { s1: at(1), s2: 'Oct 25, 2026' });
  assert.equal(timing.complete, false);
  assert.equal(timing.at, Date.parse(at(1)));
  assert.deepEqual(getRelatedWarningTiming([{ id: 's1' }], {}), { at: null, complete: false });
  assert.deepEqual(getRelatedWarningTiming([], {}), { at: null, complete: false });
});

test('a later normal reading stays in the same scope without claiming a repair or altering history', () => {
  const sessions = [session(1), session(2), session(5, { proof: [reading(5, { value: '60%', status: 'normal' })] })];
  const before = structuredClone(sessions);
  const timeline = buildProblemTimeline(groupFor(sessions), 'memory_usage', [], asOf);
  assert.deepEqual(timeline.events.map(event => event.value), ['90%', '90%', '60%']);
  assert.equal(timeline.latest.status, 'No warning in this reading');
  assert.equal(timeline.awaitingReading, false);
  assert.doesNotMatch(timeline.events.map(event => event.status).join(' '), /repaired|resolved|fixed|healthy/i);
  assert.deepEqual(sessions, before);
});

test('matching computers and exact scopes remain separate, and unrelated groups are not seeded', () => {
  const sessions = [session(1), session(2, { device_id: 'computer-b' }),
    session(3, { diagnosis_mode: 'component', component_ids: '["memory"]' }),
    session(4, { diagnosis_mode: 'scenario', scenario_id: 'slow-system' }), session(5, { device_id: 'computer-c' })];
  const model = buildProblemTimelineGroups(sessions, problem('s1', 's2', 's3', 's4'), asOf);
  assert.equal(model.groups.length, 4);
  assert.ok(model.groups.every(group => group.snapshots.length === 1));
  assert.ok(model.groups.every(group => group.deviceId !== 'computer-c'));
});

test('old, future, duplicate, missing and label-only records are not silently linked', () => {
  const sessions = [session(1), session(2, { device_id: '' }), session(3, { created_at: '2026-10-26 00:03:00' }),
    session(4, { proof: [reading(4, { metric_id: undefined })] }), session(5), session(5), session(6, { created_at: '2027-01-01T00:00:00Z' })];
  const model = buildProblemTimelineGroups(sessions, problem('s1', 's2', 's3', 's4', 's5', 's6'), asOf);
  assert.equal(model.groups.length, 1);
  assert.equal(model.unplacedCount, 5);
  assert.deepEqual(model.groups[0].snapshots.map(snapshot => snapshot.sessionId), ['s1', 's4']);
});

test('records before the first related snapshot do not become an invented problem history', () => {
  const model = buildProblemTimelineGroups([session(0), session(1), session(2)], problem('s2'), asOf);
  assert.deepEqual(model.groups[0].snapshots.map(snapshot => snapshot.sessionId), ['s2']);
});

test('legacy follow-up uses its own identity and cannot establish the original action computer', () => {
  const source = session(1, { device_id: '', resolution_device_id: 'computer-b', resolution_checked_at: at(5), resolution_proof: [reading(5)] });
  const group = groupFor([source], problem('s1'));
  assert.equal(group.deviceId, 'computer-b');
  assert.deepEqual(group.snapshots.map(snapshot => snapshot.kind), ['Latest follow-up']);
  const timeline = buildProblemTimeline(group, 'memory_usage', [{ sessionId: 's1', runs: [run()] }], asOf);
  assert.equal(timeline.unplacedActions, 1);
  assert.equal(timeline.events.length, 1);
  assert.throws(() => readProblemHistory(detail(source, [run()]), source, 'full'));
});

test('a copied, missing or contradictory follow-up stays unknown instead of clearing an original warning', () => {
  for (const proof of [[], [reading(1, { value: '0%', status: 'normal' })],
    [reading(5, { value: '0%', status: 'normal', evidence_kind: 'unavailable' })]]) {
    const source = session(1, { resolution_device_id: 'computer-a', resolution_checked_at: at(5), resolution_proof: proof });
    const timeline = buildProblemTimeline(groupFor([source], problem('s1')), 'memory_usage', [], asOf);
    assert.equal(timeline.latest.kind, 'Latest follow-up');
    assert.equal(timeline.latest.tone, 'unknown');
    assert.equal(timeline.latest.value, 'Not verified');
  }
});

test('reported completed actions remain neutral and require later readings, even with a Resolved attempt flag', () => {
  const source = session(1);
  const history = readProblemHistory(detail(source, [run({ attempts: [{ action_code: 'close_selected_apps', verification_status: 'Resolved' }] })]), source, 'full');
  const timeline = buildProblemTimeline(groupFor([source], problem('s1')), 'memory_usage', [history], asOf);
  assert.equal(timeline.awaitingReading, true);
  const action = timeline.events.at(-1);
  assert.equal(action.kind, 'Saved action');
  assert.equal(action.tone, 'neutral');
  assert.equal(action.status, 'Action reported complete; effect not verified');
  assert.equal(timeline.latest.tone, 'attention');
});

test('later follow-ups remove the pending-reading label without asserting action causality', () => {
  const source = session(1, { resolution_device_id: 'computer-a', resolution_checked_at: at(5), resolution_proof: [reading(5, { value: '50%', status: 'normal' })] });
  const history = readProblemHistory(detail(source, [run()]), source, 'full');
  const timeline = buildProblemTimeline(groupFor([source], problem('s1')), 'memory_usage', [history], asOf);
  assert.equal(timeline.awaitingReading, false);
  assert.deepEqual(timeline.events.map(event => event.kind), ['Original check', 'Saved action', 'Latest follow-up']);
  assert.equal(timeline.latest.value, '50%');
});

test('a follow-up page can retain action provenance from its earlier verified original', () => {
  const source = session(1, { resolution_device_id: 'computer-a', resolution_checked_at: at(5), resolution_proof: [reading(5)] });
  const group = groupFor([source], problem('s1'));
  const page = { ...group, snapshots: group.snapshots.filter(snapshot => snapshot.kind === 'Latest follow-up') };
  const history = readProblemHistory(detail(source, [run()]), source, 'full');
  const timeline = buildProblemTimeline(page, 'memory_usage', [history], asOf, group.snapshots);
  assert.equal(timeline.events.length, 2);
  assert.equal(timeline.unplacedActions, 0);
});

test('failed and unknown action statuses never become successful repairs', () => {
  for (const status of ['Failed', 'Resolved', 'Running', '']) {
    const source = session(1);
    const timeline = buildProblemTimeline(groupFor([source], problem('s1')), 'memory_usage', [{ sessionId: 's1', runs: [run({ status })] }], asOf);
    assert.equal(timeline.events.at(-1).tone, 'neutral');
    assert.doesNotMatch(timeline.events.at(-1).status, /successful|repaired|resolved/i);
  }
});

test('invalid action timing is disclosed and duplicate action IDs are not counted twice', () => {
  const source = session(1), group = groupFor([source], problem('s1'));
  for (const bad of [run({ created_at: 'yesterday' }), run({ created_at: at(0) }), run({ completed_at: at(2) }), run({ completed_at: '2027-01-01T00:00:00Z' })]) {
    const timeline = buildProblemTimeline(group, 'memory_usage', [{ sessionId: 's1', runs: [bad] }], asOf);
    assert.equal(timeline.unplacedActions, 1);
    assert.equal(timeline.events.length, 1);
  }
  assert.equal(buildProblemTimeline(group, 'memory_usage', [{ sessionId: 's1', runs: [run(), run()] }], asOf).events.length, 2);
});

test('event windows stay separate and event counts never become a repair percentage', () => {
  const metric = 'application_crash_history';
  const event = (index, extra = {}) => reading(index, { metric_id: metric, label: 'Application crash records', value: '4 records', evidence_kind: 'event_log',
    window_kind: 'previous_7_days', window_start: '2026-10-19T00:00:00Z', window_end: at(index), ...extra });
  const source = session(1, { proof: [event(1)], resolution_device_id: 'computer-a', resolution_checked_at: at(5),
    resolution_proof: [event(5, { value: '0 records', status: 'normal', window_kind: 'since_diagnosis', window_start: at(1) })] });
  const group = groupFor([source], { pattern_key: metric, occurrences: [{ id: 's1' }] });
  const timeline = buildProblemTimeline(group, metric, [], asOf);
  assert.equal(timeline.events.length, 2);
  assert.equal(timeline.latest.value, '0 records');
  assert.equal(timeline.latest.tone, 'neutral');
  assert.ok(timeline.latest.window.sinceOriginal);
  assert.notEqual(timeline.events[0].window.start, timeline.latest.window.start);
});

test('malformed session responses are errors, never an empty or healthy timeline', () => {
  for (const payload of [{}, null, { sessions: null }, { sessions: [null] }, { sessions: [{ session_id: 's1', proof: [null] }] },
    { sessions: [session(1, { device_id: 123 })] }, { sessions: [session(1, { proof: [reading(1, { value: 0 })] })] }]) assert.throws(() => parseProblemSessions(payload));
  assert.deepEqual(parseProblemSessions({ sessions: [] }), []);
  assert.deepEqual(parseProblemSessions([session(1)]), [session(1)]);
});

test('action responses must match the requested saved session, computer, scope and original time', () => {
  const source = session(1);
  for (const payload of [null, {}, { ...source }, detail({ ...source, session_id: 'other' }), detail({ ...source, device_id: 'other' }),
    detail({ ...source, diagnosis_mode: 'component', component_ids: '["memory"]' }), detail({ ...source, created_at: at(2) }),
    detail(source, [run(), run()]), detail(source, [run({ attempts: [null] })])]) assert.throws(() => readProblemHistory(payload, source, 'full'));
  assert.deepEqual(readProblemHistory(detail(source), source, 'full'), { sessionId: 's1', runs: [] });
});

test('bounded action loading reports partial failures without claiming no actions were saved', async () => {
  const sources = Array.from({ length: 9 }, (_, index) => session(index + 1));
  let active = 0, maximum = 0;
  const result = await loadProblemHistories(sources, 'full', async id => {
    active++; maximum = Math.max(maximum, active);
    await new Promise(resolve => setTimeout(resolve, 5)); active--;
    if (id === 's2') throw new Error('Unavailable');
    return detail(sources.find(source => source.session_id === id));
  }, new AbortController().signal);
  assert.ok(maximum <= 4);
  assert.equal(result.histories.length, 8);
  assert.deepEqual(result.failed, ['s2']);
});

test('aborted and superseded action requests never publish stale history', async () => {
  const controller = new AbortController();
  const pending = loadProblemHistories([session(1)], 'full', async () => {
    controller.abort(); return detail(session(1), [run()]);
  }, controller.signal);
  await assert.rejects(pending, { name: 'AbortError' });
  let calls = 0;
  await assert.rejects(loadProblemHistories([session(1)], 'full', async () => { calls++; }, controller.signal), { name: 'AbortError' });
  assert.equal(calls, 0);
});
