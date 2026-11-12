import test from 'node:test';
import assert from 'node:assert/strict';
import { reviewOnlyNotice, workloadGuidanceNotice, memoryActionDisposition } from '../src/lib/repairSupport.ts';

test('heat battery and processor workload show specific investigation guidance', () => {
  for (const category of ['Thermal condition', 'Battery concern', 'Elevated CPU Utilization']) {
    assert.match(reviewOnlyNotice(category), /memory check/i);
    assert.equal(workloadGuidanceNotice(category), reviewOnlyNotice(category));
  }
  assert.match(reviewOnlyNotice('Thermal condition'), /airflow/);
  assert.match(reviewOnlyNotice('Battery concern'), /charging/);
  assert.match(reviewOnlyNotice('Elevated CPU Utilization'), /Task Manager/);
});

test('unavailable measurements remain unavailable and independent memory scope remains enforced', () => {
  assert.match(reviewOnlyNotice('Thermal readings unavailable'), /unavailable measurements/);
  const memory = [{ metric_id: 'memory_usage', value: '90%', status: 'high', evidence_kind: 'measurement' }];
  assert.equal(memoryActionDisposition({ diagnosis_mode: 'scenario', scenario_id: 'overheating-loud-fan' }, memory), 'handoff');
  assert.equal(memoryActionDisposition({ diagnosis_mode: 'scenario', scenario_id: 'rapid-battery-drain' }, memory), 'handoff');
  assert.equal(memoryActionDisposition({ diagnosis_mode: 'component', component_ids: ['memory'] }, memory), 'direct');
  assert.equal(reviewOnlyNotice('High Memory Pressure'), null);
});
