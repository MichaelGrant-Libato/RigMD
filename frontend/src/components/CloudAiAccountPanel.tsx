import { useEffect, useState } from 'react';
import { Link2, LogOut, Loader2, ShieldCheck } from 'lucide-react';
import { getCloudAiAccount, signOutCloudAi, startWebsiteConnection, getWebsiteConnection, cancelWebsiteConnection,
  type CloudAiAccount, type WebsiteConnection } from '../services/cloudAiService';
import { getBackendErrorMessage } from '../services/autonomyService';

export default function CloudAiAccountPanel({ onAccountChanged, compact = false }: { onAccountChanged?: () => void; compact?: boolean }) {
  const [account, setAccount] = useState<CloudAiAccount | null>(null);
  const [connection, setConnection] = useState<WebsiteConnection | null>(null);
  const [busy, setBusy] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let active = true;
    void Promise.all([getCloudAiAccount(), getWebsiteConnection()]).then(async ([value, pending]) => {
      if (!active) return;
      setAccount(value); setConnection(pending);
      if (!value.signedIn && pending.state === 'connected') {
        const current = await getCloudAiAccount();
        if (active) { setAccount(current); window.dispatchEvent(new Event('rigmd:account-changed')); }
      } else if (value.signedIn) window.dispatchEvent(new Event('rigmd:account-changed'));
    }).catch(() => { if (active) setError('Account status is unavailable. Local checks still work.'); })
      .finally(() => { if (active) setLoaded(true); });
    return () => { active = false; };
  }, []);
  const waiting = connection?.state === 'waiting' || connection?.state === 'connecting';
  useEffect(() => {
    if (!waiting) return;
    let active = true, polling = false;
    const timer = window.setInterval(() => {
      if (polling) return;
      polling = true;
      void getWebsiteConnection().then(async value => {
        if (!active) return;
        if (value.state === 'connected') {
          const connected = await getCloudAiAccount();
          if (active) { setAccount(connected); window.dispatchEvent(new Event('rigmd:account-changed')); }
        } else if (['error', 'expired', 'canceled'].includes(value.state)) {
          setError(value.state === 'expired' ? 'The connection expired. Start again when you are ready.' : 'Connection not completed. You can try again.');
        }
        if (active) setConnection(value);
      }).catch(() => { if (active) { setConnection(null); setError('Could not check the connection. Please try again.'); } })
        .finally(() => { polling = false; });
    }, 2000);
    return () => { active = false; window.clearInterval(timer); };
  }, [waiting]);
  useEffect(() => {
    const refresh = () => {
      void getCloudAiAccount().then(setAccount).catch(() => {});
      onAccountChanged?.();
    };
    window.addEventListener('rigmd:account-changed', refresh);
    return () => window.removeEventListener('rigmd:account-changed', refresh);
  }, [onAccountChanged]);
  const connect = async () => {
    if (busy || waiting) return;
    setBusy(true); setError(null);
    try { setConnection(await startWebsiteConnection()); }
    catch (err) { setError(getBackendErrorMessage(err)); }
    finally { setBusy(false); }
  };
  const cancel = async () => {
    setBusy(true); setError(null);
    try {
      const value = await cancelWebsiteConnection(); setConnection(value);
      if (value.state === 'connected') {
        setAccount(await getCloudAiAccount()); window.dispatchEvent(new Event('rigmd:account-changed'));
      }
    }
    catch (err) { setError(getBackendErrorMessage(err)); }
    finally { setBusy(false); }
  };
  const disconnect = async () => {
    setBusy(true); setError(null);
    try { setAccount(await signOutCloudAi()); setConnection(null); window.dispatchEvent(new Event('rigmd:account-changed')); }
    catch (err) { setError(getBackendErrorMessage(err)); }
    finally { setBusy(false); }
  };
  return <section className={compact ? 'mx-6 mt-4 shrink-0 rounded-xl border border-cyan-400/20 bg-[#101c26] p-4 lg:mx-8' : 'rounded-2xl border border-cyan-400/20 bg-[#101821] p-6'} aria-label="RigMD account">
    <div className="flex flex-wrap items-center justify-between gap-4">
      <div className="min-w-0">
        <h2 className="flex items-center gap-2 font-bold text-white"><ShieldCheck size={18} className="text-cyan-300" /> RigMD account</h2>
        <p aria-live="polite" className="mt-2 break-words text-sm text-slate-300">{!loaded ? 'Checking your connection…' : account?.signedIn ?
          `Connected as ${account.email}` : account?.reconnecting ? 'Your remembered account will reconnect when the website is available.' :
          'Use the account already signed in on the RigMD website. No separate AI account is needed.'}</p>
        {account?.signedIn && <p className="mt-1 text-sm text-slate-400">{account.remembered ? 'This Windows account will reconnect automatically next time.' : 'Connected for this launch. RigMD could not remember this connection.'}</p>}
      </div>
      {account?.signedIn ? !compact && <button type="button" disabled={busy} onClick={() => void disconnect()} className="inline-flex items-center gap-2 rounded-lg border border-white/20 px-4 py-2 text-sm text-white disabled:opacity-50"><LogOut size={16} /> Disconnect this PC</button> :
        !waiting && <button type="button" disabled={busy || !loaded} onClick={() => void connect()} className="inline-flex shrink-0 items-center gap-2 rounded-lg bg-cyan-400 px-4 py-2 text-sm font-bold text-[#041014] disabled:opacity-50">
          {busy ? <Loader2 size={16} className="animate-spin" /> : <Link2 size={16} />} Connect website account
        </button>}
    </div>
    {waiting && <div className="mt-4 rounded-xl border border-cyan-400/30 bg-cyan-400/5 p-4" role="status">
      {connection?.state === 'connecting' ? <p className="flex items-center gap-2 text-sm text-slate-200"><Loader2 size={16} className="animate-spin" /> Finishing your account connection…</p> : <>
        <p className="text-sm text-slate-200">In your browser, confirm this matching code and choose <strong>Connect this PC</strong>:</p>
        <p className="my-3 font-mono text-2xl tracking-widest text-cyan-300">{connection?.code}</p>
        <p className="text-sm text-slate-400">Waiting for your website account. This request expires after two minutes.</p>
      </>}
      <button type="button" disabled={busy || connection?.state === 'connecting'} onClick={() => void cancel()} className="mt-3 rounded-lg border border-white/20 px-4 py-2 text-sm text-white">Cancel connection</button>
    </div>}
    {!compact && <p className="mt-3 text-sm text-slate-400">Local diagnostics remain available offline. Disconnecting this PC keeps your website session and saved checks.</p>}
    {error && <p role="alert" className="mt-3 break-words text-sm text-red-300">{error}</p>}
  </section>;
}
