import { useContext, useEffect, useRef, useState } from 'react';
import { RemediationReviewAccess } from '../lib/remediationReviewAccess';
import { storageSize } from '../lib/storageOverview';
import { type CleanupFiles, type CleanupKind, type CleanupOperation, type CleanupPreview } from '../lib/storageCleanup';
import { executeStorageCleanup, getCleanupFiles, getRecentStorageCleanups, getStorageCleanupOperation, previewStorageCleanup } from '../services/storageCleanupService';
import { getBackendErrorMessage } from '../services/autonomyService';

const button = 'min-h-11 rounded-lg border border-cyan-400/35 px-4 py-2 text-sm font-semibold text-cyan-200 disabled:opacity-40';
function message(error: unknown) { return error instanceof Error && !('response' in error) ? error.message : getBackendErrorMessage(error); }

export default function StorageFileCleanup({ metricId, categoryId, onBusyChange, onChanged }: {
  metricId: string; categoryId?: string; onBusyChange: (busy: boolean) => void; onChanged: () => void;
}) {
  const permission = useContext(RemediationReviewAccess);
  const kind: CleanupKind = categoryId ? 'recycle_files' : 'empty_recycle_bin';
  const [list, setList] = useState<CleanupFiles | null>(null);
  const [loading, setLoading] = useState(Boolean(categoryId));
  const [revision, setRevision] = useState(0);
  const [ids, setIds] = useState<string[]>([]);
  const [preview, setPreview] = useState<CleanupPreview | null>(null);
  const [approved, setApproved] = useState(false);
  const [requesting, setRequesting] = useState(false);
  const [error, setError] = useState('');
  const [operation, setOperation] = useState<CleanupOperation | null>(null);
  const [historyReady, setHistoryReady] = useState(false);
  const [historyRevision, setHistoryRevision] = useState(0);
  const [uncertain, setUncertain] = useState(false);
  const pending = useRef<string | null>(null);
  const notified = useRef('');
  const busy = requesting || operation?.status === 'running' || uncertain;
  const operationId = operation?.id;
  const operationStatus = operation?.status;

  useEffect(() => { onBusyChange(busy); return () => onBusyChange(false); }, [busy, onBusyChange]);
  useEffect(() => { if (!permission.allowed) setApproved(false); }, [permission.allowed]);
  useEffect(() => {
    const controller = new AbortController(); let active = true;
    setIds([]); setPreview(null); setApproved(false); setError('');
    if (!categoryId) { setLoading(false); return () => controller.abort(); }
    setLoading(true); setList(null);
    getCleanupFiles(metricId, categoryId, controller.signal).then(value => { if (active) setList(value); })
      .catch(error => { if (active) setError(message(error)); }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [categoryId, metricId, revision, permission.sessionId]);
  useEffect(() => {
    const controller = new AbortController(); let active = true;
    setHistoryReady(false);
    getRecentStorageCleanups(permission.sessionId, metricId, controller.signal).then(items => {
      if (!active) return;
      if (items[0]) setOperation(items[0]);
      if (pending.current && items.some(item => item.before.capturedAt === pending.current)) { pending.current = null; setUncertain(false); }
      setHistoryReady(true); setError('');
    }).catch(error => { if (active) setError(message(error)); });
    return () => { active = false; controller.abort(); };
  }, [permission.sessionId, metricId, historyRevision]);
  useEffect(() => {
    if (!operationId || operationStatus !== 'running') return;
    const controller = new AbortController(); let active = true; let timer: number | undefined;
    async function poll() {
      try {
        const value = await getStorageCleanupOperation(operationId!, permission.sessionId, metricId, controller.signal);
        if (!active) return;
        setOperation(value); setError('');
        if (value.status === 'running') timer = window.setTimeout(poll, 1500);
      } catch (error) { if (active) setError(`${message(error)} Cleanup may still be running. Do not repeat it.`); }
    }
    void poll();
    return () => { active = false; controller.abort(); window.clearTimeout(timer); };
  }, [operationId, operationStatus, permission.sessionId, metricId, historyRevision]);
  useEffect(() => {
    if (!operation || operation.status === 'running' || notified.current === operation.id) return;
    notified.current = operation.id; setPreview(null); setApproved(false); setIds([]);
    setRevision(value => value + 1); onChanged();
  }, [operation, onChanged]);

  async function review() {
    if (busy || !permission.allowed || !historyReady || categoryId && !ids.length) return;
    setRequesting(true); setError(''); setPreview(null); setApproved(false);
    try { setPreview(await previewStorageCleanup(permission.sessionId, metricId, kind, ids)); }
    catch (error) { setError(message(error)); }
    finally { setRequesting(false); }
  }
  async function execute() {
    if (!preview?.canExecute || !preview.approvalToken || !approved || busy || !permission.allowed) return;
    if (Date.parse(preview.approvalExpiresAt ?? '') <= Date.now()) { setPreview(null); setApproved(false); setError('This preview expired. Review the cleanup again.'); return; }
    setRequesting(true); setApproved(false); setError(''); pending.current = preview.capturedAt;
    try {
      setOperation(await executeStorageCleanup(permission.sessionId, metricId, preview.approvalToken));
      pending.current = null; setPreview(null);
    } catch (error) {
      const status = (error as { response?: { status?: number } }).response?.status;
      setUncertain(!status || status >= 500);
      if (status && status < 500) pending.current = null;
      setPreview(null); setError(`${message(error)} No automatic retry was made.`);
    } finally { setRequesting(false); }
  }
  return <section aria-label={categoryId ? 'Choose personal files to recycle' : 'Recycle Bin cleanup'} className="space-y-4 rounded-xl border border-cyan-400/25 bg-cyan-400/[0.025] p-4">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><h5 className="font-bold text-white">{categoryId ? 'Choose files you no longer need' : 'Empty your Recycle Bin'}</h5><p className="mt-1 text-sm text-slate-400">{categoryId ? 'Select individual files, review them, then move them to the Recycle Bin. Nothing is selected automatically.' : 'A separate permanent cleanup for this drive. Review the contents count and size before approving.'}</p></div>
      {categoryId && <button type="button" disabled={loading || busy} onClick={() => setRevision(value => value + 1)} className={button}>Scan folder again</button>}
    </div>
    {loading && <p role="status" className="text-sm text-slate-400">Finding large personal files…</p>}
    {list && <>
      {list.partial && <p className="text-xs text-amber-200">This is a partial scan. More files may be available. Only the files you select will be moved.</p>}
      {!list.files.length ? <p className="text-sm text-slate-400">No eligible personal files were found in this scan. App files, linked files, cloud placeholders and protected files are excluded.</p> : <ul className="space-y-2">{list.files.map(file => <li key={file.id}><label className="flex cursor-pointer items-start gap-3 rounded-lg border border-white/10 p-3 text-sm">
        <input type="checkbox" checked={ids.includes(file.id)} disabled={busy || !permission.allowed} onChange={event => { setIds(current => event.target.checked ? [...current, file.id] : current.filter(id => id !== file.id)); setPreview(null); setApproved(false); }} className="mt-1" />
        <span className="min-w-0 flex-1 break-words text-slate-200">{file.name}</span><span className="shrink-0 text-cyan-200">{storageSize(file.bytes)}</span>
      </label></li>)}</ul>}
      <p className="text-xs text-slate-400">{ids.length} selected · {storageSize(list.files.filter(file => ids.includes(file.id)).reduce((sum, file) => sum + file.bytes, 0))} in selected files. Moving them to the bin does not free space yet.</p>
    </>}
    <button type="button" disabled={busy || !permission.allowed || !historyReady || Boolean(categoryId) && !ids.length} onClick={review} className={button}>{requesting ? 'Checking cleanup…' : categoryId ? 'Review selected files' : 'Review Recycle Bin cleanup'}</button>
    {preview && <section className="space-y-3 border-t border-white/15 pt-4" aria-label="Cleanup preview and approval">
      <p className="text-sm text-slate-200">{preview.summary}</p>
      <p className="text-sm font-semibold text-cyan-200">{preview.kind === 'empty_recycle_bin' ? `${preview.bin.items} items · ${storageSize(preview.bytes)} in the bin` : `${preview.files.length} files · ${storageSize(preview.bytes)}`}</p>
      {preview.files.length > 0 && <ul className="space-y-1 text-xs text-slate-300">{preview.files.map(file => <li key={file.id}>{file.name} · {storageSize(file.bytes)}</li>)}</ul>}
      {preview.canExecute && <><label className="flex items-start gap-2 text-sm text-amber-100"><input type="checkbox" checked={approved} disabled={busy || !permission.allowed} onChange={event => setApproved(event.target.checked)} className="mt-1" /><span>{kind === 'empty_recycle_bin' ? `I approve permanently emptying ALL items in my Recycle Bin on ${preview.drive}. I understand this cannot be undone from the bin.` : 'I have reviewed these files and approve moving only them to the Recycle Bin.'}</span></label>
        <button type="button" disabled={!approved || busy || !permission.allowed} onClick={execute} className={`${button} border-amber-400/40 text-amber-100`}>{kind === 'empty_recycle_bin' ? 'Empty Recycle Bin permanently' : 'Move selected files to Recycle Bin'}</button></>}
    </section>}
    {error && <p role="alert" className="text-sm text-amber-200">{error}</p>}
    {(!historyReady || uncertain || error && operationStatus === 'running') && <button type="button" onClick={() => setHistoryRevision(value => value + 1)} className={button}>Check cleanup status</button>}
    {operation && <section aria-label="Storage cleanup result" className="space-y-2 border-t border-white/15 pt-4">
      <h5 className="font-semibold text-white">{operation.status === 'running' ? 'Cleanup in progress' : 'Cleanup result'}</h5>
      <p role="status" className="text-sm text-slate-200">{operation.summary}</p>
      <p className="text-xs text-slate-400">Before: {storageSize(operation.before.freeBytes)} free{operation.afterFreeBytes !== null ? ` · After: ${storageSize(operation.afterFreeBytes)} free` : ''}</p>
      {operation.afterBin && <p className="text-xs text-slate-400">Recycle Bin on {operation.before.drive}: {operation.afterBin.items} items · {storageSize(operation.afterBin.bytes)}</p>}
      <p className="text-xs text-slate-400">Other activity can change free space. Use Check again to verify the original storage warning.</p>
    </section>}
  </section>;
}
