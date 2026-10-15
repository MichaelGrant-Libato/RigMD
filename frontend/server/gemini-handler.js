import { applyCloudHeaders, boundedJson, cloudConfig } from './cloud-config.js';
import { MODELS, REQUEST_BYTES, validatePayload } from './gemini-policy.js';

// Dependencies are explicit so access-control tests never consume a live API quota.
export function createGeminiHandler({ env = process.env, fetchImpl = globalThis.fetch } = {}) {
  return async function handler(req, res) {
    const fail = (status, error) => res.status(status).json({ error, fallbackToLocal: true });
    if (!applyCloudHeaders(req, res)) return fail(403, 'Origin not permitted.');
    if (req.method === 'OPTIONS') return res.status(204).end();
    if (req.method !== 'POST') { res.setHeader('Allow', 'POST, OPTIONS'); return fail(405, 'Method not permitted.'); }
    const authorization = req.headers?.authorization;
    if (typeof authorization !== 'string' || authorization.length > 8192 ||
      !/^Bearer [A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/.test(authorization))
      return fail(401, 'Sign in to RigMD to use cloud AI.');
    const config = cloudConfig(env);
    const serviceKey = env.SUPABASE_SECRET_KEY || env.SUPABASE_SERVICE_ROLE_KEY;
    if (!config || !serviceKey || !env.GEMINI_API_KEY) return fail(503, 'Cloud AI is unavailable.');
    if (typeof req.headers?.['content-type'] !== 'string' ||
      req.headers['content-type'].split(';')[0].trim().toLowerCase() !== 'application/json')
      return fail(415, 'A JSON request is required.');
    let payload;
    try {
      if (Number(req.headers?.['content-length']) > REQUEST_BYTES) return fail(413, 'Request is too large.');
      const serialized = typeof req.body === 'string' ? req.body : JSON.stringify(req.body);
      if (!serialized) return fail(400, 'Invalid request.');
      if (Buffer.byteLength(serialized, 'utf8') > REQUEST_BYTES) return fail(413, 'Request is too large.');
      payload = JSON.parse(serialized);
    } catch { return fail(400, 'Invalid JSON request.'); }
    const model = req.query?.model ?? 'gemini-3.5-flash';
    if (typeof model !== 'string' || !MODELS.has(model)) return fail(400, 'Model not permitted.');
    const body = validatePayload(payload, model);
    if (!body) return fail(400, 'Request content is not permitted.');
    try {
      const userResponse = await fetchImpl(`${config.projectUrl}/auth/v1/user`, {
        headers: { apikey: config.publicKey, Authorization: authorization }, signal: AbortSignal.timeout(2000), redirect: 'error',
      });
      if (!userResponse.ok) {
        await userResponse.body?.cancel();
        if (userResponse.status === 429) { res.setHeader('Retry-After', '60'); return fail(429, 'Cloud sign-in is busy. Try again later.'); }
        return fail(userResponse.status >= 500 ? 503 : 401, 'Cloud sign-in could not be verified.');
      }
      const user = await boundedJson(userResponse, 65536);
      if (!/^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/i.test(user.id ?? '') ||
        user.role !== 'authenticated' || user.is_anonymous === true || !user.email ||
        !user.email_confirmed_at || !Number.isFinite(Date.parse(user.email_confirmed_at)))
        return fail(403, 'A verified RigMD email account is required.');
      const quotaResponse = await fetchImpl(`${config.projectUrl}/rest/v1/rpc/consume_rigmd_ai_quota`, {
        method: 'POST', headers: { apikey: serviceKey, 'Content-Type': 'application/json',
          ...(serviceKey.startsWith('ey') ? { Authorization: `Bearer ${serviceKey}` } : {}) },
        body: JSON.stringify({ p_user_id: user.id }), signal: AbortSignal.timeout(2000), redirect: 'error',
      });
      if (!quotaResponse.ok) { await quotaResponse.body?.cancel(); return fail(503, 'Cloud usage limits are temporarily unavailable.'); }
      const quota = await boundedJson(quotaResponse, 4096);
      if (!Array.isArray(quota) || quota.length !== 1 || typeof quota[0]?.allowed !== 'boolean')
        return fail(503, 'Cloud usage limits are temporarily unavailable.');
      if (!quota[0].allowed) {
        res.setHeader('Retry-After', String(Math.min(86400, Math.max(1, Number(quota[0].retry_after_seconds) || 60))));
        return fail(429, 'Cloud AI usage limit reached. Local diagnostics are still available.');
      }
      const googleResponse = await fetchImpl(`https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent`, {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'x-goog-api-key': env.GEMINI_API_KEY },
        body: JSON.stringify(body), signal: AbortSignal.timeout(8000), redirect: 'error',
      });
      if (!googleResponse.ok) {
        await googleResponse.body?.cancel();
        if (googleResponse.status === 429) { res.setHeader('Retry-After', '60'); return fail(429, 'Cloud AI is busy. Try again later.'); }
        if ([400, 404].includes(googleResponse.status)) return fail(googleResponse.status, 'This cloud model could not serve the request.');
        return fail(502, 'Cloud AI could not complete this request.');
      }
      const data = await boundedJson(googleResponse, 256 * 1024);
      if (!data || !Array.isArray(data.candidates)) return fail(502, 'Cloud AI returned an invalid response.');
      return res.status(200).json(data);
    } catch (error) {
      return fail(error?.name === 'TimeoutError' || error?.name === 'AbortError' ? 504 : 503,
        'Cloud AI is temporarily unavailable. Local diagnostics are still available.');
    }
  };
}
