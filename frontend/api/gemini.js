export default async function handler(req, res) {
  // Set CORS headers so local RigMD desktop client or web testing can connect
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type, X-Client-ID');

  if (req.method === 'OPTIONS') {
    return res.status(200).end();
  }

  if (req.method !== 'POST') {
    return res.status(405).json({
      error: 'Method not allowed. Use POST.',
      fallbackToLocal: true
    });
  }

  const apiKey = process.env.GEMINI_API_KEY;
  if (!apiKey) {
    return res.status(503).json({
      error: 'GEMINI_API_KEY environment variable is not configured on Vercel.',
      fallbackToLocal: true
    });
  }

  try {
    let payload = req.body;
    if (typeof payload === 'string') {
      try {
        payload = JSON.parse(payload);
      } catch {
        // Keep raw string if not JSON
      }
    }

    // Determine model from query parameter or payload
    const model =
      req.query?.model ||
      (payload && typeof payload === 'object' && payload.model) ||
      'gemini-3.5-flash';

    // If payload wrapped the Gemini request inside a .body property, unwrap it
    let geminiBody = payload;
    if (payload && typeof payload === 'object' && payload.body && payload.body.contents) {
      geminiBody = payload.body;
    }

    const googleUrl = `https://generativelanguage.googleapis.com/v1beta/models/${encodeURIComponent(
      model
    )}:generateContent?key=${apiKey}`;

    const googleResponse = await fetch(googleUrl, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: typeof geminiBody === 'string' ? geminiBody : JSON.stringify(geminiBody)
    });

    const data = await googleResponse.json();
    return res.status(googleResponse.status).json(data);
  } catch (error) {
    return res.status(500).json({
      error: 'Gemini proxy request failed: ' + (error?.message || String(error)),
      fallbackToLocal: true
    });
  }
}
