import test from 'node:test';
import assert from 'node:assert/strict';
import { getActionDecision } from '../src/lib/actionDecision.ts';
test('app-closing explanation separates process exit, memory changes and hardware repair', () => {
  for (const tool of ['close_selected_app', 'terminate_processes']) {
    const decision = getActionDecision(tool, [{ metric_id: 'memory_usage', label: 'Physical Memory', value: '88%', status: 'high' }]);
    assert.match(decision.why, /memory warning/);
    assert.match(decision.why, /does not test or repair/);
    assert.match(decision.check, /separate memory recheck/);
    assert.match(decision.check, /background/);
    assert.match(decision.check, /does not prove a repair/);
  }
  assert.doesNotMatch(getActionDecision('close_selected_app').why, /includes a memory warning/);
});
test('storage cleanup is not described as freeing app memory', () => {
  for (const tool of ['clear_temp_files', 'clear_user_temp_files', 'run_disk_cleanup']) {
    assert.match(getActionDecision(tool).why, /does not directly free the working memory/);
    assert.match(getActionDecision(tool).check, /not proof/);
  }
});
test('system-file check and unknown tools never promise a verified repair', () => {
  assert.match(getActionDecision('run_system_file_checker').check, /cannot prove/);
  assert.match(getActionDecision('unregistered-tool').why, /not a confirmed repair/);
  assert.match(getActionDecision('unregistered-tool').check, /unverified/);
});
