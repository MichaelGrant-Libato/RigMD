import assert from 'node:assert/strict';
import test from 'node:test';
import { readFile } from 'node:fs/promises';
import { CLOUD_TIMEOUT_MS, createGeminiHandler } from '../server/gemini-handler.js';
import { createConfigHandler } from '../api/cloud-ai-config.js';

const env = { SUPABASE_URL: 'https://test-project.supabase.co', SUPABASE_PUBLISHABLE_KEY: 'sb_publishable_fake',
  SUPABASE_SECRET_KEY: 'sb_secret_fake_admin', GEMINI_API_KEY: 'fake-google-key' };
const stagingOrigin = 'https://rig-md-git-work-local-891ed7-michaelgrantlibato7-9044s-projects.vercel.app';
const stagingEnv = { ...env, SUPABASE_URL: 'https://qwytztbzvjgmqybdmrch.supabase.co',
  VERCEL_ENV: 'preview', VERCEL_GIT_COMMIT_REF: 'work/local-hardening-sync' };
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
  const infos = [];
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
    logger: { warn: (...args) => warnings.push(args), info: (...args) => infos.push(args) } })(req, res);
  return { res, calls, warnings, infos };
}

test('provider status and completion logs contain only bounded metadata, never response data', async () => {
  const rejected = await run(request(), { google: json({ error: { message: `${env.GEMINI_API_KEY} private prompt` } }, 503) });
  assert.equal(rejected.res.code, 502);
  assert.deepEqual(rejected.warnings, [['Cloud AI provider rejected request.', { model: 'gemini-3.5-flash', status: 503 }]]);
  for (const reportedFinish of ['STOP', 'MAX_TOKENS', 'SAFETY', `unsafe\n${env.GEMINI_API_KEY}`]) {
    const result = await run(request(), { google: json({ candidates: [{ finishReason: reportedFinish,
      content: { parts: [{ text: `private text ${env.GEMINI_API_KEY}` }] } }] }) });
    assert.equal(result.res.code, 200);
    assert.deepEqual(result.infos, [['Cloud AI provider response.', { model: 'gemini-3.5-flash', status: 200,
      finishReason: ['STOP', 'MAX_TOKENS', 'SAFETY'].includes(reportedFinish) ? reportedFinish : 'OTHER' }]]);
    assert.ok(!JSON.stringify({ warnings: result.warnings, infos: result.infos }).includes(env.GEMINI_API_KEY));
    assert.ok(!JSON.stringify(result.infos).includes('private text'));
  }
  const empty = await run(request(), { google: json({ candidates: [] }) });
  assert.equal(empty.res.code, 502);
  assert.deepEqual(empty.warnings, [['Cloud AI provider returned no candidates.', { model: 'gemini-3.5-flash' }]]);
});

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

test('only the staging branch alias can preflight and read its public configuration', async () => {
  const req = request(); req.method = 'OPTIONS'; req.headers.origin = stagingOrigin;
  delete req.headers.authorization;
  const { res, calls } = await run(req, { env: stagingEnv });
  assert.equal(res.code, 204);
  assert.equal(calls.length, 0);
  assert.equal(res.headers['Access-Control-Allow-Origin'], stagingOrigin);
  assert.equal(res.headers['Access-Control-Allow-Headers'], 'Authorization, Content-Type');
  const preflight = response(); createConfigHandler(stagingEnv)(req, preflight);
  assert.equal(preflight.code, 204);
  const config = response(); createConfigHandler(stagingEnv)({ ...req, method: 'GET' }, config);
  assert.equal(config.code, 200);
  assert.equal(config.headers['Access-Control-Allow-Origin'], stagingOrigin);
  assert.deepEqual(config.value, { supabaseUrl: stagingEnv.SUPABASE_URL, publishableKey: stagingEnv.SUPABASE_PUBLISHABLE_KEY });
  assert.ok(!JSON.stringify(config.value).includes(stagingEnv.SUPABASE_SECRET_KEY));
  assert.ok(!JSON.stringify(config.value).includes(stagingEnv.GEMINI_API_KEY));
});

test('staging origin requires Preview, the exact branch, and the effective staging project', async () => {
  for (const changed of [
    { VERCEL_ENV: 'production' }, { VERCEL_ENV: 'development' }, { VERCEL_ENV: undefined },
    { VERCEL_GIT_COMMIT_REF: 'main' }, { VERCEL_GIT_COMMIT_REF: 'work/local-hardening-syn' },
    { VERCEL_GIT_COMMIT_REF: undefined },
    { SUPABASE_URL: 'https://rjictnjgmuvwzilipqcz.supabase.co' },
    { SUPABASE_URL: 'https://other-project.supabase.co' },
    { SUPABASE_URL: '', VITE_SUPABASE_URL: '' }, { SUPABASE_PUBLISHABLE_KEY: 'sb_secret_wrong' },
    { SUPABASE_URL: 'https://rjictnjgmuvwzilipqcz.supabase.co', VITE_SUPABASE_URL: stagingEnv.SUPABASE_URL },
  ]) {
    const configEnv = { ...stagingEnv, ...changed };
    for (const method of ['POST', 'OPTIONS']) {
      const req = request(); req.method = method; req.headers.origin = stagingOrigin;
      const { res, calls } = await run(req, { env: configEnv });
      assert.equal(res.code, 403); assert.equal(calls.length, 0);
      assert.equal(res.headers['Access-Control-Allow-Origin'], undefined);
    }
    for (const method of ['GET', 'OPTIONS']) {
      const config = response(); createConfigHandler(configEnv)({ method, headers: { origin: stagingOrigin } }, config);
      assert.equal(config.code, 403);
      assert.equal(config.headers['Access-Control-Allow-Origin'], undefined);
    }
  }
});

