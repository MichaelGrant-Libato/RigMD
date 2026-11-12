import { useCallback, useContext, useEffect, useId, useRef, useState } from 'react';
import { executeAppUninstall, getAppUninstallOperation, getBackendErrorMessage, getRecentAppUninstalls, getStorageApps, openInstalledApps, previewAppUninstall, type StorageAppSnapshot } from '../services/autonomyService';
import { type UninstallOperation, type UninstallPreview, type UninstallSnapshot } from '../lib/appUninstall';
import { RemediationReviewAccess } from '../lib/remediationReviewAccess';
import { filterStorageReviewApps, selectedStorageReviewApp, storageReviewAppKey } from '../lib/storageRemovalReview';
import { installationStatusText, removalAvailability } from '../lib/storageReviewLanguage';
import { reviewSelectedAppInWindows, type WindowsAppReviewResult } from '../lib/windowsAppReview';
import WindowsToolGuide from './WindowsToolGuide';

function size(bytes: number | null) {
  return bytes === null ? 'Size not listed' : bytes >= 1024 ** 3 ? `${(bytes / 1024 ** 3).toFixed(1)} GB` : `${(bytes / 1024 ** 2).toFixed(1)} MB`;
}
function failure(error: unknown) {
  return error instanceof Error && !('response' in error) ? error.message : getBackendErrorMessage(error);
}
async function copyAppName(name: string) {
  if (!navigator.clipboard?.writeText) throw new Error('Clipboard access is unavailable.');
  await navigator.clipboard.writeText(name);
}
function Readings({ title, snapshot }: { title: string; snapshot?: UninstallSnapshot | null }) {
  return <section className="space-y-2 rounded-lg border border-white/10 p-3">
    <h5 className="text-sm font-semibold text-white">{title}</h5>
    {!snapshot ? <p className="text-xs text-slate-400">Waiting for a new check</p> : <>
      <p className="text-sm text-slate-200">{installationStatusText(snapshot.installerStatus)}</p>
      <p className="text-xs text-slate-400">{new Date(snapshot.capturedAt).toLocaleString()}</p>
      {snapshot.volumes.length ? snapshot.volumes.map(volume => <p key={volume.drive} className="text-xs text-slate-300">{volume.drive} · {size(volume.freeBytes)} free / {size(volume.totalBytes)}</p>)
        : <p className="text-xs text-slate-400">Free-space readings unavailable</p>}
    </>}
  </section>;
}

