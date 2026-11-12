import test from 'node:test';
import assert from 'node:assert/strict';
import { reviewSelectedAppInWindows } from '../src/lib/windowsAppReview.ts';

test('the chosen Windows inventory name is copied during the user gesture before Settings opens', async () => {
  const calls = [];
  const app = { id: 'selected-app', name: 'A dynamically discovered app & tools' };
  const result = await reviewSelectedAppInWindows(app,
    async name => { calls.push(['copy', name]); },
    async () => { calls.push(['open']); return { success: true, summary: 'Backend confirmed navigation.' }; });
  assert.deepEqual(calls, [['copy', app.name], ['open']]);
  assert.deepEqual(result, { appId: app.id, appName: app.name, nameCopied: true, summary: 'Backend confirmed navigation.' });
});

test('denied or unavailable clipboard access does not prevent the Windows fallback', async () => {
  for (const copy of [async () => { throw new Error('Denied'); }, () => { throw new Error('Unavailable'); }]) {
    let opened = false;
    const result = await reviewSelectedAppInWindows({ id: 'another-app', name: 'Another local app' }, copy,
      async () => { opened = true; return { success: true }; });
    assert.equal(opened, true);
    assert.equal(result.nameCopied, false);
    assert.equal(result.appName, 'Another local app');
  }
});

test('backend failure cannot be reported as opened or as a successful uninstall', async () => {
  const app = { id: 'app', name: 'Chosen app' };
  await assert.rejects(reviewSelectedAppInWindows(app, async () => {}, async () => ({ success: false, summary: 'Launch failed.' })), /Launch failed/);
  await assert.rejects(reviewSelectedAppInWindows(app, async () => {}, async () => { throw new Error('Backend unavailable'); }), /Backend unavailable/);
});
