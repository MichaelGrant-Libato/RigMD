import test from 'node:test';
import assert from 'node:assert/strict';
import { getPublicPageMetadata } from '../src/lib/publicPageMetadata.ts';

test('public account pages have distinct titles and descriptions without user information', () => {
  const pages = ['/register', '/login', '/verify-email', '/download', '/desktop-connect'].map(getPublicPageMetadata);
  assert.equal(new Set(pages.map(page => page.title)).size, 5);
  for (const page of pages) {
    assert.match(page.title, /RigMD/);
    assert.ok(page.description.length > 30);
  }
});

test('unknown links receive missing-page metadata rather than registration metadata', () => {
  assert.equal(getPublicPageMetadata('/').title, getPublicPageMetadata('/register').title);
  assert.equal(getPublicPageMetadata('/missing').title, 'Page Not Found | RigMD');
  assert.doesNotMatch(JSON.stringify(getPublicPageMetadata('/missing?email=private@example.test')), /private@example/);
});
