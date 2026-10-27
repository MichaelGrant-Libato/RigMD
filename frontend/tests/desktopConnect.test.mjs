import assert from 'node:assert/strict';
import test from 'node:test';
import { generateKeyPairSync, privateDecrypt, createDecipheriv, constants } from 'node:crypto';
import { createDesktopConnectHandler } from '../server/desktop-connect.js';
import { readDesktopConnectRequest, DESKTOP_CONNECT_CALLBACK } from '../src/lib/desktopAccountConnect.js';

const { publicKey, privateKey } = generateKeyPairSync('rsa', { modulusLength: 3072 });
const encoded = publicKey.export({ type: 'spki', format: 'der' }).toString('base64url');
const state = 'a'.repeat(43), now = 1793059200000;
const user = { id: '89520f7c-2c44-4fef-9baa-e70f7cfa2de9', email: 'member@example.com',
  role: 'authenticated', is_anonymous: false, email_confirmed_at: '2026-10-01T00:00:00Z' };
const env = { SUPABASE_URL: 'https://test-project.supabase.co', SUPABASE_PUBLISHABLE_KEY: 'sb_publishable_fake', SUPABASE_SECRET_KEY: 'sb_secret_test' };
const json = (value, status = 200) => new Response(JSON.stringify(value), { status });
const request = () => ({ method: 'POST', headers: { origin: 'https://www.rig-md.com', authorization: 'Bearer browser.access.signature', 'content-type': 'application/json' },
  body: { state, publicKey: encoded } });
