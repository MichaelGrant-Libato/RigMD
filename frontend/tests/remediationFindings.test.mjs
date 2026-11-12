import test from 'node:test';
import assert from 'node:assert/strict';
import { getRemediationFindings } from '../src/lib/remediationFindings.ts';

test('all saved warnings remain selectable alongside memory without mixing their action evidence', () => {
  const readings = [
    { metric_id: 'memory_usage', value: '88%', status: 'high' },
    { metric_id: 'storage_usage_C:', label: 'Storage space C:', value: '90% used', status: 'high' },
    { metric_id: 'memory_app_warning', value: 'Reported warning', status: 'high', evidence_kind: 'reported_status' },
    { metric_id: 'storage_usage_D:', label: 'Storage space D:', value: '95% used', status: 'high' },
    { metric_id: 'cpu_temperature', value: '90°C', status: 'high' },
    { metric_id: 'network_dns', value: 'Test did not pass', status: 'high' },
  ];
  const before = JSON.stringify(readings);
  const findings = getRemediationFindings(readings);
  assert.equal(findings.length, 5);
  assert.equal(findings[0].kind, 'memory');
  assert.equal(findings[0].readings.length, 2);
  assert.deepEqual(findings.filter(finding => finding.kind === 'storage').map(finding => finding.id), ['storage_usage_C:', 'storage_usage_D:']);
  assert.equal(findings.at(-1).kind, 'guidance');
  assert.equal(JSON.stringify(readings), before);
});

test('normal, informational and missing follow-up readings cannot become remediation choices', () => {
  assert.deepEqual(getRemediationFindings([
    { metric_id: 'memory_usage', value: '50%', status: 'normal' },
    { metric_id: 'storage_usage_C:', value: 'Not available', status: 'unknown' },
    { metric_id: 'graphics_context', value: 'Detected', status: 'info' },
    { metric_id: 'storage_usage_D:', value: 'Unavailable', status: 'high', evidence_kind: 'missing_follow_up' },
  ]), []);
});
