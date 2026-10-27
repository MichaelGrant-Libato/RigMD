import { Buffer } from 'node:buffer';
import process from 'node:process';
import { createCipheriv, createPublicKey, publicEncrypt, randomBytes, constants } from 'node:crypto';
import { applyCloudHeaders, boundedJson, cloudConfig } from './cloud-config.js';

const sites = new Set(['https://www.rig-md.com', 'https://rig-md.com']);
const id = value => typeof value === 'string' && /^[A-Za-z0-9_-]{43}$/.test(value);
const verified = user => /^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/i.test(user?.id ?? '') &&
  user.role === 'authenticated' && user.is_anonymous !== true && typeof user.email === 'string' &&
  user.email.length > 0 && Number.isFinite(Date.parse(user.email_confirmed_at));

// A fresh desktop session is created; the browser's refresh credential is never copied or rotated.
export function createDesktopConnectHandler({ env = process.env, fetchImpl = globalThis.fetch, now = Date.now } = {}) {
  return async function handler(req, res) {
    const fail = (status, message) => res.status(status).json({ message });
    if (!applyCloudHeaders(req, res, env) || !sites.has(req.headers?.origin)) return fail(403, 'Open the RigMD website to connect this PC.');
    if (req.method === 'OPTIONS') return res.status(204).end();
    if (req.method !== 'POST') { res.setHeader('Allow', 'POST, OPTIONS'); return fail(405, 'Method not permitted.'); }
    const authorization = req.headers?.authorization;
    if (typeof authorization !== 'string' || authorization.length > 8192 || !/^Bearer [A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/.test(authorization))
      return fail(401, 'Sign in on the RigMD website first.');
    const config = cloudConfig(env), serviceKey = env.SUPABASE_SECRET_KEY || env.SUPABASE_SERVICE_ROLE_KEY;
    if (!config || !serviceKey) return fail(503, 'Website connection is not available yet.');
    if (typeof req.headers?.['content-type'] !== 'string' || req.headers['content-type'].split(';')[0].trim().toLowerCase() !== 'application/json') return fail(415, 'A JSON request is required.');
    let state, key;
    try {
      const serialized = typeof req.body === 'string' ? req.body : JSON.stringify(req.body);
      if (!serialized || Buffer.byteLength(serialized) > 4096 || Number(req.headers?.['content-length']) > 4096) return fail(413, 'Request is too large.');
      const body = JSON.parse(serialized);
      if (!body || Object.keys(body).some(k => !['state', 'publicKey'].includes(k)) || !id(body.state) ||
        typeof body.publicKey !== 'string' || !/^[A-Za-z0-9_-]{400,1000}$/.test(body.publicKey)) return fail(400, 'Invalid PC connection.');
      state = body.state;
      key = createPublicKey({ key: Buffer.from(body.publicKey, 'base64url'), format: 'der', type: 'spki' });
      if (key.asymmetricKeyType !== 'rsa' || key.asymmetricKeyDetails.modulusLength !== 3072 || key.asymmetricKeyDetails.publicExponent !== 65537n)
        return fail(400, 'Invalid PC connection.');
    } catch { return fail(400, 'Invalid PC connection.'); }
    const fetchJson = async (path, options, limit = 65536) => {
      const response = await fetchImpl(config.projectUrl + path, { ...options, redirect: 'error', signal: AbortSignal.timeout(5000) });
      if (!response.ok) { await response.body?.cancel(); const error = new Error('Upstream refused connection'); error.status = response.status; throw error; }
      return boundedJson(response, limit);
    };
    try {
      const user = await fetchJson('/auth/v1/user', { headers: { apikey: config.publicKey, Authorization: authorization } });
      if (!verified(user)) return fail(403, 'Use your verified RigMD website account.');
      const adminHeaders = { apikey: serviceKey, 'Content-Type': 'application/json',
        ...(serviceKey.startsWith('ey') ? { Authorization: `Bearer ${serviceKey}` } : {}) };
      // Connection requests share the existing per-user/global cloud request budget.
      const quota = await fetchJson('/rest/v1/rpc/consume_rigmd_ai_quota', { method: 'POST', headers: adminHeaders,
        body: JSON.stringify({ p_user_id: user.id }) }, 4096);
      if (!Array.isArray(quota) || quota.length !== 1 || typeof quota[0]?.allowed !== 'boolean') return fail(503, 'Connection limits are unavailable.');
      if (!quota[0].allowed) { res.setHeader('Retry-After', '60'); return fail(429, 'Please wait before connecting again. Offline checks still work.'); }
      const link = await fetchJson('/auth/v1/admin/generate_link', { method: 'POST', headers: adminHeaders,
        body: JSON.stringify({ type: 'magiclink', email: user.email }) });
      if (link.id !== user.id || link.verification_type !== 'magiclink' || typeof link.hashed_token !== 'string' || link.hashed_token.length > 256)
        return fail(503, 'Could not prepare the account connection.');
      const desktop = await fetchJson('/auth/v1/verify', { method: 'POST',
        headers: { apikey: config.publicKey, 'Content-Type': 'application/json' },
        body: JSON.stringify({ type: 'magiclink', token_hash: link.hashed_token }) });
      if (!verified(desktop.user) || desktop.user.id !== user.id || typeof desktop.refresh_token !== 'string' ||
        desktop.refresh_token.length < 1 || desktop.refresh_token.length > 8192)
        return fail(503, 'Could not prepare the account connection.');
      const secret = randomBytes(32), iv = randomBytes(12);
      try {
        const cipher = createCipheriv('aes-256-gcm', secret, iv);
        cipher.setAAD(Buffer.from(state));
        const ciphertext = Buffer.concat([cipher.update(JSON.stringify({ state, userId: user.id,
          refreshToken: desktop.refresh_token, expiresAt: now() + 120000 })), cipher.final()]);
        const wrappedKey = publicEncrypt({ key, padding: constants.RSA_PKCS1_OAEP_PADDING, oaepHash: 'sha256' }, secret);
        return res.status(200).json({ wrappedKey: wrappedKey.toString('base64url'), iv: iv.toString('base64url'),
          tag: cipher.getAuthTag().toString('base64url'), ciphertext: ciphertext.toString('base64url') });
      } finally { secret.fill(0); }
    } catch (error) {
      // Never return/log upstream bodies, passwords, tokens, public keys, or full request URLs.
      return fail(error.status === 401 || error.status === 403 ? 401 : error.status === 429 ? 429 : 503,
        'The website could not connect this PC. Try again; offline checks still work.');
    }
  };
}
