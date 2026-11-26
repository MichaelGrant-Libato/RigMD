import test from 'node:test';
import assert from 'node:assert/strict';
import { chartReadings, formatAdapterLinkSpeed, formatMeasurement, keepDeviceSample, latestDeviceSample, trafficScale } from '../src/lib/deviceTelemetry.ts';

const now = Date.parse('2026-11-25T13:51:30Z');
const stamp = seconds => new Date(now + seconds * 1000).toISOString();

test('a rolling chart keeps measured idle and breaks across missing or stale CPU readings', () => {
  const points = chartReadings([
    { capturedAtUtc: stamp(-62), cpuUsageCapturedAtUtc: stamp(-62), cpuUsagePercent: 3 },
    { capturedAtUtc: stamp(-13), cpuUsageCapturedAtUtc: stamp(-13), cpuUsagePercent: 0 },
    { capturedAtUtc: stamp(-5), cpuUsageCapturedAtUtc: stamp(-5), cpuUsagePercent: 12 },
    { capturedAtUtc: stamp(-1), cpuUsageCapturedAtUtc: stamp(-7), cpuUsagePercent: 18 },
  ], now, sample => ({ value: sample.cpuUsagePercent, timestamp: sample.cpuUsageCapturedAtUtc }));

  assert.deepEqual(points, [
    { time: now - 13000, value: 0 },
    { time: now - 12999, value: null },
    { time: now - 5000, value: 12 },
    { time: now - 1000, value: null },
  ]);
});

test('late packets cannot roll back the cards and dropped connections clear live readouts', () => {
  const sample = { capturedAtUtc: stamp(0), ramUsedGb: 13 };
  const samples = keepDeviceSample([], sample, now);
  assert.equal(keepDeviceSample(samples, { capturedAtUtc: stamp(-1), ramUsedGb: 8 }, now), samples);
  assert.equal(latestDeviceSample({ samples, now, disconnected: false }), sample);
  assert.equal(latestDeviceSample({ samples, now, disconnected: true }), undefined);
  assert.equal(latestDeviceSample({ samples, now: now + 5001, disconnected: false }), undefined);
});

test('measurement text distinguishes an idle disk, missing counters, and small transfers', () => {
  assert.equal(formatMeasurement(0, ' KB/s', 1, 'Idle'), 'Idle');
  assert.equal(formatMeasurement(null, ' KB/s', 1, 'Idle'), 'Not available');
  assert.equal(formatMeasurement(0.024, ' KB/s', 1, 'Idle'), '<0.1 KB/s');
});

test('a network traffic spike changes chart units without uneven axis labels or invented points', () => {
  const chart = trafficScale([{ time: 1, value: 57723 }, { time: 2, value: null }, { time: 3, value: 0 }]);
  assert.equal(chart.unit, 'Mbps');
  assert.deepEqual(chart.ticks, [0, 20, 40, 60, 80]);
  assert.equal(chart.upperBound, 80);
  assert.deepEqual(chart.rows.map(row => row.value), [57.723, null, 0]);
  assert.equal(trafficScale([{ time: 1, value: 1.6 }]).unit, 'Kbps');
});

test('reported link speed formats a gigabit Ethernet link separately from traffic', () => {
  assert.equal(formatAdapterLinkSpeed(1000), '1 Gbps');
  assert.equal(formatAdapterLinkSpeed(100), '100 Mbps');
  assert.equal(formatAdapterLinkSpeed(0), 'Not available');
});
