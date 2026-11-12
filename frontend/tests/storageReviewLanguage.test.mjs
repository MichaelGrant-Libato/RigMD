import test from 'node:test';
import assert from 'node:assert/strict';
import { installationStatusText, removalAvailability } from '../src/lib/storageReviewLanguage.ts';

test('unsupported and protected apps explain availability without suggesting they are safe to remove', () => {
  assert.match(removalAvailability({ canUninstall: false, uninstallReason: 'Unsupported installer' }), /not available/);
  assert.match(removalAvailability({ canUninstall: false, uninstallReason: 'Protected or shared system software' }), /Protected/);
  assert.doesNotMatch(removalAvailability({ canUninstall: true, uninstallReason: '' }), /safe|unused/i);
});

test('missing or unrecognized installed status never implies that removal succeeded', () => {
  for (const status of ['unknown', '', 'unexpected']) assert.match(installationStatusText(status), /could not be confirmed/);
  assert.match(installationStatusText('installed'), /as installed/);
  assert.match(installationStatusText('absent'), /no longer lists this installation/);
  assert.doesNotMatch(installationStatusText('absent'), /all files|repaired|freed|safe/i);
});
