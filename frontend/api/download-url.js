import { resolveReleaseDownload } from '../server/releasePolicy.js';

export default function handler(req, res) {
  res.setHeader('Cache-Control', 'no-store');
  if (req.method !== 'GET') { res.setHeader('Allow', 'GET'); return res.status(405).json({ message: 'Method not allowed.' }); }
  const result = resolveReleaseDownload(process.env);
  return res.status(result.available ? 200 : 503).json(result);
}
