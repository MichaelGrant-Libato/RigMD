import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';
import { gpuUsagePercent, mergeLiveUsage, measuredPercent } from '../src/lib/liveReadings.ts';
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

test('CPU temperature refreshes independently and clears lost sensors without changing CPU identity', () => {
  const stamp = '2026-11-17T08:15:00Z';
  const now = Date.parse(stamp);
  const sample = { cpuTempCelsius: 58.5, cpuTemperatureSource: 'LibreHardwareMonitor 0.9.6 / PawnIO',
    cpuTemperatureSensorIdentifier: '/intelcpu/0/temperature/0', cpuTemperatureCapturedAtUtc: stamp };
  const original = { cpu: { name: 'Intel Processor', cores: 12 }, ram: { total_gb: 16 }, gpu: { adapters: [] } };
  const current = mergeLiveUsage(original, { ...sample, cpuUsagePercent: 45, ramUsagePercent: 50, ramUsedGb: 8 }, now);
  assert.equal(current.cpu.name, 'Intel Processor');
  assert.equal(current.cpu.cores, 12);
  assert.equal(current.cpu.usage_percent, 45);
  assert.equal(current.ram.used_gb, 8);
  assert.equal(current.cpu.temperature_celsius, 58.5);
  assert.equal(current.cpu.temperature_sensor_identifier, '/intelcpu/0/temperature/0');
  assert.equal(current.cpu.temperature_source, sample.cpuTemperatureSource);
  assert.equal(original.cpu.temperature_celsius, undefined);

  for (const payload of [{}, { ...sample, cpuTempCelsius: null }, { ...sample, cpuTemperatureCapturedAtUtc: '2026-11-17T08:14:54Z', cpuUsagePercent: 20 }]) {
    const missing = mergeLiveUsage(current, payload, now);
    assert.equal(missing.cpu.temperature_celsius, null);
    assert.equal(missing.cpu.temperature_source, 'Unavailable');
    assert.equal(missing.cpu.temperature_sensor_identifier, null);
    assert.equal(missing.cpu.temperature_captured_at_utc, null);
    assert.equal(missing.cpu.name, 'Intel Processor');
  }
});

const gpuCapturedAt = '2026-11-17T08:15:00Z';
const gpuNow = Date.parse(gpuCapturedAt);
const unavailableTemperature = { temperatureCelsius: null, temperatureSource: 'Unavailable', temperatureCapturedAtUtc: null };
const integrated = { id: 'adapter-1', name: 'Intel Graphics', type: 'Integrated', usagePercent: 74, capturedAtUtc: gpuCapturedAt, ...unavailableTemperature };
const dedicated = { id: 'adapter-2', name: 'GeForce', type: 'Dedicated', usagePercent: 16, capturedAtUtc: gpuCapturedAt, ...unavailableTemperature };

test('GPU usage requires an independently timestamped, valid adapter measurement', () => {
  assert.equal(gpuUsagePercent(integrated, gpuNow + 5000), 74);
  assert.equal(gpuUsagePercent(integrated, gpuNow - 2000), 74);
  assert.equal(gpuUsagePercent(integrated, gpuNow + 5001), null);
  assert.equal(gpuUsagePercent(integrated, gpuNow - 2001), null);
  for (const capturedAtUtc of [null, undefined, '', 'invalid']) {
    assert.equal(gpuUsagePercent({ ...integrated, capturedAtUtc }, gpuNow), null);
  }
  for (const usagePercent of [null, undefined, NaN, Infinity, -1, 101, '0']) {
    assert.equal(gpuUsagePercent({ ...dedicated, usagePercent }, gpuNow), null);
  }
});

test('adapter-specific updates keep idle and busy GPUs distinct while CPU and RAM continue updating', () => {
  const stats = { cpu: {}, ram: { total_gb: 16 }, gpu: { adapters: [integrated, dedicated] } };
  const idleIntegrated = { ...integrated, usagePercent: 0 };
  const busyDedicated = { ...dedicated, usagePercent: 100 };
  const next = mergeLiveUsage(stats, {
    cpuUsagePercent: 32, ramUsagePercent: 75, ramUsedGb: 12,
    gpuAdapters: [busyDedicated, idleIntegrated],
  }, gpuNow);

  assert.deepEqual(next.gpu.adapters.find(a => a.id === integrated.id), idleIntegrated);
  assert.deepEqual(next.gpu.adapters.find(a => a.id === dedicated.id), busyDedicated);
  assert.equal(stats.gpu.adapters[0].usagePercent, 74);
  assert.equal(next.cpu.usage_percent, 32);
  assert.equal(next.ram.usage_percent, 75);
  assert.equal(next.ram.used_gb, 12);

  const partial = mergeLiveUsage(next, { gpuAdapters: [
    { ...integrated, capturedAtUtc: '2026-11-17T08:14:00Z' }, dedicated,
  ] }, gpuNow);
  assert.equal(partial.gpu.adapters.find(a => a.id === integrated.id).usagePercent, null);
  assert.equal(partial.gpu.adapters.find(a => a.id === dedicated.id).usagePercent, 16);
});

