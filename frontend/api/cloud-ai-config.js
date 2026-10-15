import { applyCloudHeaders, cloudConfig } from '../server/cloud-config.js';

export function createConfigHandler(env) {
  return (req, res) => {
    if (!applyCloudHeaders(req, res)) return res.status(403).json({ error: 'Origin not permitted.' });
    if (req.method === 'OPTIONS') return res.status(204).end();
    if (req.method !== 'GET') { res.setHeader('Allow', 'GET, OPTIONS'); return res.status(405).end(); }
    const config = cloudConfig(env);
    if (!config) return res.status(503).json({ error: 'Cloud sign-in is unavailable.' });
    return res.status(200).json({ supabaseUrl: config.projectUrl, publishableKey: config.publicKey });
  };
}

export default createConfigHandler(process.env);
