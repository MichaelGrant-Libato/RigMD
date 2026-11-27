import { memo, useEffect, useMemo, useState } from 'react';
import { ChevronDown, Package, RefreshCw, Search } from 'lucide-react';
import { apiGet } from '../lib/api';
import { getApiErrorMessage } from '../lib/apiErrorMessage';

interface InstalledApp {
  id: string;
  name: string;
  publisher?: string | null;
  version?: string | null;
  description?: string | null;
  source: string;
  installPath?: string | null;
  drive?: string | null;
  estimatedBytes?: number | null;
  registeredDate?: string | null;
  iconPng?: string | null;
}

interface InstalledAppsSnapshot {
  apps: InstalledApp[];
  partial: boolean;
}

function estimatedSize(bytes?: number | null) {
  if (bytes == null || bytes <= 0) return null;
  if (bytes >= 1024 ** 3) return (bytes / 1024 ** 3).toFixed(1) + ' GB';
  if (bytes >= 1024 ** 2) return (bytes / 1024 ** 2).toFixed(1) + ' MB';
  return (bytes / 1024).toFixed(0) + ' KB';
}

function AppIcon({ png }: { png?: string | null }) {
  const [unreadable, setUnreadable] = useState(false);
  return <span className="grid size-10 shrink-0 place-items-center rounded-lg bg-cyan-400/10 text-cyan-300">
    {png && !unreadable
      ? <img src={'data:image/png;base64,' + png} width={24} height={24} alt="" loading="lazy"
          className="size-6 object-contain" onError={() => setUnreadable(true)} />
      : <Package size={19} aria-hidden="true" />}
  </span>;
}

function AppRow({ app }: { app: InstalledApp }) {
  const size = estimatedSize(app.estimatedBytes);
  const info = [
    ['Source', app.source],
    ['Publisher', app.publisher],
    ['Version', app.version],
    ['Registered date', app.registeredDate && new Date(app.registeredDate + 'T00:00:00').toLocaleDateString()],
    ['Estimated size', size],
    ['Registered location', app.installPath],
  ];

  return <li>
    <details className="group/app">
      <summary className="flex min-h-16 cursor-pointer list-none items-center gap-3 px-3 py-2.5 hover:bg-white/[.035] focus-visible:outline focus-visible:outline-inset focus-visible:outline-cyan-400 [&::-webkit-details-marker]:hidden">
        <AppIcon key={app.iconPng ?? ''} png={app.iconPng} />
        <span className="min-w-0 flex-1">
          <span className="block break-words text-sm font-semibold text-slate-100">{app.name}</span>
          <span className="mt-0.5 block break-words text-xs text-slate-400">
            {[app.publisher || app.source, app.version, app.drive].filter(Boolean).join(' · ')}
          </span>
        </span>
        {size && <span className="hidden shrink-0 text-xs text-slate-400 sm:inline">{'~' + size}</span>}
        <ChevronDown size={15} aria-hidden="true" className="shrink-0 text-slate-400 group-open/app:rotate-180" />
      </summary>
      <div className="space-y-3 border-t border-white/5 bg-black/10 px-4 py-3 sm:pl-16">
        <p className="break-words text-sm text-slate-300">{app.description || 'No description registered.'}</p>
        <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-xs sm:grid-cols-3">
          {info.map(([label, value]) => value && <div key={label} className={'min-w-0 ' + (label === 'Registered location' ? 'col-span-full' : '')}>
            <dt className="text-slate-400">{label}</dt>
            <dd className="mt-0.5 break-words text-slate-200">{value}</dd>
          </div>)}
        </dl>
      </div>
    </details>
  </li>;
}

