import test from 'node:test';
import assert from 'node:assert/strict';
import { loadReportData } from '../src/lib/reportData.ts';

const older = { session_id: 'older', session_code: 'S-099', created_at: '2026-10-23T12:34:41Z' };
const latest = { session_id: 'latest', session_code: 'S-101', created_at: '2026-10-23T13:37:51Z' };
const summary = { server_time: '2026-10-23T13:40:00Z', totals: { total_sessions: 2, this_month_count: 2, escalated_count: 0 }, recurring_issues_count: 0, warning_signs_active_count: 0 };
const response = (data, ok = true) => ({ ok, json: async () => data });
const controller = () => new AbortController();

test('refresh loads both endpoints without cache and uses returned totals, never a session code', async () => {
  const calls = [];
  const abort = controller();
  const result = await loadReportData(async (path, options) => {
    calls.push({ path, options });
    return response(path.includes('/sessions') ? { sessions: [older, latest] } : summary);
  }, abort.signal);
  assert.deepEqual(calls.map(call => call.path), ['/api/diagnosis/sessions', '/api/dashboard/summary']);
  for (const { options } of calls) {
    assert.equal(options.signal, abort.signal);
    assert.equal(options.cache, 'no-store');
  }
  assert.deepEqual(result.sessions, [latest, older]);
  assert.equal(result.dashboard.totals.total_sessions, 2);
  assert.equal(result.dashboard.server_time, summary.server_time);
});

test('array and object session responses are supported, including a verified empty history', async () => {
  for (const data of [[older], { sessions: [older] }, []]) {
    const rows = Array.isArray(data) ? data : data.sessions;
    const result = await loadReportData(async path => response(path.includes('/sessions') ? data : { ...summary, totals: { ...summary.totals, total_sessions: rows.length } }), controller().signal);
    assert.equal(result.sessions.length, rows.length);
  }
});

test('a failed checks request cannot publish a summary-only refresh', async () => {
  const calls = [];
  await assert.rejects(loadReportData(async path => { calls.push(path); return response(null, false); }, controller().signal), /checks could not be loaded/);
  assert.equal(calls.length, 1);
});

test('a failed summary request rejects the complete refresh instead of returning partial checks', async () => {
  await assert.rejects(loadReportData(async path => path.includes('/sessions') ? response([latest]) : response(null, false), controller().signal), /summary could not be loaded/);
});

test('malformed checks are errors, not an invented empty history', async () => {
  for (const data of [null, {}, { sessions: null }, [null], [{}], [{ session_id: '' }], [{ ...latest, proof: {} }]]) {
    await assert.rejects(loadReportData(async () => response(data), controller().signal), /checks could not be verified/);
  }
});

test('invalid or missing totals and timestamps cannot become a healthy zero-count report', async () => {
  for (const data of [null, {}, { ...summary, totals: {} }, { ...summary, server_time: 'invalid' },
    { ...summary, recurring_issues_count: -1 }, { ...summary, warning_signs_active_count: null },
    { ...summary, totals: { ...summary.totals, total_sessions: '2' } },
    { ...summary, totals: { ...summary.totals, this_month_count: NaN } },
    { ...summary, totals: { ...summary.totals, escalated_count: 0.5 } }]) {
    await assert.rejects(loadReportData(async path => response(path.includes('/sessions') ? [latest] : data), controller().signal), /summary could not be verified/);
  }
});

test('a summary with fewer total records than the loaded list is rejected, not silently mixed', async () => {
  await assert.rejects(loadReportData(async path => response(path.includes('/sessions') ? [latest, older] : { ...summary, totals: { ...summary.totals, total_sessions: 1 } }), controller().signal), /summary could not be verified/);
});

test('network and JSON failures reject without leaking partial report data', async () => {
  for (const failSummary of [false, true]) {
    await assert.rejects(loadReportData(async path => {
      if (path.includes('/sessions') && failSummary) return response([latest]);
      throw new Error('offline');
    }, controller().signal), /offline/);
  }
  await assert.rejects(loadReportData(async () => ({ ok: true, json: async () => { throw new SyntaxError('invalid JSON'); } }), controller().signal), SyntaxError);
});

test('an already canceled request never fetches, and cancellation between endpoints stops the refresh', async () => {
  const abort = controller();
  abort.abort();
  await assert.rejects(loadReportData(async () => { assert.fail('No request should be sent'); }, abort.signal), { name: 'AbortError' });
  const between = controller();
  let calls = 0;
  await assert.rejects(loadReportData(async () => {
    calls++;
    between.abort();
    return response([latest]);
  }, between.signal), { name: 'AbortError' });
  assert.equal(calls, 1);
});

test('a late canceled result cannot replace a newer successful refresh', async () => {
  const old = controller();
  let release;
  const pending = loadReportData(async path => {
    if (path.includes('/sessions')) return response([older]);
    await new Promise(resolve => { release = resolve; });
    return response(summary);
  }, old.signal);
  await new Promise(resolve => setImmediate(resolve));
  old.abort();
  const fresh = await loadReportData(async path => response(path.includes('/sessions') ? [latest] : summary), controller().signal);
  release();
  await assert.rejects(pending, { name: 'AbortError' });
  assert.equal(fresh.sessions[0].session_id, latest.session_id);
});
