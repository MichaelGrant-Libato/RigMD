import { useCallback, useContext, useEffect, useRef, useState } from 'react';
import { AppWindow, ArrowLeft, ChevronRight, Cloud, FileText, Folder, HardDrive, Image, Monitor, Music, RefreshCw, Trash2, Video, type LucideIcon } from 'lucide-react';
import { getBackendErrorMessage, getStorageOverview } from '../services/autonomyService';
import { categorySize, storageSize, type StorageCategoryId, type StorageOverview } from '../lib/storageOverview';
import { RemediationReviewAccess } from '../lib/remediationReviewAccess';
import StorageAppReview from './StorageAppReview';
import StorageFileCleanup from './StorageFileCleanup';

const icons: Record<StorageCategoryId, LucideIcon> = { installed_apps: AppWindow, temporary_files: Trash2, documents: FileText, pictures: Image, desktop: Monitor, music: Music, videos: Video, system_other: HardDrive, cloud: Cloud };
const primaryCategories: StorageCategoryId[] = ['installed_apps', 'documents', 'temporary_files'];
export type StorageCleanupTool = 'clear_temp_files' | 'clear_browser_cache' | 'clear_windows_update_cache';
const button = 'inline-flex min-h-10 items-center justify-center gap-2 rounded-lg border border-white/15 px-3 py-2 text-sm text-slate-200 hover:border-cyan-400/50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-300 disabled:opacity-40';

