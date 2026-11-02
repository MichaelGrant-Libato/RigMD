import test from 'node:test';
import assert from 'node:assert/strict';
import { summarizePerformance, targetFor, performanceExport, unavailableReadingCount } from '../src/lib/performanceMetrics.ts';

const record = (overrides = {}) => ({ id: 'id', createdAt: '2026-11-01T00:00:00Z', operation: 'history.ui', outcome: 'completed', durationMs: 3000,
  recordCount: 50, unavailableCount: null, mode: 'normal', engine: 'unknown', appVersion: '0.2.3', buildConfiguration: 'Release', deviceKey: 'pc-one', osVersion: 'Windows', architecture: 'X64', logicalProcessors: 8, ...overrides });

test('SRS boundaries are inclusive and history needs 50 displayed records', () => {
  assert.equal(targetFor(record()), 3000);
  assert.equal(targetFor(record({ recordCount: 49 })), null);
  assert.equal(targetFor(record({ recordCount: null })), null);
  for (const operation of ['startup.desktop', 'report.ui']) assert.equal(targetFor(record({ operation })), 5000);
  for (const operation of ['history.backend', 'scan.ui', 'scan.backend', 'pdf.desktop', 'pdf_request.ui', 'text.ui']) assert.equal(targetFor(record({ operation })), null);
  for (const outcome of ['failed', 'incomplete', 'canceled']) assert.equal(targetFor(record({ outcome })), null);
  const summary = summarizePerformance([record(), record({ durationMs: 3000.01 })])[0];
  assert.equal(summary.withinTarget, 1); assert.equal(summary.overTarget, 1);
});

test('failed, canceled, missing and nonfinite measurements cannot inflate timing passes', () => {
  const summary = summarizePerformance([record(), record({ outcome: 'failed', durationMs: 1 }), record({ outcome: 'canceled' }), record({ outcome: 'incomplete' }), record({ recordCount: 10 }), record({ durationMs: NaN })])[0];
  assert.equal(summary.runs, 5); assert.equal(summary.eligible, 1); assert.equal(summary.withinTarget, 1);
  assert.equal(summary.failed, 1); assert.equal(summary.canceled, 1); assert.equal(summary.incomplete, 1);
});

test('versions, configurations, computers, OS and operation boundaries stay separate', () => {
  const rows = [record(), record({ appVersion: '0.2.4' }), record({ buildConfiguration: 'Debug' }), record({ deviceKey: 'pc-two' }), record({ osVersion: 'other' }), record({ operation: 'history.backend' })];
  assert.equal(summarizePerformance(rows).length, 6);
});

test('percentiles use observed durations and never hide outcome counts', () => {
  const rows = Array.from({ length: 20 }, (_, i) => record({ durationMs: (i + 1) * 100 }));
  const summary = summarizePerformance(rows)[0];
  assert.equal(summary.medianMs, 1050); assert.equal(summary.p95Ms, 1900); assert.equal(summary.maxMs, 2000);
  assert.equal(summarizePerformance([record({ outcome: 'failed' })])[0].medianMs, null);
  assert.deepEqual(summarizePerformance([]), []);
});

test('export preserves actual samples, limitations, context and logging failures without fabricated passes', () => {
  const rows = [record({ durationMs: 3100 })]; const value = JSON.parse(performanceExport(rows, { dropped: 2 }, 2000));
  assert.deepEqual(value.records, rows); assert.equal(value.summaries[0].overTarget, 1); assert.equal(value.health.dropped, 2);
  assert.match(value.method, /not overall acceptance certification/); assert.equal(value.retentionLimit, 2000);
});

test('unavailable evidence is not confused with a measured warning or absent hardware', () => {
  assert.equal(unavailableReadingCount([{ status: 'high' }, { status: 'unknown' }, { evidence_kind: 'not_assessed' }, { status: 'info', evidence_kind: 'not_present' }]), 2);
  assert.equal(unavailableReadingCount(null), 0);
});