export default memo(function InstalledAppsPanel() {
  const [snapshot, setSnapshot] = useState<InstalledAppsSnapshot | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  const [search, setSearch] = useState('');
  const [drive, setDrive] = useState('');

  useEffect(() => {
    const controller = new AbortController();
    void apiGet<InstalledAppsSnapshot>('/api/device-diagnostics/installed-apps', { signal: controller.signal, timeout: 22000 })
      .then(({ data }) => {
        if (controller.signal.aborted) return;
        setSnapshot(data);
        setDrive(selected => selected && data.apps.some(app => app.drive === selected) ? selected : '');
      })
      .catch(reason => {
        if (!controller.signal.aborted) setError(getApiErrorMessage(reason, 'Installed apps could not be read. Try refreshing the list.'));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [revision]);

  const drives = useMemo(() => {
    const available = snapshot?.apps.flatMap(app => app.drive ? [app.drive] : []);
    return [...new Set(available)].sort();
  }, [snapshot]);

  const visibleApps = useMemo(() => {
    const query = search.trim().toLowerCase();
    return snapshot?.apps.filter(app => (!drive || drive === app.drive) && (!query ||
      [app.name, app.publisher, app.version, app.description].some(value => value?.toLowerCase().includes(query)))) ?? [];
  }, [snapshot, search, drive]);

  return <section aria-label="Installed apps" className="space-y-3">
    <div className="flex flex-wrap gap-2">
      <label className="relative min-w-40 flex-1">
        <span className="sr-only">Search installed apps</span>
        <Search aria-hidden="true" size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
        <input type="search" placeholder="Search apps" value={search} onChange={event => setSearch(event.target.value)}
          className="h-10 w-full rounded-lg border border-[var(--rigmd-border)] bg-[#101821] pl-9 pr-3 text-sm text-slate-100 placeholder:text-slate-400 focus-visible:outline-cyan-400" />
      </label>
      <select aria-label="Filter by app drive" value={drive} onChange={event => setDrive(event.target.value)}
        className="h-10 rounded-lg border border-[var(--rigmd-border)] bg-[#101821] px-3 text-sm text-slate-200 focus-visible:outline-cyan-400">
        <option value="">All drives</option>
        {drives.map(letter => <option key={letter} value={letter}>{letter}</option>)}
      </select>
      <button type="button" disabled={loading} onClick={() => {
        setLoading(true);
        setError(null);
        setRevision(current => current + 1);
      }} className="inline-flex h-10 items-center gap-2 rounded-lg border border-cyan-500/30 px-3 text-sm font-semibold text-cyan-200 hover:bg-cyan-400/10 disabled:opacity-50">
        <RefreshCw aria-hidden="true" size={15} className={loading ? 'animate-spin' : ''} /> Refresh
      </button>
    </div>
    <p role="status" className="text-xs text-slate-400">
      {loading ? 'Reading installed apps…' : snapshot && (
        visibleApps.length === snapshot.apps.length
          ? visibleApps.length + (visibleApps.length === 1 ? ' app' : ' apps')
          : visibleApps.length + ' of ' + snapshot.apps.length + ' apps'
      )}
    </p>
    {error && <p role="alert" className="text-sm text-amber-200">{error}{snapshot && ' Showing the previous list.'}</p>}
    {snapshot?.partial && <p className="text-sm text-amber-200">Windows returned a partial list. Some installed apps may be missing.</p>}
    {snapshot && <p className="text-xs text-slate-400">
      Windows registrations for this PC and the current account. Portable apps and apps without a Windows registration may be missing.
    </p>}
    {visibleApps.length > 0
      ? <ul className="divide-y divide-white/10 overflow-hidden rounded-lg border border-[var(--rigmd-border)] bg-[#101821]">
          {visibleApps.map(app => <AppRow key={app.id} app={app} />)}
        </ul>
      : snapshot && <p className="rounded-lg border border-[var(--rigmd-border)] p-4 text-sm text-slate-300">
          {snapshot.apps.length > 0 ? 'No apps match these filters.' : 'No installed apps were reported by Windows.'}
        </p>}
  </section>;
});
