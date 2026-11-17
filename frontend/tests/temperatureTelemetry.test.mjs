import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';
import { validTemperature, selectTemperature, formatTemperature, gpuTemperatureReading, selectedGpuTemperature } from '../src/lib/temperatureTelemetry.ts';

test('missing and invalid temperatures remain unavailable rather than becoming zero', () => {
  for (const value of [null, undefined, NaN, Infinity, -Infinity, -1, 151, '45', false]) {
    assert.equal(validTemperature(value), null);
    assert.equal(formatTemperature(value), 'Not available');
  }
  assert.equal(validTemperature(0), 0);
  assert.equal(formatTemperature(0), '0\u00b0C');
  assert.equal(formatTemperature(150), '150\u00b0C');
});

test('a snapshot is displayed only before the first live temperature sample', () => {
  assert.equal(selectTemperature(52, undefined, false), 52);
  assert.equal(formatTemperature(52, 61.5, true), '62\u00b0C');
  assert.equal(selectTemperature(52, 0, true), 0);
});

test('a new unavailable or invalid live reading clears a previously valid snapshot', () => {
  for (const live of [null, undefined, NaN, Infinity, -1, 151]) {
    assert.equal(selectTemperature(52, live, true), null);
    assert.equal(formatTemperature(52, live, true), 'Not available');
  }
});

const temperatureStamp = '2026-11-17T08:15:00Z';
const temperatureNow = Date.parse(temperatureStamp);
const gpuTemperature = {
  id: 'dedicated', name: 'Identical GPU name', type: 'Dedicated', usagePercent: 40, capturedAtUtc: temperatureStamp,
  temperatureCelsius: 51, temperatureSource: 'Windows graphics driver', temperatureCapturedAtUtc: temperatureStamp,
};

test('GPU temperature has its own measurement clock and never borrows the utilization clock', () => {
  assert.equal(gpuTemperatureReading(gpuTemperature, temperatureNow + 5000).celsius, 51);
  assert.equal(gpuTemperatureReading(gpuTemperature, temperatureNow - 2000).celsius, 51);
  assert.equal(gpuTemperatureReading(gpuTemperature, temperatureNow + 5001), null);
  assert.equal(gpuTemperatureReading(gpuTemperature, temperatureNow - 2001), null);
  for (const temperatureCapturedAtUtc of [null, undefined, '', 'invalid']) {
    assert.equal(gpuTemperatureReading({ ...gpuTemperature, temperatureCapturedAtUtc }, temperatureNow), null);
  }
  for (const temperatureCelsius of [null, undefined, NaN, Infinity, -1, 151, '51']) {
    assert.equal(gpuTemperatureReading({ ...gpuTemperature, temperatureCelsius }, temperatureNow), null);
  }
  assert.equal(gpuTemperatureReading({ ...gpuTemperature, capturedAtUtc: null }, temperatureNow).celsius, 51);
});

test('GPU temperature is selected by adapter identity even when hybrid adapter names are identical', () => {
  const other = { ...gpuTemperature, id: 'integrated', type: 'Integrated', temperatureCelsius: 36 };
  const gpu = { adapter_id: gpuTemperature.id, adapters: [other, gpuTemperature] };
  assert.equal(selectedGpuTemperature(gpu, undefined, temperatureNow).celsius, 51);
  assert.equal(selectedGpuTemperature(gpu, [other], temperatureNow), null);
  assert.equal(selectedGpuTemperature({ ...gpu, adapter_id: null }, undefined, temperatureNow), null);
  assert.equal(selectedGpuTemperature(gpu, [gpuTemperature, gpuTemperature], temperatureNow), null);
  assert.equal(selectedGpuTemperature(gpu, [{ ...gpuTemperature, temperatureCelsius: null }], temperatureNow), null);
  assert.equal(selectedGpuTemperature(gpu, [], temperatureNow), null);
});

test('device info and GPU detail render the selected adapter temperature with its source', async () => {
  const vite = await createServer({
    root: fileURLToPath(new URL('..', import.meta.url)), configFile: false, appType: 'custom',
    server: { middlewareMode: true, hmr: false, watch: null, ws: false }, optimizeDeps: { noDiscovery: true },
  });
  try {
    const { default: SystemProfileView } = await vite.ssrLoadModule('/src/pages/SystemProfileView.tsx');
    const { default: DeviceDetailModal } = await vite.ssrLoadModule('/src/components/DeviceDetailModal.tsx');
    const temperatureCapturedAtUtc = new Date().toISOString();
    const selected = { ...gpuTemperature, temperatureCapturedAtUtc };
    const stats = {
      cpu: { name: 'Processor', usage_percent: 20, frequency_mhz: 3000, cores: 6, threads: 12 },
      ram: { total_gb: 16, usage_percent: 50 }, disk: { total_gb: 100, used_gb: 50, usage_percent: 50 },
      gpu: { name: selected.name, adapter_id: selected.id, driver: 'Driver', type: 'Dedicated', vram_gb: 8,
        adapters: [{ ...selected, id: 'integrated', type: 'Integrated', temperatureCelsius: 90 }, selected] },
      storage_drives: [], storage_type: 'SSD', os_version: 'Windows 11', device_name: 'Test PC', chipset_driver: 'Board', system_age: '1 year',
    };
    const profile = renderToStaticMarkup(createElement(SystemProfileView, {
      stats, error: null, hardwareUpdatedAt: new Date(), isRefreshingHardware: false, onRefreshHardware: async () => {},
    }));
    assert.match(profile, /Temperature: 51°C/);
    assert.doesNotMatch(profile, /Temperature: 90°C/);
    assert.match(profile, /title="Windows graphics driver/);
    const modal = renderToStaticMarkup(createElement(DeviceDetailModal, { stats, isOpen: true, hardwareType: 'GPU', onClose() {} }));
    assert.match(modal, /51°C/);
    assert.doesNotMatch(modal, /90°C/);
    assert.match(modal, /title="Windows graphics driver/);
  } finally {
    await vite.close();
  }
});
