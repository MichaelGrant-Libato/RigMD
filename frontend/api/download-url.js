export default function handler(req, res) {
  const url =
    process.env.RIGMD_DOWNLOAD_URL ||
    'https://github.com/MichaelGrant-Libato/RigMD/releases/latest/download/RigMD-Setup.exe';

  res.status(200).json({ url });
}