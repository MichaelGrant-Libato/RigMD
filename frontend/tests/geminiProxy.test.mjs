import assert from 'node:assert/strict';
import test from 'node:test';
import { readFile } from 'node:fs/promises';
import { CLOUD_TIMEOUT_MS, createGeminiHandler } from '../server/gemini-handler.js';
import { createConfigHandler } from '../api/cloud-ai-config.js';

const env = { SUPABASE_URL: 'https://test-project.supabase.co', SUPABASE_PUBLISHABLE_KEY: 'sb_publishable_fake',
  SUPABASE_SECRET_KEY: 'sb_secret_fake_admin', GEMINI_API_KEY: 'fake-google-key' };
const user = { id: '89520f7c-2c44-4fef-9baa-e70f7cfa2de9', role: 'authenticated', email: 'test@example.com', email_confirmed_at: '2026-10-01T00:00:00Z' };
const body = { contents: [{ role: 'user', parts: [{ text: 'Explain a diagnostic result.' }] }] };
const json = (value, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
const request = () => ({ method: 'POST', headers: { authorization: 'Bearer fake.jwt.signature', 'content-type': 'application/json' }, query: {}, body: structuredClone(body) });
const response = () => ({ code: 200, headers: {}, value: undefined,
  setHeader(key, value) { this.headers[key] = value; }, status(code) { this.code = code; return this; },
  json(value) { this.value = value; return this; }, end() { return this; } });

async function run(req = request(), overrides = {}) {
  const calls = [];
  const warnings = [];
  const fetchImpl = async (url, options) => {
    calls.push({ url, options });
    if (url.endsWith('/auth/v1/user')) {
      if (overrides.authError) throw overrides.authError;
      return overrides.auth ?? json(overrides.user ?? user);
    }
    if (url.includes('/rpc/')) {
      if (overrides.quotaError) throw overrides.quotaError;
      return overrides.quota ?? json([{ allowed: true, retry_after_seconds: 0 }]);
    }
    if (overrides.error) throw overrides.error;
    return overrides.google ?? json({ candidates: [{ content: { parts: [{ text: 'Safe response.' }] } }] });
  };
  const res = response();
  await createGeminiHandler({ env: overrides.env ?? env, fetchImpl,
    ...(overrides.timeoutSignal ? { timeoutSignal: overrides.timeoutSignal } : {}),
    logger: { warn: (...args) => warnings.push(args) } })(req, res);
  return { res, calls, warnings };
}

test('provider has a longer bounded budget with headroom inside the Vercel duration', async () => {
  const budgets = [];
  const { res, calls } = await run(request(), { timeoutSignal: milliseconds => {
    budgets.push(milliseconds);
    return new AbortController().signal;
  } });
  assert.equal(res.code, 200);
  assert.equal(calls.length, 3);
  assert.deepEqual(budgets, [2000, 2000, 20000]);
  const vercel = JSON.parse(await readFile(new URL('../vercel.json', import.meta.url), 'utf8'));
  const platformMs = vercel.functions['api/gemini.js'].maxDuration * 1000;
  assert.ok(Object.values(CLOUD_TIMEOUT_MS).every(ms => Number.isFinite(ms) && ms > 0));
  assert.ok(Object.values(CLOUD_TIMEOUT_MS).reduce((sum, ms) => sum + ms, 0) + 5000 <= platformMs);
});

test('interrupted stages fail safely without logging prompts, credentials, or raw exceptions', async () => {
  for (const [key, stage, expectedCalls] of [['authError', 'identity', 1], ['quotaError', 'quota', 2], ['error', 'provider', 3]]) {
    for (const name of ['TimeoutError', 'AbortError', 'Error']) {
      const error = new Error(`private data ${env.GEMINI_API_KEY} ${env.SUPABASE_SECRET_KEY}`);
      error.name = name;
      const { res, calls, warnings } = await run(request(), { [key]: error });
      const timedOut = name !== 'Error';
      assert.equal(res.code, timedOut ? 504 : 503);
      assert.equal(res.value.fallbackToLocal, true);
      assert.equal(calls.length, expectedCalls);
      assert.deepEqual(warnings, [['Cloud AI request interrupted.', { stage, category: timedOut ? 'timeout' : 'upstream-failure' }]]);
      assert.ok(!JSON.stringify({ warnings, response: res.value }).includes(env.GEMINI_API_KEY));
      assert.ok(!JSON.stringify({ warnings, response: res.value }).includes(env.SUPABASE_SECRET_KEY));
    }
  }
});

test('unauthenticated calls cannot reach Auth, quota, or Gemini even with a client ID', async () => {
  for (const authorization of [undefined, '', 'Bearer sb_publishable_fake', 'Bearer fake-token', ['Bearer a.b.c']]) {
    const req = request(); req.headers.authorization = authorization; req.headers['x-client-id'] = user.id;
    const { res, calls } = await run(req);
    assert.equal(res.code, 401); assert.equal(calls.length, 0); assert.equal(res.value.fallbackToLocal, true);
  }
});

test('Supabase validates the token; email confirmation and non-anonymous identity are required', async () => {
  for (const changed of [{ email_confirmed_at: null }, { is_anonymous: true }, { role: 'anon' }, { id: 'caller-controlled' }]) {
    const { res, calls } = await run(request(), { user: { ...user, ...changed } });
    assert.equal(res.code, 403); assert.equal(calls.length, 1);
  }
  assert.equal((await run(request(), { auth: json({}, 401) })).res.code, 401);
  assert.equal((await run(request(), { auth: json({}, 503) })).res.code, 503);
  assert.equal((await run(request(), { auth: json({}, 429) })).res.code, 429);
});

test('quota identity comes from verified Auth response and failure closes access', async () => {
  const { calls, res } = await run();
  assert.equal(res.code, 200);
  assert.deepEqual(JSON.parse(calls[1].options.body), { p_user_id: user.id });
  assert.equal(calls[1].options.headers.apikey, env.SUPABASE_SECRET_KEY);
  assert.equal(calls[1].options.headers.Authorization, undefined);
  for (const quota of [json({}, 503), json({ allowed: true }), json([]), json([{ allowed: 'yes' }])]) {
    const result = await run(request(), { quota });
    assert.equal(result.res.code, 503); assert.equal(result.calls.length, 2);
  }
});

test('exceeded shared quota returns 429 with Retry-After and never calls Gemini', async () => {
  const { res, calls } = await run(request(), { quota: json([{ allowed: false, retry_after_seconds: 60 }]) });
  assert.equal(res.code, 429); assert.equal(res.headers['Retry-After'], '60'); assert.equal(calls.length, 2);
});

test('missing cloud configuration fails closed without echoing credentials', async () => {
  for (const key of ['SUPABASE_URL', 'SUPABASE_PUBLISHABLE_KEY', 'SUPABASE_SECRET_KEY', 'GEMINI_API_KEY']) {
    const { res, calls } = await run(request(), { env: { ...env, [key]: '' } });
    assert.equal(res.code, 503); assert.equal(calls.length, 0);
    assert.ok(!JSON.stringify(res).includes(env.GEMINI_API_KEY));
  }
});

test('only approved models and structured text/function content are permitted', async () => {
  for (const model of ['expensive-pro-model', '../models/other', ['gemini-3.5-flash']]) {
    const req = request(); req.query.model = model;
    assert.equal((await run(req)).res.code, 400);
  }
  for (const change of [null, [], { contents: [] }, { ...body, cachedContent: 'anything' },
    { ...body, tools: [{ googleSearch: {} }] }, { contents: [{ parts: [{ fileData: { fileUri: 'https://evil.example' } }] }] },
    { ...body, generationConfig: { maxOutputTokens: -1 } }]) {
    const req = request(); req.body = change;
    const { res, calls } = await run(req); assert.equal(res.code, 400); assert.equal(calls.length, 0);
  }
});

test('content type, declared and actual UTF-8 sizes, and invalid JSON are checked', async () => {
  const cases = [req => { req.headers['content-type'] = 'text/plain'; return 415; },
    req => { req.headers['content-length'] = '131073'; return 413; },
    req => { req.body.contents[0].parts[0].text = 'x'.repeat(131073); return 413; },
    req => { req.body = '{broken'; return 400; }];
  for (const alter of cases) { const req = request(); const expected = alter(req); assert.equal((await run(req)).res.code, expected); }
});

test('output is capped, single-candidate, and provider keys are not put in URLs or responses', async () => {
  const req = request(); req.body.generationConfig = { maxOutputTokens: 65000, candidateCount: 100, thinkingConfig: { thinkingBudget: 999999 } };
  const { calls, res } = await run(req);
  const google = calls.at(-1);
  const sent = JSON.parse(google.options.body);
  assert.equal(sent.generationConfig.maxOutputTokens, 2048);
  assert.equal(sent.generationConfig.candidateCount, 1);
  assert.deepEqual(sent.generationConfig.thinkingConfig, { thinkingLevel: 'minimal' });
  assert.ok(!google.url.includes(env.GEMINI_API_KEY));
  assert.equal(google.options.headers['x-goog-api-key'], env.GEMINI_API_KEY);
  assert.equal(google.options.headers.Authorization, undefined);
  assert.ok(!JSON.stringify(res.value).includes(env.GEMINI_API_KEY));
  assert.ok(calls.every(call => call.options.signal instanceof AbortSignal && call.options.redirect === 'error'));
});

test('multi-turn function calls remain valid within the constrained protocol', async () => {
  const req = request();
  req.body.contents.push({ role: 'model', parts: [{ functionCall: { name: 'inspect_cpu', args: {} } }] },
    { role: 'user', parts: [{ functionResponse: { name: 'inspect_cpu', response: { usage: 50 } } }] });
  req.body.tools = [{ functionDeclarations: [{ name: 'inspect_cpu', description: 'Inspect CPU', parameters: { type: 'OBJECT' } }] }];
  assert.equal((await run(req)).res.code, 200);
});

test('upstream failures, timeout and oversized responses return generic fallback-safe errors', async () => {
  const timeout = new Error(`secret ${env.GEMINI_API_KEY}`); timeout.name = 'TimeoutError';
  assert.equal((await run(request(), { error: timeout })).res.code, 504);
  for (const google of [json({ privateError: env.GEMINI_API_KEY }, 500), json({ huge: 'x'.repeat(262145) }), json({ unexpected: true })]) {
    const { res } = await run(request(), { google });
    assert.ok(res.code >= 500); assert.ok(!JSON.stringify(res.value).includes(env.GEMINI_API_KEY));
  }
  assert.equal((await run(request(), { google: json({}, 429) })).res.code, 429);
  assert.equal((await run(request(), { google: json({}, 404) })).res.code, 404);
});

test('foreign browser origins are denied and allowed preflight never consumes quota', async () => {
  const req = request(); req.headers.origin = 'https://evil.example';
  assert.equal((await run(req)).res.code, 403);
  req.headers.origin = 'http://localhost:5273.evil.example';
  assert.equal((await run(req)).res.code, 403);
  req.method = 'OPTIONS'; req.headers.origin = 'https://www.rig-md.com';
  const { res, calls } = await run(req);
  assert.equal(res.code, 204); assert.equal(calls.length, 0);
  assert.equal(res.headers['Access-Control-Allow-Origin'], req.headers.origin);
});

test('public configuration never exposes administrator or Gemini credentials', () => {
  const res = response(); createConfigHandler(env)({ method: 'GET', headers: {} }, res);
  assert.deepEqual(res.value, { supabaseUrl: env.SUPABASE_URL, publishableKey: env.SUPABASE_PUBLISHABLE_KEY });
  for (const publicKey of ['sb_secret_wrong', `a.${Buffer.from(JSON.stringify({ role: 'service_role' })).toString('base64url')}.b`]) {
    const invalid = response(); createConfigHandler({ ...env, SUPABASE_PUBLISHABLE_KEY: publicKey })({ method: 'GET', headers: {} }, invalid);
    assert.equal(invalid.code, 503);
  }
});