test('a legacy aggregate preserves adapter identities without assigning usage, and an empty inventory clears them', () => {
  const stats = { cpu: {}, ram: { total_gb: 16 }, gpu: { name: 'GeForce', adapters: [integrated, dedicated] } };
  const legacy = mergeLiveUsage(stats, { gpuUsagePercent: 93 }, gpuNow);
  assert.deepEqual(legacy.gpu.adapters, [
    { ...integrated, usagePercent: null, capturedAtUtc: null },
    { ...dedicated, usagePercent: null, capturedAtUtc: null },
  ]);
  assert.equal(legacy.gpu.name, 'GeForce');
  assert.deepEqual(mergeLiveUsage(legacy, { gpuAdapters: [], gpuUsagePercent: 93 }, gpuNow).gpu.adapters, []);
});

test('GPU temperature clears on absent or expired telemetry independently of fresh activity', () => {
  const warm = { ...dedicated, temperatureCelsius: 51, temperatureSource: 'Windows graphics driver', temperatureCapturedAtUtc: gpuCapturedAt };
  const stats = { cpu: {}, ram: { total_gb: 16 }, gpu: { adapters: [warm] } };
  const fresh = mergeLiveUsage(stats, { gpuAdapters: [warm] }, gpuNow);
  assert.equal(fresh.gpu.adapters[0].temperatureCelsius, 51);
  assert.equal(fresh.gpu.adapters[0].temperatureSource, 'Windows graphics driver');
  const expired = mergeLiveUsage(stats, { gpuAdapters: [{ ...warm, capturedAtUtc: '2026-11-17T08:15:06Z' }] }, gpuNow + 6000);
  assert.equal(expired.gpu.adapters[0].usagePercent, 16);
  assert.equal(expired.gpu.adapters[0].temperatureCelsius, null);
  for (const sample of [{}, { gpuAdapters: [{ ...warm, temperatureCelsius: null }] }]) {
    const missing = mergeLiveUsage(stats, sample, gpuNow).gpu.adapters[0];
    assert.equal(missing.temperatureCelsius, null);
    assert.equal(missing.temperatureCapturedAtUtc, null);
    assert.equal(missing.temperatureSource, 'Unavailable');
  }
});

test('GPU card labels each adapter’s own activity and distinguishes measured idle from unavailable', async () => {
  const vite = await createServer({
    root: fileURLToPath(new URL('..', import.meta.url)), configFile: false, appType: 'custom',
    server: { middlewareMode: true, hmr: false, watch: null, ws: false }, optimizeDeps: { noDiscovery: true },
  });
  try {
    const { default: GpuCard } = await vite.ssrLoadModule('/src/components/dashboard/GpuCard.tsx');
    const capturedAtUtc = new Date().toISOString();
    const adapters = [
      { ...integrated, usagePercent: 0, capturedAtUtc },
      { ...dedicated, usagePercent: 82, capturedAtUtc },
    ];
    const render = stats => renderToStaticMarkup(createElement(GpuCard, { stats }));
    const hybrid = render({ gpu: { adapters } });
    assert.match(hybrid, /aria-label="Intel Graphics activity"[^>]*aria-valuenow="0"/);
    assert.match(hybrid, /aria-label="GeForce activity"[^>]*aria-valuenow="82"/);
    assert.match(hybrid, /Integrated graphics/);
    assert.match(hybrid, /Dedicated graphics/);

    const unavailable = render({ gpu: { name: 'GeForce', adapters: [] } });
    assert.match(unavailable, /Usage unavailable/i);
    assert.doesNotMatch(unavailable, /aria-valuenow=|>0%<|>82%<|Intel Graphics/);
  } finally {
    await vite.close();
  }
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