test('other Preview deployments and lookalike staging origins remain blocked', async () => {
  for (const origin of ['https://other-preview.vercel.app',
    'https://rig-j8a2rbfz4-michaelgrantlibato7-9044s-projects.vercel.app',
    `${stagingOrigin}.evil.example`, `${stagingOrigin}/`, `${stagingOrigin}:444`,
    stagingOrigin.replace('https:', 'http:'), 'null', [stagingOrigin]]) {
    const req = request(); req.headers.origin = origin;
    const { res, calls } = await run(req, { env: stagingEnv });
    assert.equal(res.code, 403); assert.equal(calls.length, 0);
    const config = response(); createConfigHandler(stagingEnv)({ method: 'GET', headers: { origin } }, config);
    assert.equal(config.code, 403);
  }
});

test('staging origin still requires verified sign-in and quota before calling Gemini', async () => {
  const req = request(); req.headers.origin = stagingOrigin;
  const unsigned = structuredClone(req); delete unsigned.headers.authorization;
  const denied = await run(unsigned, { env: stagingEnv });
  assert.equal(denied.res.code, 401); assert.equal(denied.calls.length, 0);
  const unverified = await run(req, { env: stagingEnv, user: { ...user, email_confirmed_at: null } });
  assert.equal(unverified.res.code, 403); assert.equal(unverified.calls.length, 1);
  const exhausted = await run(req, { env: stagingEnv, quota: json([{ allowed: false, retry_after_seconds: 60 }]) });
  assert.equal(exhausted.res.code, 429); assert.equal(exhausted.calls.length, 2);
  assert.equal(exhausted.res.headers['Retry-After'], '60');
  const accepted = await run(req, { env: stagingEnv });
  assert.equal(accepted.res.code, 200); assert.equal(accepted.calls.length, 3);
  assert.equal(accepted.calls[0].url, `${stagingEnv.SUPABASE_URL}/auth/v1/user`);
  assert.equal(accepted.calls[1].url, `${stagingEnv.SUPABASE_URL}/rest/v1/rpc/consume_rigmd_ai_quota`);
  assert.equal(accepted.res.headers['Access-Control-Allow-Origin'], stagingOrigin);
});

test('staging accepts the existing VITE public configuration fallback', async () => {
  const fallbackEnv = { ...stagingEnv, SUPABASE_URL: '', SUPABASE_PUBLISHABLE_KEY: '',
    VITE_SUPABASE_URL: stagingEnv.SUPABASE_URL, VITE_SUPABASE_PUBLISHABLE_KEY: stagingEnv.SUPABASE_PUBLISHABLE_KEY };
  const req = request(); req.headers.origin = stagingOrigin;
  const { res } = await run(req, { env: fallbackEnv });
  assert.equal(res.code, 200);
  const config = response(); createConfigHandler(fallbackEnv)({ method: 'GET', headers: { origin: stagingOrigin } }, config);
  assert.equal(config.code, 200);
  assert.equal(config.value.supabaseUrl, stagingEnv.SUPABASE_URL);
});

test('production retains its existing allowed origins and no-Origin requests', async () => {
  const productionEnv = { ...env, VERCEL_ENV: 'production', VERCEL_GIT_COMMIT_REF: 'main' };
  for (const origin of [undefined, 'https://www.rig-md.com', 'https://rig-md.com',
    'http://localhost:5273', 'http://127.0.0.1:5273', 'http://localhost:5173']) {
    const req = request(); if (origin !== undefined) req.headers.origin = origin;
    const { res } = await run(req, { env: productionEnv });
    assert.equal(res.code, 200);
    assert.equal(res.headers['Access-Control-Allow-Origin'], origin);
    const config = response(); createConfigHandler(productionEnv)({ method: 'GET', headers: req.headers }, config);
    assert.equal(config.code, 200);
  }
});

test('public configuration never exposes administrator or Gemini credentials', () => {
  const res = response(); createConfigHandler(env)({ method: 'GET', headers: {} }, res);
  assert.deepEqual(res.value, { supabaseUrl: env.SUPABASE_URL, publishableKey: env.SUPABASE_PUBLISHABLE_KEY });
  for (const publicKey of ['sb_secret_wrong', `a.${Buffer.from(JSON.stringify({ role: 'service_role' })).toString('base64url')}.b`]) {
    const invalid = response(); createConfigHandler({ ...env, SUPABASE_PUBLISHABLE_KEY: publicKey })({ method: 'GET', headers: {} }, invalid);
    assert.equal(invalid.code, 503);
  }
});