export default function StorageReview({ metricId, onBusyChange, onReviewCleanup }: {
  metricId: string;
  onBusyChange: (busy: boolean) => void;
  onReviewCleanup: (tool: StorageCleanupTool) => void;
}) {
  const permission = useContext(RemediationReviewAccess);
  const [snapshot, setSnapshot] = useState<StorageOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [revision, setRevision] = useState(0);
  const [page, setPage] = useState<StorageCategoryId | 'overview'>('overview');
  const [more, setMore] = useState(false);
  const [busy, setBusy] = useState(false);
  const storageChanged = useCallback(() => setRevision(value => value + 1), []);
  const heading = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    setLoading(true); setError('');
    getStorageOverview(metricId, controller.signal).then(data => { if (active) setSnapshot(data); })
      .catch(error => { if (active) setError(error instanceof Error && !('response' in error) ? error.message : getBackendErrorMessage(error)); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [metricId, revision]);
  useEffect(() => {
    onBusyChange(busy);
    return () => onBusyChange(false);
  }, [busy, onBusyChange]);
  useEffect(() => {
    heading.current?.focus();
    heading.current?.scrollIntoView({ block: 'nearest' });
  }, [page]);

  const category = snapshot?.categories.find(category => category.id === page);
  const volume = snapshot?.volume;
  const used = volume ? volume.totalBytes - volume.freeBytes : null;
  const usedPercent = volume ? (volume.totalBytes - volume.freeBytes) / volume.totalBytes * 100 : 0;
  const categories = snapshot?.categories.filter(category => more || primaryCategories.includes(category.id)) ?? [];
  const returnToOverview = () => { if (!busy) { setPage('overview'); setRevision(value => value + 1); } };

  return <section aria-label="Storage overview and categories" className="space-y-5">
    <nav aria-label="Storage review navigation" className="flex flex-wrap items-center justify-between gap-3">
      <div className="flex min-w-0 items-center gap-2 text-sm text-slate-300">
        {page !== 'overview' ? <button type="button" disabled={busy} onClick={returnToOverview} className={button}><ArrowLeft size={16} />Storage overview</button> : <span className="font-semibold text-cyan-200">Storage overview</span>}
        {page !== 'overview' && <><ChevronRight size={15} className="shrink-0" /><span>{category?.name ?? 'Storage category'}</span></>}
      </div>
      {page === 'overview' && <button type="button" disabled={loading || busy} onClick={() => setRevision(value => value + 1)} className={button}><RefreshCw size={15} className={loading ? 'animate-spin' : ''} />{loading ? 'Reading storage…' : 'Refresh storage'}</button>}
    </nav>
    <h4 ref={heading} tabIndex={-1} className="text-xl font-bold text-white outline-none">{page === 'overview' ? 'Free up space on this drive' : category?.name ?? 'Storage category'}</h4>
    {page === 'overview' && <section aria-label="Ways to free storage space" className="space-y-4 rounded-xl border border-cyan-400/30 bg-cyan-400/5 p-4 sm:p-5">
      <div><h5 className="font-bold text-white">Start with a cleanup</h5><p className="mt-1 text-sm text-slate-300">Choose what to clean, review the actual preview, then approve the change. RigMD checks free space afterward.</p></div>
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="space-y-2 rounded-lg border border-white/10 bg-black/10 p-3"><h6 className="font-semibold text-white">Temporary files</h6><p className="text-xs text-slate-400">Review temporary files that the cleanup tool can remove.</p><button type="button" disabled={!permission.allowed || busy} onClick={() => onReviewCleanup('clear_temp_files')} className={button}>Review temporary-file cleanup<ChevronRight size={16} /></button></div>
        <div className="space-y-2 rounded-lg border border-white/10 bg-black/10 p-3"><h6 className="font-semibold text-white">Apps you no longer need</h6><p className="text-xs text-slate-400">Choose an installed app and review its uninstall option.</p><button type="button" disabled={busy} onClick={() => setPage('installed_apps')} className={button}>Review installed apps<ChevronRight size={16} /></button></div>
      </div>
      <details className="text-sm text-slate-300"><summary className="cursor-pointer">More cleanup options</summary><div className="mt-3 flex flex-wrap gap-2"><button type="button" disabled={!permission.allowed || busy} onClick={() => onReviewCleanup('clear_browser_cache')} className={button}>Review browser cache</button><button type="button" disabled={!permission.allowed || busy} onClick={() => onReviewCleanup('clear_windows_update_cache')} className={button}>Review downloaded update files</button></div></details>
    </section>}
    {error && <div role="alert" className="space-y-2 rounded-lg border border-amber-400/30 p-3 text-sm text-amber-200"><p>{error}</p><button type="button" disabled={loading} className={button} onClick={() => setRevision(value => value + 1)}>Try again</button></div>}
    {loading && <p role="status" className="text-sm text-slate-400">Reading drive space and accessible local file sizes. Nothing is being removed.</p>}
    {snapshot && <p className="text-xs text-slate-400">{loading || error ? 'Previous reading' : 'Read'} {new Date(snapshot.capturedAt).toLocaleString()}. {loading || error ? 'The displayed sizes may have changed.' : 'Refresh after making changes.'}</p>}
    {page === 'overview' && volume && used !== null && <>
      <section aria-label="Selected drive storage" className="space-y-3 rounded-xl border border-cyan-400/20 bg-cyan-400/[0.04] p-4 sm:p-5">
        <div className="flex flex-wrap items-center justify-between gap-2"><h5 className="flex items-center gap-2 text-lg font-bold text-white"><HardDrive size={21} />Drive {volume.drive} · {storageSize(volume.totalBytes)}</h5><span className="text-sm text-slate-300">{usedPercent.toFixed(1)}% used</span></div>
        <div role="meter" aria-label={`Drive ${volume.drive} space used`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Number(usedPercent.toFixed(1))} aria-valuetext={`${storageSize(used)} used, ${storageSize(volume.freeBytes)} free`} className="h-3 overflow-hidden rounded-full bg-white/15">
          <div className={`h-full rounded-full ${usedPercent >= 90 ? 'bg-amber-400' : 'bg-cyan-400'}`} style={{ width: `${usedPercent}%` }} />
        </div>
        <div className="flex flex-wrap justify-between gap-2 text-sm"><span className="text-slate-300">{storageSize(used)} used</span><span className="font-semibold text-cyan-200">{storageSize(volume.freeBytes)} free</span></div>
      </section>
      <StorageFileCleanup metricId={metricId} onBusyChange={setBusy} onChanged={storageChanged} />
      <div><h5 className="font-semibold text-white">Find files and apps to remove</h5><p className="mt-1 text-sm text-slate-400">Open a category to choose specific items. Personal files go to the Recycle Bin first.</p></div>
      <ul className="space-y-2">{categories.map(category => {
        const Icon = icons[category.id] ?? Folder;
        return <li key={category.id}><button type="button" onClick={() => setPage(category.id)} className="flex min-h-20 w-full items-center gap-4 rounded-xl border border-white/10 bg-white/[0.025] p-4 text-left transition-colors hover:border-cyan-400/40 hover:bg-cyan-400/5 focus-visible:outline focus-visible:outline-2 focus-visible:outline-cyan-300">
          <Icon size={24} className="shrink-0 text-cyan-300" /><span className="min-w-0 flex-1"><span className="flex flex-wrap items-center justify-between gap-2"><strong className="text-sm text-white">{category.name}</strong><span className="text-sm text-cyan-200">{categorySize(category)}</span></span><span className="mt-1 block text-xs text-slate-400">{category.id === 'installed_apps' ? 'Find an app and review removal' : category.id === 'temporary_files' ? 'Check temporary files and review cleanup' : category.state === 'unavailable' ? 'View what this review can tell you' : category.state === 'partial' ? 'Incomplete count · Some files could not be included' : 'Review local files in this category'}</span></span><ChevronRight size={18} className="shrink-0 text-slate-400" />
        </button></li>;
      })}</ul>
      <button type="button" aria-expanded={more} onClick={() => setMore(value => !value)} className="min-h-10 text-sm font-semibold text-cyan-200 underline-offset-4 hover:underline">{more ? 'Show fewer categories' : 'Show more categories'}</button>
      <p className="text-xs leading-relaxed text-slate-400">Folder sizes count accessible local files for this Windows account. They can differ from space used on disk. Some files are skipped, and these categories do not add up to the drive total. Unavailable sizes do not mean a folder is empty.</p>
    </>}
    {page === 'installed_apps' && <><p className="rounded-lg border border-white/10 p-3 text-sm text-slate-400">Read from Windows on this PC. Desktop and Store entries are labeled separately. The list is not filtered by drive: installer estimates do not show where an app’s files and data are stored.</p><StorageAppReview metricId={metricId} onBusyChange={setBusy} /></>}
    {page !== 'overview' && page !== 'installed_apps' && category && <section className="space-y-4 rounded-xl border border-white/10 bg-black/10 p-4 sm:p-5">
      <p className="text-sm leading-relaxed text-slate-300">{category.description}</p>
      <div className="flex flex-wrap items-baseline gap-2"><strong className="text-2xl text-white">{categorySize(category)}</strong>{category.measurement?.fileBytes != null && <span className="text-sm text-slate-400">· {category.measurement.filesCounted.toLocaleString()} files counted</span>}</div>
      {category.state === 'partial' && <p role="status" className="text-sm text-amber-200">This is an incomplete count. The review stops after a short scan and skips files it cannot safely read. The actual folder size may be larger.</p>}
      {category.measurement?.fileBytes != null && <p className="text-xs leading-relaxed text-slate-400">These are file sizes, not guaranteed space you could recover. Online-only files, links and inaccessible items are skipped. File contents are not opened.</p>}
      {['documents', 'pictures', 'desktop', 'music', 'videos'].includes(page) && <StorageFileCleanup key={`${metricId}-${page}`} metricId={metricId} categoryId={page} onBusyChange={setBusy} onChanged={storageChanged} />}
      {page === 'temporary_files' && <div className="space-y-3 border-t border-white/10 pt-4"><p className="text-sm text-slate-300">Get a fresh cleanup preview with the exact locations and estimated size. Nothing is deleted until you approve and run that action.</p><button type="button" disabled={!permission.allowed || busy} onClick={() => onReviewCleanup('clear_temp_files')} className={`${button} border-cyan-400/40 text-cyan-200`}>Review temporary file cleanup<ChevronRight size={16} /></button></div>}
      {['system_other', 'cloud'].includes(page) && <div className="space-y-3 border-t border-white/10 pt-4"><p className="text-sm text-slate-400">System files and online-only files are protected here. To free space, review temporary-file cleanup or return to your personal files and apps.</p><button type="button" disabled={!permission.allowed || busy} onClick={() => onReviewCleanup('clear_temp_files')} className={button}>Review cleanup options</button></div>}
    </section>}
  </section>;
}
