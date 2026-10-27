import { useEffect, useState } from 'react';
import { ShieldCheck, Monitor, Loader2 } from 'lucide-react';
import { Link, useLocation } from 'react-router-dom';
import { supabase, isEmailVerified } from '../lib/supabase';
import { DESKTOP_CONNECT_CALLBACK, readDesktopConnectRequest } from '../lib/desktopAccountConnect';

export default function DesktopConnectPage() {
  const location = useLocation();
  const [request] = useState(() => readDesktopConnectRequest(location.hash));
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  useEffect(() => {
    // Keep the website Origin on the HTTPS-to-loopback form POST. No path or fragment is disclosed.
    const policy = document.createElement('meta'); policy.name = 'referrer'; policy.content = 'origin';
    document.head.append(policy);
    return () => policy.remove();
  }, []);
  useEffect(() => {
    let active = true;
    if (!supabase) { setLoading(false); return; }
    void supabase.auth.getUser().then(({ data }) => { if (active) setUser(data.user); })
      .catch(() => { if (active) setError('Could not check your website account.'); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);
  const connect = async () => {
    if (busy || !request || !user) return;
    setBusy(true); setError('');
    try {
      const { data } = await supabase.auth.getSession();
      if (!data.session) throw new Error('Sign in on the RigMD website and try again.');
      const current = await supabase.auth.getUser(data.session.access_token);
      if (current.error || !isEmailVerified(current.data.user)) throw new Error('Verify your website account and try again.');
      if (current.data.user.id !== user.id) {
        setUser(current.data.user);
        throw new Error('Your website account changed. Review the account shown here, then connect again.');
      }
      const response = await fetch('/api/desktop-connect', { method: 'POST', cache: 'no-store',
        headers: { Authorization: `Bearer ${data.session.access_token}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({ state: request.state, publicKey: request.publicKey }) });
      const payload = await response.json();
      if (!response.ok) throw new Error(payload.message || 'Could not connect this PC.');
      // Top-level form navigation works without opening local API access to cross-site fetches.
      // Only encrypted, short-lived, PC-bound data crosses the loopback connection.
      const form = document.createElement('form'); form.method = 'POST'; form.action = DESKTOP_CONNECT_CALLBACK;
      for (const [name, value] of Object.entries({ state: request.state, payload: JSON.stringify(payload) })) {
        const input = document.createElement('input'); input.type = 'hidden'; input.name = name; input.value = value; form.append(input);
      }
      document.body.append(form); form.submit();
    } catch (err) { setError(err.message || 'Could not connect this PC.'); setBusy(false); }
  };
  return <main className="flex min-h-screen items-center justify-center bg-[#0b1118] p-6 text-slate-200">
    <section className="w-full max-w-lg rounded-2xl border border-cyan-400/30 bg-[#14212d] p-7 shadow-xl">
      <ShieldCheck size={36} className="mb-5 text-cyan-300" />
      <h1 className="text-2xl font-bold text-white">Connect RigMD on this PC</h1>
      <p className="mt-3 leading-7">Use your website account in the desktop app. RigMD will remember the connection on this Windows account.</p>
      {!request ? <p role="alert" className="mt-5 text-amber-300">Open RigMD Desktop and choose Connect website account to start a fresh connection.</p> : loading ?
        <p className="mt-5">Checking your website account…</p> : !user || !isEmailVerified(user) ? <div className="mt-5">
          <p>Sign in with your verified RigMD account to continue.</p>
          <Link className="mt-4 inline-block text-cyan-300 underline" to="/login" state={{ from: location }}>Continue to website sign-in</Link>
          <p className="mt-4 text-sm text-slate-400">Already signed in at the other RigMD address? <a className="text-cyan-300 underline" href={`${window.location.hostname === 'www.rig-md.com' ? 'https://rig-md.com' : 'https://www.rig-md.com'}/desktop-connect${location.hash}`}>Use that website session</a>.</p>
        </div> : <>
          <p className="mt-5 break-all font-semibold text-emerald-300">{user.email}</p>
          <div className="my-5 rounded-xl border border-white/15 p-4"><p>Confirm this code matches the one in RigMD Desktop:</p>
            <p className="mt-2 font-mono text-2xl tracking-widest text-cyan-300">{request.code}</p></div>
          <p className="text-sm leading-6 text-slate-300">Only connect if you started this request on this PC. Selected diagnostic readings may be sent for AI explanations when Gemini is enabled. Disconnect anytime in Settings.</p>
          <button disabled={busy} onClick={() => void connect()} className="mt-6 flex w-full items-center justify-center gap-2 rounded-xl bg-cyan-400 px-5 py-3 font-bold text-[#041014] disabled:opacity-50">
            {busy ? <Loader2 size={18} className="animate-spin" /> : <Monitor size={18} />} Connect this PC
          </button>
        </>}
      {error && <p role="alert" className="mt-4 text-red-300">{error}</p>}
    </section>
  </main>;
}
