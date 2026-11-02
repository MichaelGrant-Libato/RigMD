import test from 'node:test';
import assert from 'node:assert/strict';
import { appSpeedSummary, friendlyBuildLabel, outcomeLabel, secondsLabel } from '../src/lib/performancePresentation.ts';
import { measurementContext, performanceExport } from '../src/lib/performanceMetrics.ts';

const record = (overrides = {}) => ({ id: 'one', createdAt: '2026-11-01T01:00:00Z', operation: 'startup.desktop', outcome: 'completed', durationMs: 5000,
  recordCount: null, unavailableCount: null, mode: 'normal', engine: 'unknown', httpStatus: null, apiWorkingSetMb: null, apiCpuTimeMs: null,
  appVersion: '0.2.3+commit123', buildConfiguration: 'Debug', deviceKey: 'pc-one', osVersion: 'Windows', architecture: 'X64', logicalProcessors: 8, ...overrides });
const summary = rows => appSpeedSummary(rows, measurementContext(record()));

test('plain-language cards show actual latest timings rather than a median or a fabricated pass', () => {
  const rows = [record({ durationMs: 18060 }), record({ id: 'newer', createdAt: '2026-11-01T02:00:00Z', durationMs: 13010 })];
  const startup = summary(rows)[0];
  assert.equal(startup.latest.durationMs, 13010);
  assert.equal(startup.status, 'over');
  assert.equal(startup.withinTarget, 0);
  assert.equal(startup.overTarget, 2);
  assert.equal(secondsLabel(startup.latest.durationMs), '13.01 seconds');
});

test('history cards require at least 50 displayed checks and do not substitute backend timing', () => {
  const rows = [record({ operation: 'history.ui', recordCount: 10, durationMs: 100 }), record({ operation: 'history.backend', recordCount: 136, durationMs: 50 })];
  assert.equal(summary(rows)[1].status, 'unmeasured');
  rows.push(record({ operation: 'history.ui', recordCount: 50, durationMs: 3000 }));
  const history = summary(rows)[1];
  assert.equal(history.status, 'within');
  assert.equal(history.latest.recordCount, 50);
  assert.equal(history.measured, 1);
});

test('a fast latest timing keeps earlier slow timings visible in the counts', () => {
  const startup = summary([record({ durationMs: 6000 }), record({ id: 'new', createdAt: '2026-11-01T03:00:00Z', durationMs: 4000 })])[0];
  assert.equal(startup.status, 'within');
  assert.equal(startup.withinTarget, 1);
  assert.equal(startup.overTarget, 1);
  assert.equal(startup.measured, 2);
});

test('incomplete, failed, and canceled attempts remain visible and cannot pass a timing target', () => {
  const rows = ['incomplete', 'failed', 'canceled'].map((outcome, i) => record({ id: outcome, outcome, createdAt: `2026-11-01T0${i + 2}:00:00Z`, durationMs: 1 }));
  const startup = summary(rows)[0];
  assert.equal(startup.status, 'unmeasured');
  assert.equal(startup.latest, null);
  assert.equal(startup.measured, 0);
  assert.equal(startup.incomplete, 1); assert.equal(startup.failed, 1); assert.equal(startup.canceled, 1);
  assert.equal(startup.latestAttempt.outcome, 'canceled');
});

test('each selected setup stays separate and invalid durations cannot appear as fast results', () => {
  const rows = [record({ durationMs: 9000 }), record({ buildConfiguration: 'Release', durationMs: 10 }), record({ deviceKey: 'another-pc', durationMs: 10 }), record({ durationMs: NaN }), record({ durationMs: -1 }), record({ durationMs: Infinity })];
  assert.equal(summary(rows)[0].measured, 1);
  assert.equal(summary(rows)[0].status, 'over');
});

test('friendly setup names hide implementation identifiers while the download retains full evidence', () => {
  const row = record();
  assert.equal(friendlyBuildLabel(row, 0), 'Version 0.2.3 · Development build · Saved setup 1');
  assert.match(friendlyBuildLabel(record({ buildConfiguration: 'Release' }), 1), /Release build · Saved setup 2/);
  const before = structuredClone(row);
  summary([row]);
  const exported = JSON.parse(performanceExport([row], {}, 2000));
  assert.deepEqual(exported.records, [before]);
  assert.equal(exported.records[0].appVersion, '0.2.3+commit123');
});

test('empty records offer all three tasks without claiming they were measured', () => {
  const cards = summary([]);
  assert.deepEqual(cards.map(card => card.title), ['Opening RigMD', 'Opening 50 saved checks', 'Preparing your report']);
  assert.ok(cards.every(card => card.latest === null && card.status === 'unmeasured' && card.measured === 0));
  assert.equal(outcomeLabel('incomplete'), 'Not fully measured');
  assert.equal(secondsLabel(0), '0.00 seconds');
});
