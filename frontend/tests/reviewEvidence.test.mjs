import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { REVIEW_THRESHOLDS, NETWORK_THRESHOLDS, networkReviewStatus, percentReviewStatus } from '../src/lib/reviewThresholds.ts';
import { friendlyConfidence, readingEvidenceLabel, getSnapshotReadings } from '../src/lib/diagnosticLanguage.ts';
import { getResolutionCategory } from '../src/lib/pastChecksFormatting.ts';
import { storageReviewStatus } from '../src/lib/storageTelemetry.ts';

test('frontend workload policies match Windows scanner constants at boundaries', () => {
  const rules = name => readFileSync(new URL(`../../backend-dotnet/RigMD.Application/Services/${name}.cs`, import.meta.url), 'utf8');
  assert.equal(REVIEW_THRESHOLDS.cpu, Number(/Elevated = (\d+)/.exec(rules('CpuLoadThresholds'))[1]));
  assert.equal(REVIEW_THRESHOLDS.memory, Number(/HighUsagePercent = (\d+)/.exec(rules('MemoryPressureRules'))[1]));
  for (const threshold of [REVIEW_THRESHOLDS.cpu, REVIEW_THRESHOLDS.memory, REVIEW_THRESHOLDS.storage]) {
    for (const value of [null, undefined, NaN, Infinity, -1, 101, '82']) assert.equal(percentReviewStatus(value, threshold), 'unknown');
    assert.equal(percentReviewStatus(0, threshold), 'normal');
    assert.equal(percentReviewStatus(threshold - 0.01, threshold), 'normal');
    assert.equal(percentReviewStatus(threshold, threshold), 'high');
    assert.equal(percentReviewStatus(100, threshold), 'high');
  }
});

test('browser working-set size cannot turn a lower RAM percentage into a pressure warning', () => {
  for (const mb of [0, 1800, 50000, null]) {
    const rows = getSnapshotReadings('inspect_memory_and_processes', { memory: { usagePercent: 78.8 }, browserSummary: { browserMemoryMb: mb } });
    assert.equal(rows[0].status, 'normal');
    assert.equal(rows[0].evidence_kind, 'measurement');
    assert.equal(rows[0].rule_version, 'windows-review-v2');
    assert.ok(['info', 'unknown'].includes(rows[1].status));
  }
});

test('evidence availability never implies strong root-cause certainty or validates old ratings', () => {
  assert.equal(friendlyConfidence('Available'), 'Evidence available');
  assert.equal(friendlyConfidence('Partial'), 'Evidence partly available');
  for (const old of ['High', 'Medium', 'Low']) assert.equal(friendlyConfidence(old), 'Unvalidated rating');
  assert.equal(friendlyConfidence('NotApplicable'), 'Not applicable');
  assert.equal(readingEvidenceLabel({ evidence_kind: 'measurement', status: 'unknown' }), 'Not verified');
  assert.match(readingEvidenceLabel({ status: 'normal' }), /type not recorded/);
  assert.equal(readingEvidenceLabel({ evidence_kind: 'event_log' }), 'Windows event-log evidence');
});

test('partial coverage, pending follow-up and absent hardware have distinct outcomes', () => {
  assert.equal(getResolutionCategory('needs_recheck', null, null, true, false, []).label, 'Awaiting follow-up');
  const partial = getResolutionCategory('needs_recheck', null, null, true, false, [{ status: 'normal' }, { status: 'unknown' }]);
  assert.equal(partial.label, 'Partially checked');
  assert.match(partial.fallbackText, /unsupported sensor/);
  assert.equal(getResolutionCategory('not_applicable').label, 'Not applicable');
});

test('preview readings keep network, storage and battery measurements independent', () => {
  const rows = getSnapshotReadings('inspect_network_connectivity', { liveProbe: { pingLatencyMs: 150, dnsResolutionSucceeded: true }, packetLossPercent: null });
  assert.deepEqual(rows.map(r => r.status), ['high', 'unknown', 'normal']);
  const storage = getSnapshotReadings('inspect_storage_health', { volumes: [{ drive: 'C:', usagePercent: 80, freeGb: 10 }, { drive: 'D:', usagePercent: -1, freeGb: -1 }] });
  assert.deepEqual(storage.map(r => r.status), ['high', 'unknown']);
  assert.doesNotMatch(storage[1].value, /-1/);
  assert.equal(getSnapshotReadings('inspect_storage_health', {})[0].status, 'unknown');
  assert.equal(getSnapshotReadings('inspect_battery_and_power', { hasBattery: false })[0].status, 'unknown');
  assert.equal(getSnapshotReadings('inspect_battery_and_power', { querySucceeded: true, hasBattery: false })[0].status, 'info');
  assert.equal(getSnapshotReadings('inspect_battery_and_power', { querySucceeded: true, hasBattery: true, battery: { chargePercent: 20 } })[0].status, 'high');
});

test('network indicators use scanner boundaries and missing checks never become a healthy link', () => {
  const good = { query_succeeded: true, has_active_adapter: true, has_ipv4_address: true, has_default_gateway: true,
    has_dns_servers: true, dns_resolution_succeeded: true, ping_latency_ms: 149, packet_loss_percent: 4.99,
    is_wifi: true, wifi_signal_strength: 46 };
  assert.equal(networkReviewStatus(good), 'normal');
  assert.equal(networkReviewStatus({ ...good, ping_latency_ms: NETWORK_THRESHOLDS.latencyMs }), 'high');
  assert.equal(networkReviewStatus({ ...good, packet_loss_percent: NETWORK_THRESHOLDS.lossPercent }), 'high');
  assert.equal(networkReviewStatus({ ...good, wifi_signal_strength: NETWORK_THRESHOLDS.weakSignalPercent }), 'high');
  assert.equal(networkReviewStatus({ ...good, has_active_adapter: false }), 'high');
  assert.equal(networkReviewStatus({ ...good, query_succeeded: false }), 'unknown');
  assert.equal(networkReviewStatus({ ...good, ping_latency_ms: null }), 'unknown');
  assert.equal(networkReviewStatus({ ...good, wifi_signal_strength: 0 }), 'unknown');
  assert.equal(networkReviewStatus({ ...good, dns_resolution_succeeded: undefined }), 'unknown');
});

test('a full volume is not hidden by a low pooled storage percentage', () => {
  assert.equal(storageReviewStatus({ disk: { total_gb: 1000, usage_percent: 30 }, all_disks: [{ usage_percent: 90 }, { usage_percent: 10 }] }), 'high');
  assert.equal(storageReviewStatus({ storage_drives: [{ usage_percent: 79.99 }] }), 'normal');
  assert.equal(storageReviewStatus({ storage_drives: [{ usage_percent: null }] }), 'unknown');
  assert.equal(storageReviewStatus({ disk: { total_gb: 0, usage_percent: 0 } }), 'unknown');
});
