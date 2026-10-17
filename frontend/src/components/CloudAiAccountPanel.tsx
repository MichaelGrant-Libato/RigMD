import { useEffect, useState, type FormEvent } from 'react';
import { LogIn, LogOut, Loader2, ShieldCheck } from 'lucide-react';
import { getCloudAiAccount, signInCloudAi, signOutCloudAi, type CloudAiAccount } from '../services/cloudAiService';
import { getBackendErrorMessage } from '../services/autonomyService';

export default function CloudAiAccountPanel({ onAccountChanged }: { onAccountChanged: () => void }) {
  const [account, setAccount] = useState<CloudAiAccount | null>(null);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [accountLoaded, setAccountLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let active = true;
    getCloudAiAccount().then(value => { if (active) setAccount(value); })
      .catch(() => { if (active) setError('Cloud account status is unavailable.'); })
      .finally(() => { if (active) setAccountLoaded(true); });
    return () => { active = false; };
  }, []);

  const signIn = async (event: FormEvent) => {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setError(null);
    try { setAccount(await signInCloudAi(email.trim(), password)); onAccountChanged(); }
    catch (err) { setError(getBackendErrorMessage(err)); }
    finally { setPassword(''); setBusy(false); }
  };
  const signOut = async () => {
    if (busy) return;
    setBusy(true);
    setError(null);
    try { setAccount(await signOutCloudAi()); onAccountChanged(); }
    catch (err) { setError(getBackendErrorMessage(err)); }
    finally { setBusy(false); }
  };
  return (
    <section className="border-b border-[var(--rigmd-border)] pb-6" aria-labelledby="cloud-account-title">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="cloud-account-title" className="flex items-center gap-2 text-base font-bold text-white">
          <ShieldCheck size={18} className="text-cyan-300" /> Cloud AI account
        </h2>
        <span className="text-sm text-slate-300">{!accountLoaded ? 'Checking account...' : account?.signedIn ? 'Signed in for this app session' : 'Signed out - Offline checks available'}</span>
      </div>
      {account?.signedIn ? (
        <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
          <p className="min-w-0 break-all text-sm text-emerald-300">{account.email}</p>
          <button type="button" disabled={busy} onClick={() => void signOut()}
            className="inline-flex items-center gap-2 rounded-lg border border-white/20 px-4 py-2 text-sm text-white disabled:opacity-50">
            <LogOut size={16} /> Sign out
          </button>
        </div>
      ) : (
        <form onSubmit={event => void signIn(event)} className="mt-4 grid items-end gap-4 md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto]">
          <label className="min-w-0 text-sm text-slate-300">
            Email
            <input name="email" type="email" autoComplete="username" maxLength={254} required disabled={busy}
              value={email} onChange={event => setEmail(event.target.value)}
              className="mt-1 block w-full min-w-0 rounded-lg border border-white/20 bg-[#0b1118] px-3 py-2 text-white focus:border-cyan-400 focus:outline-none" />
          </label>
          <label className="min-w-0 text-sm text-slate-300">
            Password
            <input name="password" type="password" autoComplete="current-password" maxLength={1024} required disabled={busy}
              value={password} onChange={event => setPassword(event.target.value)}
              className="mt-1 block w-full min-w-0 rounded-lg border border-white/20 bg-[#0b1118] px-3 py-2 text-white focus:border-cyan-400 focus:outline-none" />
          </label>
          <button type="submit" disabled={busy || !accountLoaded || !email.trim() || !password}
            className="inline-flex min-h-10 items-center justify-center gap-2 rounded-lg bg-cyan-400 px-4 py-2 text-sm font-bold text-[#041014] disabled:opacity-50">
            {busy ? <Loader2 size={16} className="animate-spin" /> : <LogIn size={16} />} Sign in
          </button>
        </form>
      )}
      {error && <p role="alert" className="mt-3 break-words text-sm text-red-300">{error}</p>}
    </section>
  );
}
