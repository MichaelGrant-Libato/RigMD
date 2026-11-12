import test from 'node:test';
import assert from 'node:assert/strict';
import { dnsCacheReviewReason } from '../src/lib/networkActionReview.ts';
import { getRecommendedFindingSteps } from '../src/lib/checkNextSteps.ts';
const rows = () => ['network_adapter','network_address','network_gateway','network_dns_config','network_dns'].map(id => ({
  metric_id: id, label: id, status: id === 'network_dns' ? 'high' : 'normal',
  value: id === 'network_dns' ? 'Not reported' : 'Reported', evidence_kind: 'measurement'
}));
test('DNS review requires a saved lookup failure and each connected prerequisite', () => {
  assert.equal(dnsCacheReviewReason(rows()), null);
  for (const id of ['network_adapter','network_address','network_gateway','network_dns_config']) {
    const proof = rows().map(row => row.metric_id === id ? { ...row, value: 'Not reported', status: 'high' } : row);
    assert.match(dnsCacheReviewReason(proof), /Reconnect Wi-Fi/);
    assert.match(dnsCacheReviewReason(rows().filter(row => row.metric_id !== id)), /Reconnect Wi-Fi/);
  }
});
test('latency-only, missing, duplicate and unavailable DNS readings do not authorize DNS refresh', () => {
  const proofs = [rows().slice(0,-1), [...rows(), rows().at(-1)],
    rows().map(row => row.metric_id === 'network_dns' ? { ...row, value: 'Reported', status: 'normal' } : row),
    rows().map(row => row.metric_id === 'network_dns' ? { ...row, evidence_kind: 'unavailable' } : row)];
  for (const proof of proofs) assert.match(dnsCacheReviewReason(proof), /do not show a verified/);
});
test('offline guidance tells users to reconnect and recheck without changing saved observations', () => {
  const proof = [{ metric_id: 'network_adapter', label: 'Active IPv4 adapter', status: 'high', value: 'Not reported' }];
  const original = JSON.stringify(proof);
  const steps = getRecommendedFindingSteps({ diagnosed_category: 'Network issue', proof });
  assert.match(steps[0].instruction, /Reconnect Wi-Fi or Ethernet/);
  assert.match(dnsCacheReviewReason(proof), /cannot restore a disconnected adapter/);
  assert.equal(JSON.stringify(proof), original);
});
