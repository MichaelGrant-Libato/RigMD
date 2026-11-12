import test from 'node:test';
import assert from 'node:assert/strict';
import { parseHomeOverview, essentialHomeReady, overviewPills, fullInventoryNeeded, sensorStreamingNeeded } from '../src/lib/homeOverview.ts';
import { appSpeedSummary } from '../src/lib/performancePresentation.ts';
import { measurementContext } from '../src/lib/performanceMetrics.ts';
const snapshot = () => ({ scope: 'essential', capturedAt: '2026-11-10T00:00:00Z', deviceName: 'TEST-PC',
  cpu: { name: 'Test CPU', usagePercent: 0, state: 'measured', source: 'Windows processor busy time (native)', capturedAt: '2026-11-10T00:00:00Z', windowSeconds: 1 },
  memory: { totalGb: 16, usedGb: 0, usagePercent: 0 }, storageQuerySucceeded: true,
  volumes: [{ drive: 'X:\\', totalGb: 100, usedGb: 0, usagePercent: 0 }],
  network: { querySucceeded: true, hasActiveAdapter: true, hasIpv4Address: true, hasDefaultGateway: true, hasDnsServers: true, kind: 'wired', ipAddress: '192.0.2.10' } });

test('Home preserves measured zero readings and does not certify the network from link metadata', () => {
  const data = parseHomeOverview(snapshot()); assert.equal(data.cpu.usagePercent, 0); assert.equal(data.memory.usedGb, 0);
  assert.equal(essentialHomeReady(data), true); assert.equal(overviewPills(data).find(pill => pill.label === 'Network').variant, 'neutral');
});
test('warm-up is not ready; explicitly unavailable readings stay null and neutral', () => {
  const data = snapshot(); data.cpu = { ...data.cpu, usagePercent: null, state: 'warming', capturedAt: null, windowSeconds: null };
  assert.equal(essentialHomeReady(parseHomeOverview(data)), false);
  data.cpu.state = 'unavailable'; data.memory = { totalGb: null, usedGb: null, usagePercent: null }; data.volumes = []; data.storageQuerySucceeded = false;
  assert.equal(essentialHomeReady(parseHomeOverview(data)), true);
  assert.equal(overviewPills(data).find(pill => pill.label === 'Memory').variant, 'neutral');
  assert.equal(overviewPills(data).find(pill => pill.label === 'Storage').variant, 'neutral');
});
test('malformed essentials cannot become an empty healthy snapshot', () => {
  for (const value of [null, {}, { ...snapshot(), scope: 'full' }, { ...snapshot(), capturedAt: 'invalid' }]) assert.throws(() => parseHomeOverview(value));
  for (const value of [NaN, Infinity, -1, 101, '25']) { const data = snapshot(); data.cpu.usagePercent = value; assert.throws(() => parseHomeOverview(data)); }
  const missing = snapshot(); missing.cpu.capturedAt = null; assert.throws(() => parseHomeOverview(missing));
  const infiniteWindow = snapshot(); infiniteWindow.cpu.windowSeconds = Infinity; assert.throws(() => parseHomeOverview(infiniteWindow));
  const impossible = snapshot(); impossible.memory.usedGb = 17; assert.throws(() => parseHomeOverview(impossible));
});
test('unknown or partial volumes cannot earn a normal storage indicator; measured high usage stays visible', () => {
  const data = snapshot(); data.volumes.push({ drive: 'Y:\\', totalGb: null, usedGb: null, usagePercent: null });
  assert.equal(overviewPills(parseHomeOverview(data)).find(pill => pill.label === 'Storage').variant, 'neutral');
  data.volumes[0].usagePercent = 90; data.volumes[0].usedGb = 90;
  assert.equal(overviewPills(parseHomeOverview(data)).find(pill => pill.label === 'Storage').variant, 'warning');
});
test('full inventory is requested for device details and reports; sensors also run during checks', () => {
  for (const page of ['home', 'settings', 'appSpeed', 'diagnosticHistory', 'help']) { assert.equal(fullInventoryNeeded(page), false); assert.equal(sensorStreamingNeeded(page), false); }
  for (const page of ['systemProfile', 'reports']) { assert.equal(fullInventoryNeeded(page), true); assert.equal(sensorStreamingNeeded(page), true); }
  assert.equal(sensorStreamingNeeded('newDiagnosis'), true);
});
test('essential startup timing names its scope and retains failed target observations', () => {
  const row = { id: 'one', createdAt: '2026-11-10T00:00:00Z', operation: 'startup.desktop', outcome: 'completed', durationMs: 6000, mode: 'essential_home', appVersion: '0.2.11', buildConfiguration: 'Release', deviceKey: 'test', osVersion: 'Windows', architecture: 'X64', logicalProcessors: 4 };
  const result = appSpeedSummary([row], measurementContext(row))[0];
  assert.match(result.description, /Home essential readings/); assert.equal(result.status, 'over'); assert.equal(result.overTarget, 1);
});
