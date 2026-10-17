// Keep public authentication configuration separate from all server-only keys.
export function cloudConfig(env) {
  const projectUrl = (env.SUPABASE_URL || env.VITE_SUPABASE_URL || '').trim().replace(/\/$/, '');
  const publicKey = (env.SUPABASE_PUBLISHABLE_KEY || env.VITE_SUPABASE_PUBLISHABLE_KEY ||
    env.SUPABASE_ANON_KEY || env.VITE_SUPABASE_ANON_KEY || '').trim();
  if (!/^https:\/\/[a-z0-9-]+\.supabase\.co$/.test(projectUrl)) return null;
  let publicRole;
  try { publicRole = JSON.parse(Buffer.from(publicKey.split('.')[1] || '', 'base64url').toString()).role; } catch { /* Not a legacy JWT key. */ }
  if (publicKey.startsWith('sb_secret_') || (!publicKey.startsWith('sb_publishable_') &&
    (!/^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$/.test(publicKey) || publicRole !== 'anon'))) return null;
  return { projectUrl, publicKey };
}

export function applyCloudHeaders(req, res, env = process.env) {
  res.setHeader('Cache-Control', 'no-store');
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('Vary', 'Origin');
  const origin = req.headers?.origin;
  if (origin === undefined) return true;
  const allowed = new Set(['https://www.rig-md.com', 'https://rig-md.com',
    'http://localhost:5273', 'http://127.0.0.1:5273', 'http://localhost:5173']);
  // This branch alias is trusted only in the isolated staging deployment.
  if (env.VERCEL_ENV === 'preview' && env.VERCEL_GIT_COMMIT_REF === 'work/local-hardening-sync' &&
    cloudConfig(env)?.projectUrl === 'https://qwytztbzvjgmqybdmrch.supabase.co') {
    allowed.add('https://rig-md-git-work-local-891ed7-michaelgrantlibato7-9044s-projects.vercel.app');
  }
  if (typeof origin !== 'string' || !allowed.has(origin)) return false;
  res.setHeader('Access-Control-Allow-Origin', origin);
  res.setHeader('Access-Control-Allow-Methods', 'POST, GET, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Authorization, Content-Type');
  return true;
}

export async function boundedJson(response, maximumBytes) {
  const reader = response.body?.getReader();
  if (!reader) throw new Error('Missing response body');
  const chunks = [];
  let size = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > maximumBytes) throw new Error('Response limit exceeded');
      chunks.push(Buffer.from(value));
    }
    return JSON.parse(Buffer.concat(chunks).toString('utf8'));
  } finally {
    await reader.cancel().catch(() => {});
    reader.releaseLock();
  }
}
