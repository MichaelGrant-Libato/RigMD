import test from 'node:test';
import assert from 'node:assert/strict';
import { getDesktopPdfBridge, saveDesktopReportPdf } from '../src/lib/desktopReportPdf.ts';

class Bridge {
  listeners = new Set();
  posted = [];
  addEventListener(type, listener) { this.listeners.add(listener); }
  removeEventListener(type, listener) { this.listeners.delete(listener); }
  postMessage(message) { this.posted.push(message); }
  result(status, requestId = 'request-1', type = 'rigmd:report-pdf-result') {
    for (const listener of this.listeners) listener({ data: { type, requestId, status } });
  }
}

test('native PDF support is absent outside the desktop host', () => {
  assert.equal(getDesktopPdfBridge(), null);
});

test('PDF saving correlates the host response and never sends report content or a save path', async () => {
  const bridge = new Bridge();
  const pending = saveDesktopReportPdf('RigMD-Device-Report-test.pdf', bridge, new AbortController().signal, 'request-1');
  assert.deepEqual(bridge.posted, [{ type: 'rigmd:save-report-pdf', requestId: 'request-1', filename: 'RigMD-Device-Report-test.pdf' }]);
  for (const data of [null, 'saved', {}, { type: 'rigmd:report-pdf-result', requestId: 'request-1', status: 'nonsense' }])
    for (const listener of bridge.listeners) listener({ data });
  bridge.result('saved', 'other-request');
  bridge.result('saved', 'request-1', 'other-type');
  assert.equal(bridge.listeners.size, 1);
  bridge.result('saved');
  assert.equal(await pending, 'saved');
  assert.equal(bridge.listeners.size, 0);
});

test('canceling the native Save dialog is reported separately from success', async () => {
  const bridge = new Bridge();
  const pending = saveDesktopReportPdf('RigMD-Device-Report-test.pdf', bridge, new AbortController().signal, 'request-1');
  bridge.result('canceled');
  assert.equal(await pending, 'canceled');
  assert.equal(bridge.listeners.size, 0);
});

test('native failures and busy responses reject and release the listener', async () => {
  for (const status of ['busy', 'error']) {
    const bridge = new Bridge();
    const pending = saveDesktopReportPdf('RigMD-Device-Report-test.pdf', bridge, new AbortController().signal, 'request-1');
    bridge.result(status);
    await assert.rejects(pending, /PDF/);
    assert.equal(bridge.listeners.size, 0);
  }
});

test('aborting an unmounted report or failed bridge never leaves a listener behind', async () => {
  const bridge = new Bridge();
  const controller = new AbortController();
  const pending = saveDesktopReportPdf('RigMD-Device-Report-test.pdf', bridge, controller.signal, 'request-1');
  controller.abort();
  await assert.rejects(pending, { name: 'AbortError' });
  assert.equal(bridge.listeners.size, 0);
  const canceled = new AbortController(); canceled.abort();
  await assert.rejects(saveDesktopReportPdf('test.pdf', bridge, canceled.signal), { name: 'AbortError' });
  bridge.postMessage = () => { throw Error('Host unavailable'); };
  await assert.rejects(saveDesktopReportPdf('test.pdf', bridge, new AbortController().signal), /Host unavailable/);
  assert.equal(bridge.listeners.size, 0);
});
