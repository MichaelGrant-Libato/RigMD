import test from 'node:test';
import assert from 'node:assert/strict';
import { createMeasurement, afterScreenPaint } from '../src/lib/performanceMeasurement.ts';

function source(initial = 'visible') {
  const listeners = new Set();
  return { visibilityState: initial, listeners,
    addEventListener(type, listener) { assert.equal(type, 'visibilitychange'); listeners.add(listener); },
    removeEventListener(type, listener) { listeners.delete(listener); },
    change(value) { this.visibilityState = value; for (const listener of listeners) listener(); },
  };
}
test('duration uses the monotonic clock and completion is recorded exactly once', () => {
  let clock = 100; const rows = []; const doc = source();
  const run = createMeasurement((...row) => rows.push(row), { mode: 'history_50' }, () => clock, doc);
  clock = 3100; run.finish('completed', { recordCount: 50 }); clock = 999999; run.finish('failed');
  assert.deepEqual(rows, [[3000, 'completed', { mode: 'history_50', recordCount: 50 }]]);
  assert.equal(doc.listeners.size, 0);
});
test('backgrounding at any point makes an otherwise completed run incomplete', () => {
  for (const initial of ['visible', 'hidden']) {
    const doc = source(initial); const rows = [];
    const run = createMeasurement((...row) => rows.push(row), {}, () => 20, doc);
    doc.change('hidden'); doc.change('visible'); run.finish();
    assert.equal(rows[0][1], 'incomplete'); assert.equal(doc.listeners.size, 0);
  }
});
test('backgrounding cannot turn an error or cancellation into a timing pass', () => {
  for (const outcome of ['failed', 'canceled', 'incomplete']) {
    const rows = []; const doc = source('hidden');
    const run = createMeasurement((...row) => rows.push(row), {}, () => 0, doc); run.finish(outcome);
    assert.equal(rows[0][1], outcome); assert.equal(doc.listeners.size, 0);
  }
});
test('screen-ready callback needs two frames and can be canceled before either frame', () => {
  let next = 1; const callbacks = new Map(); let painted = 0;
  const schedule = callback => { const id = next++; callbacks.set(id, callback); return id; };
  const cancel = id => callbacks.delete(id);
  const frame = () => { const batch = [...callbacks.values()]; callbacks.clear(); for (const callback of batch) callback(0); };
  afterScreenPaint(() => painted++, schedule, cancel); assert.equal(painted, 0);
  frame(); assert.equal(painted, 0); frame(); assert.equal(painted, 1);
  const cancelFirst = afterScreenPaint(() => painted++, schedule, cancel); cancelFirst(); frame(); frame(); assert.equal(painted, 1);
  const cancelSecond = afterScreenPaint(() => painted++, schedule, cancel); frame(); cancelSecond(); frame(); assert.equal(painted, 1);
});