const response = () => ({ code: 0, headers: {}, setHeader(k, v) { this.headers[k] = v; }, status(c) { this.code = c; return this; }, json(v) { this.value = v; return this; }, end() {} });
async function run(req = request(), override = {}) {
  const calls = [], res = response();
  const fetchImpl = async (url, options) => {
    calls.push({ url, options });
    if (override.fail) throw new Error('SECRET_UPSTREAM_ERROR');
    if (url.endsWith('/user')) return override.identity ?? json(user);
    if (url.includes('/rpc/')) return override.quota ?? json([{ allowed: true }]);
    if (url.endsWith('/generate_link')) return override.link ?? json({ ...user, verification_type: 'magiclink', hashed_token: 'one-time-hash' });
    if (url.endsWith('/verify')) return override.verify ?? json({ user, refresh_token: 'new-desktop-refresh', access_token: 'desktop.access.signature' });
    throw new Error('Unexpected request');
  };
  await createDesktopConnectHandler({ env, fetchImpl, now: () => now })(req, res);
  return { calls, res };
}
function decrypt(envelope, aad = state) {
  const secret = privateDecrypt({ key: privateKey, padding: constants.RSA_PKCS1_OAEP_PADDING, oaepHash: 'sha256' }, Buffer.from(envelope.wrappedKey, 'base64url'));
  const decipher = createDecipheriv('aes-256-gcm', secret, Buffer.from(envelope.iv, 'base64url'));
  decipher.setAAD(Buffer.from(aad)); decipher.setAuthTag(Buffer.from(envelope.tag, 'base64url'));
  return JSON.parse(Buffer.concat([decipher.update(Buffer.from(envelope.ciphertext, 'base64url')), decipher.final()]).toString());
}
test('website account produces a fresh encrypted PC-bound session, never transfers browser refresh credentials', async () => {
  const { res, calls } = await run();
  assert.equal(res.code, 200); assert.equal(res.headers['Cache-Control'], 'no-store');
  assert.deepEqual(decrypt(res.value), { state, userId: user.id, refreshToken: 'new-desktop-refresh', expiresAt: now + 120000 });
  assert.equal(calls.length, 4);
  assert.equal(calls[0].options.headers.Authorization, 'Bearer browser.access.signature');
  assert.deepEqual(JSON.parse(calls[2].options.body), { type: 'magiclink', email: user.email });
  assert.deepEqual(JSON.parse(calls[3].options.body), { type: 'magiclink', token_hash: 'one-time-hash' });
  assert.ok(!JSON.stringify(res.value).includes('new-desktop-refresh'));
  assert.ok(!JSON.stringify(res.value).includes(env.SUPABASE_SECRET_KEY));
  assert.ok(calls.every(c => c.options.redirect === 'error' && c.options.signal));
  assert.throws(() => decrypt(res.value, 'b'.repeat(43)));
});
test('no identity or token requests without a trusted website origin and authenticated request', async () => {
  for (const origin of [undefined, 'null', 'http://localhost:5273', 'https://evil.test', ['https://www.rig-md.com']]) {
    const req = request(); req.headers.origin = origin;
    const { res, calls } = await run(req); assert.equal(res.code, 403); assert.equal(calls.length, 0);
  }
  for (const authorization of [undefined, '', 'Bearer fake', ['Bearer a.b.c']]) {
    const req = request(); req.headers.authorization = authorization;
    const { res, calls } = await run(req); assert.equal(res.code, 401); assert.equal(calls.length, 0);
  }
});
test('unverified, anonymous and substituted identities never receive a desktop session', async () => {
  for (const identity of [{ ...user, is_anonymous: true }, { ...user, email_confirmed_at: null }, { ...user, role: 'service_role' }]) {
    const { res, calls } = await run(request(), { identity: json(identity) }); assert.equal(res.code, 403); assert.equal(calls.length, 1);
  }
  for (const override of [
    { link: json({ ...user, id: 'different', verification_type: 'magiclink', hashed_token: 'one-time-hash' }) },
    { verify: json({ user: { ...user, id: 'different' }, refresh_token: 'must-not-release' }) },
  ]) { const { res } = await run(request(), override); assert.equal(res.code, 503); assert.ok(!JSON.stringify(res.value).includes('must-not-release')); }
});
test('existing per-user and global quotas apply before issuing a desktop session', async () => {
  const { res, calls } = await run(request(), { quota: json([{ allowed: false }]) });
  assert.equal(res.code, 429); assert.equal(calls.length, 2);
  const failed = await run(request(), { quota: json({ allowed: true }) }); assert.equal(failed.res.code, 503); assert.equal(failed.calls.length, 2);
});
test('malformed, oversize and non-RSA keys are rejected before any privileged request', async () => {
  const ec = generateKeyPairSync('ec', { namedCurve: 'P-256' }).publicKey.export({ type: 'spki', format: 'der' }).toString('base64url');
  for (const body of [null, { state: 'short', publicKey: encoded }, { state, publicKey: 'a'.repeat(500) }, { state, publicKey: ec },
    { state, publicKey: encoded, email: 'victim@example.com' }, { state, publicKey: 'a'.repeat(5000) }]) {
    const req = request(); req.body = body; const { res, calls } = await run(req);
    assert.ok([400, 413].includes(res.code)); assert.equal(calls.length, 0);
  }
  for (const contentType of ['text/plain', ['application/json'], undefined]) {
    const req = request(); req.headers['content-type'] = contentType; const result = await run(req);
    assert.equal(result.res.code, 415); assert.equal(result.calls.length, 0);
  }
});
test('upstream errors reveal no credentials or raw messages', async () => {
  const { res } = await run(request(), { fail: true }); assert.equal(res.code, 503);
  assert.ok(!JSON.stringify(res.value).includes('SECRET_UPSTREAM_ERROR'));
});
test('connection fragments preserve only state/public key and never accept callback redirection', () => {
  const fragment = '#state=' + state + '&publicKey=' + encoded;
  assert.deepEqual(readDesktopConnectRequest(fragment), { state, publicKey: encoded, code: 'AAAAAAAA' });
  for (const suffix of ['&state=' + state, '&callback=https://evil.test', '&publicKey=' + encoded]) assert.equal(readDesktopConnectRequest(fragment + suffix), null);
  assert.equal(DESKTOP_CONNECT_CALLBACK, 'http://127.0.0.1:5273/desktop-account-connect');
});
