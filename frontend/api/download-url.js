import { resolveWebsiteDownload } from '../server/releasePolicy.js';

export default async function handler(req, res) {
  res.setHeader('Cache-Control', 'no-store');
  if (req.method !== 'GET') { res.setHeader('Allow', 'GET'); return res.status(405).json({ message: 'Method not allowed.' }); }
  const result = await resolveWebsiteDownload(process.env);
  return res.status(result.available ? 200 : 503).json(result);
}
