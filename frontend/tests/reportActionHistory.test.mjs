import test from 'node:test';
import assert from 'node:assert/strict';
import { loadReportActionHistory } from '../src/lib/reportActionHistory.ts';
const expected = { session_id: 'saved-check', device_id: 'test-device', created_at: '2026-11-13T00:00:00Z' };
const run = { run_id: 'run-one', status: 'Completed', created_at: expected.created_at,
  attempts: [{ action_code: 'close_selected_app', created_at: expected.created_at, verification_status: 'Unknown',
    execution_summary: 'Normal close requested.', proof: [{label: 'Test app', after: 'Processes remained'}] }] };
const detail = () => ({ ...expected, remediation_history: [structuredClone(run)] });
const response = data => ({ ok: true, json: async () => data });
test('only selected check detail is requested without cache and saved uncertainty is retained', async () => {
  const signal = new AbortController().signal;
  const result = await loadReportActionHistory(async (path, options) => {
    assert.equal(path, '/api/diagnosis/sessions/saved-check');
    assert.equal(options.signal, signal); assert.equal(options.cache, 'no-store');
    return response(detail());
  }, expected, signal);
  assert.deepEqual(result, { runs: [run] });
});
test('a verified empty action history is distinct from unavailable history', async () => {
  const result = await loadReportActionHistory(async () => response({...expected, remediation_history: []}), expected, new AbortController().signal);
  assert.deepEqual(result, {runs: []});
});
test('actions from another check, device or original timestamp cannot enter this report', async () => {
  for (const change of [{session_id: 'other'}, {device_id: 'other'}, {created_at: '2026-11-12T00:00:00Z'}]) {
    await assert.rejects(loadReportActionHistory(async () => response({...detail(), ...change}), expected, new AbortController().signal), /could not be verified/);
  }
});
test('malformed or duplicate action runs cannot become a completed report', async () => {
  for (const history of [null, {}, [null], [run, run], [{...run, attempts: null}],
    [{...run, attempts: [{...run.attempts[0], proof: [{after: {bad: true}}]}]}],
    [{...run, attempts: [{...run.attempts[0], verification_status: true}]}]]) {
    await assert.rejects(loadReportActionHistory(async () => response({...expected, remediation_history: history}), expected, new AbortController().signal), /could not be verified/);
  }
});
test('failed detail and invalid JSON stay unavailable, never verified empty', async () => {
  await assert.rejects(loadReportActionHistory(async () => ({ok: false}), expected, new AbortController().signal), /could not be loaded/);
  await assert.rejects(loadReportActionHistory(async () => ({ok: true, json: async () => {throw new SyntaxError('invalid');}}), expected, new AbortController().signal), SyntaxError);
});
test('canceled or late old-check responses cannot publish action history', async () => {
  const controller = new AbortController(); controller.abort();
  await assert.rejects(loadReportActionHistory(async () => { assert.fail('No request'); }, expected, controller.signal), {name: 'AbortError'});
  const late = new AbortController();
  await assert.rejects(loadReportActionHistory(async () => { late.abort(); return response(detail()); }, expected, late.signal), {name: 'AbortError'});
});