export default function StorageAppReview({ metricId, onBusyChange }: { metricId: string; onBusyChange?: (busy: boolean) => void }) {
  const permission = useContext(RemediationReviewAccess);
  const selectionId = useId();
  const [snapshot, setSnapshot] = useState<StorageAppSnapshot | null>(null);
  const [error, setError] = useState('');
  const [revision, setRevision] = useState(0);
  const [storeNotice, setStoreNotice] = useState('');
  const [filter, setFilter] = useState('');
  const [supportedOnly, setSupportedOnly] = useState(false);
  const [limit, setLimit] = useState(8);
  const [selectedId, setSelectedId] = useState('');
  const [preview, setPreview] = useState<UninstallPreview | null>(null);
  const [approved, setApproved] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [starting, setStarting] = useState(false);
  const [operation, setOperation] = useState<UninstallOperation | null>(null);
  const [statusError, setStatusError] = useState('');
  const [historyReady, setHistoryReady] = useState(false);
  const [historyRevision, setHistoryRevision] = useState(0);
  const [pollRevision, setPollRevision] = useState(0);
  const [uncertain, setUncertain] = useState(false);
  const [openingWindows, setOpeningWindows] = useState(false);
  const [windowsReview, setWindowsReview] = useState<WindowsAppReviewResult | null>(null);
  const [windowsError, setWindowsError] = useState('');
  const sequence = useRef(0);
  const invalidatePreview = useCallback(() => { sequence.current++; }, []);
  const uncertainRequest = useRef<{ appId: string; capturedAt: string } | null>(null);
  const apps = filterStorageReviewApps(snapshot?.apps ?? [], filter, supportedOnly);
  const selected = selectedStorageReviewApp(apps, selectedId);
  const operationId = operation?.id;
  const operationStatus = operation?.status;
  const operationAppId = operation?.app.id;
  const busy = previewing || starting || operationStatus === 'running' || openingWindows;

  useEffect(() => {
    onBusyChange?.(busy);
    return () => onBusyChange?.(false);
  }, [busy, onBusyChange]);
  useEffect(() => { if (!permission.allowed) setApproved(false); }, [permission.allowed]);
  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    invalidatePreview();
    setSnapshot(null); setError(''); setSelectedId(''); setPreview(null); setApproved(false); setWindowsReview(null); setWindowsError('');
    setStoreNotice('');
    getStorageApps(controller.signal).then(async value => {
      if (!active) return;
      setSnapshot(value); setStoreNotice('Checking Store apps in the background… Desktop apps are ready to review.');
      try {
        const complete = await getStorageApps(controller.signal, true);
        if (active) { setSnapshot(complete); setStoreNotice(complete.partial ? 'Some Windows app information is unavailable. You can still review the apps shown.' : ''); }
      } catch { if (active) setStoreNotice('Store apps could not be read. Desktop apps are still available. Update the list to try again.'); }
    })
      .catch(error => { if (active) setError(failure(error)); });
    return () => { active = false; controller.abort(); invalidatePreview(); };
  }, [revision, invalidatePreview]);
  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    setHistoryReady(false);
    getRecentAppUninstalls(permission.sessionId, metricId, controller.signal).then(items => {
      if (!active) return;
      setOperation(current => items[0] && (!current || Date.parse(items[0].before.capturedAt) > Date.parse(current.before.capturedAt)) ? items[0] : current);
      setHistoryReady(true); setStatusError('');
      const pending = uncertainRequest.current;
      if (pending && items.some(item => item.app.id === pending.appId && Date.parse(item.before.capturedAt) >= Date.parse(pending.capturedAt))) {
        uncertainRequest.current = null; setUncertain(false);
      }
    }).catch(error => { if (active) setStatusError(failure(error)); });
    return () => { active = false; controller.abort(); };
  }, [permission.sessionId, metricId, historyRevision]);
  useEffect(() => {
    if (!operationId || !operationAppId || operationStatus !== 'running') return;
    const id = operationId;
    const controller = new AbortController();
    let active = true;
    let timer: number | undefined;
    async function poll() {
      try {
        const current = await getAppUninstallOperation(id, permission.sessionId, metricId, operationAppId!, controller.signal);
        if (!active) return;
        setOperation(current); setStatusError('');
        if (current.status === 'running') timer = window.setTimeout(poll, 2000);
        else setRevision(value => value + 1);
      } catch (error) { if (active) setStatusError(`${failure(error)} The uninstall may still be running. Do not repeat it.`); }
    }
    void poll();
    return () => { active = false; controller.abort(); window.clearTimeout(timer); };
  }, [operationId, operationStatus, operationAppId, permission.sessionId, metricId, pollRevision]);

  async function review() {
    if (!selected || !permission.allowed || busy || !historyReady || uncertain) return;
    const request = ++sequence.current;
    setPreviewing(true); setError(''); setPreview(null); setApproved(false);
    try {
      const value = await previewAppUninstall(permission.sessionId, metricId, selected.id);
      if (request === sequence.current) setPreview(value);
    } catch (error) { if (request === sequence.current) setError(failure(error)); }
    finally { if (request === sequence.current) setPreviewing(false); }
  }
  const canUninstall = permission.allowed && !busy && historyReady && !uncertain && approved &&
    selected && preview?.app.id === selected.id && preview.canExecute && Boolean(preview.approvalToken);
  async function uninstall() {
    if (!canUninstall || !preview?.approvalToken || !selected) return;
    if (Date.parse(preview.approvalExpiresAt ?? '') <= Date.now()) { setPreview(null); setApproved(false); setError('This preview expired. Request a new preview.'); return; }
    setStarting(true); setError(''); setApproved(false);
    uncertainRequest.current = { appId: selected.id, capturedAt: preview.before.capturedAt };
    try {
      setOperation(await executeAppUninstall(permission.sessionId, metricId, selected.id, preview.approvalToken));
      setPreview(null);
      uncertainRequest.current = null;
    } catch (error) {
      setPreview(null);
      const status = (error as { response?: { status?: number } })?.response?.status;
      setUncertain(!status || status >= 500);
      if (status && status < 500) uncertainRequest.current = null;
      setError(`${failure(error)} No automatic retry was made. Refresh operation status before trying again.`);
    } finally { setStarting(false); }
  }
  function changeFilter(nextFilter: string, nextSupportedOnly: boolean) {
    if (busy) return;
    invalidatePreview();
    setFilter(nextFilter); setSupportedOnly(nextSupportedOnly); setLimit(8);
    setSelectedId(''); setPreview(null); setApproved(false); setError(''); setWindowsReview(null); setWindowsError('');
  }
  async function openSelectedInWindows() {
    if (!selected || busy) return;
    const app = selected;
    setOpeningWindows(true); setWindowsReview(null); setWindowsError(''); setApproved(false);
    try { setWindowsReview(await reviewSelectedAppInWindows(app, copyAppName, openInstalledApps)); }
    catch (error) { setWindowsError(failure(error)); }
    finally { setOpeningWindows(false); }
  }
  return <section aria-label="Installed apps storage review" className="space-y-4">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><h4 className="text-lg font-bold text-white">Choose an app you no longer need</h4><p className="mt-1 text-sm text-slate-400">1. Choose an app · 2. Check what will change · 3. Approve removal</p></div>
      <button type="button" disabled={busy} onClick={() => setRevision(value => value + 1)} className="min-h-10 rounded-lg border border-white/20 px-3 py-2 text-sm text-slate-200 disabled:opacity-50">Update app list</button>
    </div>
    <p className="text-sm text-slate-400">Sizes are estimates. A large app is not necessarily unused or safe to remove.</p>
    {storeNotice && <p role="status" className="text-xs text-slate-400">{storeNotice}</p>}
    <details className="rounded-lg border border-white/10 p-3 text-xs text-slate-400"><summary className="cursor-pointer font-semibold text-slate-300">Why some apps can’t be removed here</summary>
      <p className="mt-2">RigMD can remove only apps that use a supported Windows installation method. It also keeps recognized Windows, security and shared software protected. This does not mean every other app is safe to remove.</p>
      <p className="mt-2">{snapshot?.summary ?? 'Installer-reported estimates are not measured disk use or guaranteed recoverable space. Last-used history and removal safety are unknown.'}</p>
      <p className="mt-2">This list comes from Windows on this PC. RigMD supports eligible Windows Installer apps, verified official desktop uninstallers, and eligible Store apps for your account. Some apps will show their own confirmation or Windows permission prompt. Protected software and uninstallers that cannot be verified are not forced to uninstall.</p>
      {snapshot && <p className="mt-2">Read {new Date(snapshot.capturedAt).toLocaleString()} · {snapshot.apps.length} registrations{snapshot.partial ? ' · Partial inventory' : ''}</p>}
    </details>
    {error && <p role="alert" className="rounded-lg border border-amber-400/30 p-3 text-sm text-amber-200">{error}</p>}
    {statusError && <div role="alert" className="space-y-2 rounded-lg border border-amber-400/30 p-3 text-sm text-amber-200"><p>{statusError}</p>
      <button type="button" onClick={() => { setHistoryRevision(value => value + 1); setPollRevision(value => value + 1); }} className="min-h-10 rounded-lg border border-white/20 px-3 py-2">Refresh operation status</button>
    </div>}
    {uncertain && <p role="status" className="text-sm text-amber-200">The request outcome is uncertain. Check recent operations before preparing another uninstall.
      <button type="button" onClick={() => setHistoryRevision(value => value + 1)} className="ml-2 underline">Check operations</button></p>}
    <div className="grid min-w-0 gap-5 lg:grid-cols-[minmax(0,1fr)_minmax(280px,0.85fr)]">
      <div className="min-w-0 space-y-3">
        <label className="block text-sm text-slate-200">Find an app<input type="search" placeholder="Type an app name" value={filter} disabled={busy} onChange={event => changeFilter(event.target.value, supportedOnly)} className="mt-2 block w-full rounded-lg border border-white/20 bg-[#101821] px-3 py-2 disabled:opacity-50" /></label>
        <label className="flex min-h-10 items-center gap-2 text-sm text-slate-300"><input type="checkbox" checked={supportedOnly} disabled={busy} onChange={event => changeFilter(filter, event.target.checked)} />Only show apps with an uninstall option</label>
        {!snapshot ? <p role="status" className="text-sm text-slate-400">{error ? 'Inventory unavailable. Try Refresh list.' : 'Reading app registrations…'}</p>
          : !apps.length ? <p className="text-sm text-slate-400">No matching app registrations. This is not proof that no apps are installed.</p>
            : <ul className="space-y-2" aria-label="Apps by reported size">{apps.slice(0, limit).map(app => <li key={app.id}>
              <label className={`flex cursor-pointer items-start gap-3 rounded-lg border p-3 ${selectedId === app.id ? 'border-cyan-400/60 bg-cyan-400/5' : 'border-white/10 bg-black/10'}`}>
                <input type="radio" name={`${selectionId}-app`} aria-label={`Select ${app.name}`} checked={selectedId === app.id} disabled={busy || !permission.allowed}
                  onChange={() => { invalidatePreview(); setSelectedId(storageReviewAppKey(app)); setPreview(null); setApproved(false); setError(''); setWindowsReview(null); setWindowsError(''); }} className="mt-1" />
                <span className="min-w-0 flex-1"><span className="flex flex-wrap justify-between gap-2"><strong className="text-sm text-white">{app.name}</strong><span className="text-sm text-cyan-200">{size(app.estimatedBytes)}</span></span>
                  <span className="mt-1 block text-xs text-slate-400">{app.publisher || 'App publisher not listed'} · {app.sourceType === 'store' ? 'Windows Store' : 'Desktop app'}</span>
                  <span className={`mt-2 block text-xs ${app.canUninstall ? 'text-cyan-200' : 'text-slate-400'}`}>{removalAvailability(app)}</span>
                </span>
              </label>
            </li>)}</ul>}
        {apps.length > limit && <button type="button" onClick={() => setLimit(value => value + 8)} className="min-h-10 rounded-lg border border-white/20 px-3 py-2 text-sm text-slate-200">Show more apps ({apps.length - limit} remaining)</button>}
      </div>
      <div className="min-w-0 space-y-4 self-start lg:sticky lg:top-4">
        <section className="space-y-4 rounded-xl border border-cyan-400/25 bg-black/20 p-4" aria-label="Uninstall preview and approval">
          <h4 className="font-bold text-white">{selected?.name ?? 'Choose an app from the list'}</h4>
          {!selected ? <p className="text-sm text-slate-400">Nothing is removed just by choosing it. You will review and approve any removal here.</p> : <>
            <p className="text-sm text-slate-400">{size(selected.estimatedBytes)} estimated size</p>
            <details className="text-xs text-slate-400"><summary className="cursor-pointer">App details</summary><p className="mt-2">{selected.publisher || 'Publisher not listed'} · {selected.version || 'Version not listed'} · {selected.scope}</p></details>
            {(!selected.canUninstall || preview?.app.id === selected.id && !preview.canExecute) ? <>
              <p className="text-sm text-slate-300">{preview?.app.id === selected.id ? preview.summary : selected.uninstallReason || 'RigMD cannot offer removal for this app. Nothing has been changed.'}</p>
              <div className="space-y-3 border-t border-white/10 pt-4">
                <h5 className="text-sm font-semibold text-white">Review this app in Windows</h5>
                <p className="text-sm leading-relaxed text-slate-400">Open Windows Installed apps, then paste <strong className="text-slate-200">{selected.name}</strong> into its search box. This button also copies the app’s name.</p>
                <WindowsToolGuide target="installed apps" appName={selected.name} onOpen={openSelectedInWindows} busy={openingWindows} disabled={busy} />
                <p className="text-xs leading-relaxed text-slate-400">Windows does not provide a reliable direct link to every individual app. It will show whether removal is available. Opening this page removes nothing; any uninstall is confirmed in Windows.</p>
                {windowsReview?.appId === selected.id && <div role="status" className="space-y-2 rounded-lg border border-cyan-400/20 bg-cyan-400/5 p-3 text-sm text-slate-200"><p>{windowsReview.summary}</p><p>{windowsReview.nameCopied ? `“${windowsReview.appName}” was copied. Paste it into Windows’ app search box.` : `The name could not be copied. Search Windows Installed apps for “${windowsReview.appName}”.`}</p><p className="text-xs text-slate-400">After making any change there, return to RigMD and use Check again.</p></div>}
                {windowsError && <p role="alert" className="text-sm text-amber-200">{windowsError} You can open Settings → Apps → Installed apps yourself and search for “{selected.name}”.</p>}
              </div>
            </> : <>
            <p className="text-sm text-amber-200">Removing this app may also remove its settings or saved data. Save your work first. RigMD cannot undo the removal.</p>
            <button type="button" onClick={review} disabled={!permission.allowed || busy || !historyReady || uncertain}
              className="min-h-11 w-full rounded-lg border border-cyan-400/40 px-3 py-2 text-sm font-bold text-cyan-200 disabled:opacity-50">{previewing ? 'Checking the app…' : 'Check before removing'}</button>
            </>}
            {preview && preview.app.id === selected.id && <>
              {preview.canExecute && <p className="text-sm text-slate-300">{preview.summary}</p>}<Readings title="Before removal" snapshot={preview.before} />
              {preview.canExecute && <>
                <label className="flex items-start gap-2 text-sm text-amber-100"><input type="checkbox" checked={approved} disabled={busy || !permission.allowed} onChange={event => setApproved(event.target.checked)} className="mt-1" /><span>I approve uninstalling <strong>{preview.app.name}</strong>. I have saved my work.</span></label>
                <button type="button" onClick={uninstall} disabled={!canUninstall} className="min-h-11 w-full rounded-lg bg-red-500/20 px-3 py-2 text-sm font-bold text-red-200 disabled:opacity-40">{starting ? 'Starting removal…' : 'Remove this app'}</button>
              </>}
            </>}
          </>}
        </section>
        {operation && <section aria-label="Uninstall outcome" className="space-y-3 rounded-xl border border-white/15 p-4">
          <h4 className="font-bold text-white">{operation.app.name} · {operation.status === 'running' ? 'Removing app…' : 'Removal result'}</h4>
          <p role="status" className="text-sm text-slate-300">{operation.summary}</p>
          <Readings title="Before removal" snapshot={operation.before} /><Readings title="After removal" snapshot={operation.after} />
          {operation.exitCode != null && <details className="text-xs text-slate-400"><summary className="cursor-pointer">Details for support</summary><p className="mt-2">Uninstaller result code: {operation.exitCode}</p></details>}
          <p className="text-xs text-slate-400">Other activity can also change free space. Use Check again to see whether the storage warning has cleared.</p>
        </section>}
      </div>
    </div>
  </section>;
}
