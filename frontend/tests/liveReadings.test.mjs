import test from 'node:test';
import assert from 'node:assert/strict';
import { mergeLiveUsage, measuredPercent } from '../src/lib/liveReadings.ts';
import { readingPercent, readingTone, explanationSourceLabel } from '../src/lib/diagnosticReadingVisuals.ts';
import { requiresHardwareInspection } from '../src/lib/repairSupport.ts';

test('late or invalid CPU samples do not look current just because an event arrived', () => {
  const stats = { cpu: {}, ram: { total_gb: 16 } };
  const now = Date.parse('2026-10-20T12:00:00Z');
  for (const stamp of ['invalid', '2026-10-20T11:59:54Z', '2026-10-20T12:00:03Z']) {
    assert.equal(mergeLiveUsage(stats, { cpuUsagePercent: 100, cpuUsageCapturedAtUtc: stamp }, now).cpu.usage_percent, null);
  }
  assert.equal(mergeLiveUsage(stats, { cpuUsagePercent: 32, cpuUsageCapturedAtUtc: '2026-10-20T11:59:59Z' }, now).cpu.usage_percent, 32);
});

test('fresh CPU and RAM replace a stale startup snapshot together, without rounding usable memory to installed memory', () => {
  const original = { cpu: { usage_percent: 100, name: 'CPU' }, ram: { usage_percent: 93, used_gb: 14.7, total_gb: 15.7 } };
  const next = mergeLiveUsage(original, { cpuUsagePercent: 53, ramUsagePercent: 92, ramUsedGb: 14.4, ramTotalGb: 15.7 });
  assert.equal(next.cpu.usage_percent, 53);
  assert.equal(next.ram.used_gb, 14.4);
  assert.equal(next.ram.total_gb, 15.7);
  assert.equal(original.cpu.usage_percent, 100);
  assert.equal(mergeLiveUsage(next, {}).cpu.usage_percent, null);
  assert.equal(mergeLiveUsage(next, {}).ram.used_gb, null);
});

test('measured zero and 100 are real values; missing or invalid samples are unavailable', () => {
  for (const input of [null, undefined, NaN, Infinity, -1, 101, '53']) assert.equal(measuredPercent(input), null);
  assert.equal(measuredPercent(0), 0);
  assert.equal(measuredPercent(100), 100);
});

test('reading bars use identified measured percentages only, without inventing temperatures or diagnoses', () => {
  assert.equal(readingPercent({ metric_id: 'memory_usage', value: '88.9% (14 / 15.7 GB)' }), 88.9);
  assert.equal(readingPercent({ metric_id: 'cpu_usage', value: '0%' }), 0);
  for (const value of ['Unavailable', '101%', '-5%', 'NaN%']) assert.equal(readingPercent({ metric_id: 'cpu_usage', value }), null);
  assert.equal(readingPercent({ metric_id: 'cpu_temperature', value: '50 C' }), null);
  assert.equal(readingTone('unknown'), 'unknown');
  assert.equal(readingTone('high'), 'attention');
});

test('old or unknown explanations are never attributed to Gemini', () => {
  assert.equal(explanationSourceLabel(), 'Source not recorded');
  assert.equal(explanationSourceLabel('local'), 'Built-in guidance (not Gemini)');
  assert.equal(explanationSourceLabel('gemini', 'test-model'), 'Gemini (test-model)');
});

test('hardware referral requires a hardware warning, not high workload or missing temperature', () => {
  for (const category of ['Storage health behavior', 'Drive health warning', 'SMART failure']) assert.equal(requiresHardwareInspection(category), true);
  for (const category of ['High Memory Pressure', 'Elevated CPU Utilization', 'Thermal telemetry unavailable', 'No Active Issue Detected']) assert.equal(requiresHardwareInspection(category), false);
});
