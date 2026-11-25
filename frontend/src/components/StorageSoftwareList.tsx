import { memo, useCallback, useEffect, useRef, useState } from 'react';
import { ChevronDown, Cpu, ExternalLink, HardDrive, Package, RefreshCw } from 'lucide-react';
import { apiGet, apiPost } from '../lib/api';
import { sortInstalledApps } from '../lib/softwareInventory';

interface InventoryEntry {
  id: string;
  name: string;
  source: string;
  publisher?: string | null;
  provider?: string | null;
  version?: string | null;
  installPath?: string | null;
  estimatedSizeBytes?: number | null;
  volume?: string | null;
  diskIndex?: number | null;
  iconPng?: string | null;
  inf?: string | null;
  status?: string | null;
  details?: string | null;
}

interface Inventory {
  entries: InventoryEntry[];
  partial: boolean;
  drives?: Array<{ diskIndex: number; volumes: string[]; freeGb: number | null }>;
}

type LocationFilter = number | 'all' | 'unknown';

const buttonClass = 'inline-flex items-center justify-center gap-2 rounded-lg border border-slate-700 px-3 py-2 text-sm text-slate-200 hover:bg-slate-800 disabled:opacity-50';
const fieldClass = 'rounded-lg border border-slate-700 bg-[#101821] px-3 py-2 text-sm text-slate-200 focus-visible:outline focus-visible:outline-cyan-400';

function formatSize(bytes: number | null | undefined) {
  if (typeof bytes !== 'number' || !Number.isFinite(bytes) || bytes <= 0) return null;
  const units = [[1024 ** 3, 'GB'], [1024 ** 2, 'MB'], [1024, 'KB']] as const;
  const [divisor, unit] = units.find(([threshold]) => bytes >= threshold) ?? units[2];
  return `${(bytes / divisor).toLocaleString(undefined, { maximumFractionDigits: 1 })} ${unit}`;
}

function InventoryIcon({ apps, iconPng }: { apps: boolean; iconPng?: string | null }) {
  const [failedIcon, setFailedIcon] = useState<string | null>(null);
  const FallbackIcon = apps ? Package : Cpu;
  return (
    <span aria-hidden="true" className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-cyan-400/10 text-cyan-300">
      {apps && iconPng && iconPng !== failedIcon
        ? <img alt="" width={24} height={24} loading="lazy" className="h-6 w-6 object-contain"
            src={'data:image/png;base64,' + iconPng} onError={() => setFailedIcon(iconPng)} />
        : <FallbackIcon size={18} />}
    </span>
  );
}

export default memo(function StorageSoftwareList({
  kind,
  diskIndex,
  onSelectDrive,
}: {
  kind: 'apps' | 'drivers';
  diskIndex?: number | null;
  onSelectDrive?: (index: number) => void;
}) {
  const [inventory, setInventory] = useState<Inventory | null>(null);
  const [error, setError] = useState(false);
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  const [search, setSearch] = useState('');
  const [location, setLocation] = useState<LocationFilter>(diskIndex ?? 'all');
  const [sort, setSort] = useState<'name' | 'size'>('name');
  const [opening, setOpening] = useState(false);
  const [launchError, setLaunchError] = useState(false);
  const settingsVisit = useRef<{ opened: boolean; away: boolean; returned: boolean } | null>(null);
  const apps = kind === 'apps';

  useEffect(() => {
    const controller = new AbortController();
    apiGet<Inventory>('/api/software-inventory/' + kind, { signal: controller.signal })
      .then(({ data }) => {
        if (!controller.signal.aborted) setInventory(data);
      })
      .catch(() => {
        if (!controller.signal.aborted) setError(true);
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [kind, revision]);

  const refresh = useCallback(() => {
    setError(false);
    setLoading(true);
    setRevision(value => value + 1);
  }, []);

  const refreshAfterSettings = useCallback(() => {
    const visit = settingsVisit.current;
    if (!visit?.opened || !visit.returned) return;
    settingsVisit.current = null;
    refresh();
  }, [refresh]);

  useEffect(() => {
    if (!apps) return;
    function leave() {
      const visit = settingsVisit.current;
      if (!visit) return;
      visit.away = true;
      visit.returned = false;
    }
    function returnToRigMd() {
      const visit = settingsVisit.current;
      if (!visit?.away || document.hidden) return;
      visit.returned = true;
      refreshAfterSettings();
    }
    function visibilityChanged() {
      if (document.hidden) leave();
      else if (document.hasFocus()) returnToRigMd();
    }
    window.addEventListener('blur', leave);
    window.addEventListener('focus', returnToRigMd);
    document.addEventListener('visibilitychange', visibilityChanged);
    return () => {
      window.removeEventListener('blur', leave);
      window.removeEventListener('focus', returnToRigMd);
      document.removeEventListener('visibilitychange', visibilityChanged);
    };
  }, [apps, refreshAfterSettings]);

  async function openWindows() {
    const visit = apps ? { opened: false, away: false, returned: false } : null;
    settingsVisit.current = visit;
    setOpening(true);
    setLaunchError(false);
    try {
      const { data } = await apiPost<{ success: boolean }>('/api/remediation/open-target', {
        target: apps ? 'installed apps' : 'device manager',
      });
      if (!data.success) {
        settingsVisit.current = null;
        setLaunchError(true);
      } else if (visit) {
        visit.opened = true;
        if (document.hidden || !document.hasFocus()) visit.away = true;
        refreshAfterSettings();
      }
    } catch {
      settingsVisit.current = null;
      setLaunchError(true);
    } finally {
      setOpening(false);
    }
  }

  const drives = inventory?.drives ?? [];
  const selectedDrive = drives.find(drive => drive.diskIndex === location);
  const visibleLocation = typeof location === 'number' && inventory && !selectedDrive ? 'all' : location;
  const query = search.trim().toLocaleLowerCase();
  const filtered = (inventory?.entries ?? []).filter(entry => {
    if (apps && typeof visibleLocation === 'number' && entry.diskIndex !== visibleLocation) return false;
    if (apps && visibleLocation === 'unknown' && entry.diskIndex != null) return false;
    return !query || [entry.name, entry.publisher, entry.provider, entry.version, entry.installPath, entry.inf, entry.details]
      .some(value => value?.toLocaleLowerCase().includes(query));
  });
  const rows = apps ? sortInstalledApps(filtered, sort) : filtered;
  const windowsLabel = apps ? 'Manage in Windows' : 'Open Device Manager';

  return (
    <section aria-label={apps ? 'Installed apps' : 'Drivers'} className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <input
          type="search"
          aria-label={apps ? 'Search installed apps' : 'Search drivers'}
          placeholder={apps ? 'Search apps' : 'Search drivers'}
          value={search}
          onChange={event => setSearch(event.target.value)}
          className={fieldClass + ' min-w-36 flex-1'}
        />
        {apps && (<>
          <select
            aria-label="App location"
            value={visibleLocation}
            onChange={event => {
              const value = event.target.value;
              if (value === 'all' || value === 'unknown') setLocation(value);
              else {
                const index = Number(value);
                setLocation(index);
                onSelectDrive?.(index);
              }
            }}
            className={fieldClass + ' max-w-full'}
          >
            {!inventory && typeof location === 'number' && <option value={location}>Drive {location}</option>}
            {drives.map(drive => (
              <option key={drive.diskIndex} value={drive.diskIndex}>Drive {drive.diskIndex} · {drive.volumes.join(', ')}</option>
            ))}
            <option value="all">All locations</option>
            <option value="unknown">Location unknown</option>
          </select>
          <select aria-label="Sort apps" className={fieldClass} value={sort}
            onChange={event => setSort(event.target.value === 'size' ? 'size' : 'name')}>
            <option value="name">Name (A–Z)</option>
            <option value="size">Size (largest first)</option>
          </select>
        </>)}
        <button type="button" className={buttonClass} onClick={refresh} disabled={loading}>
          <RefreshCw size={14} aria-hidden="true" /> Refresh
        </button>
        <button type="button" className={buttonClass} onClick={() => void openWindows()} disabled={opening}>
          <ExternalLink size={14} aria-hidden="true" /> {windowsLabel}
        </button>
      </div>
      {launchError && (
        <p role="alert" className="text-sm text-amber-300">
          {apps ? 'Open Settings → Apps → Installed apps to uninstall.' : 'Search for Device Manager in the Windows Start menu.'}
        </p>
      )}
      {inventory?.partial && (
        <p role="status" className="text-sm text-amber-300">Some Windows records could not be read.</p>
      )}
      <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-slate-400">
        <p role="status">
          {loading ? (inventory ? 'Refreshing inventory…' : 'Reading Windows inventory…')
            : error ? (inventory ? 'Refresh failed. Showing the last inventory.' : 'The inventory could not be loaded. Try refreshing.')
            : rows.length === 0 ? (apps && typeof visibleLocation === 'number' && !query ? 'No apps with a known location on this drive.' : 'No matching entries.')
            : apps ? (rows.length === 1 ? '1 app' : rows.length + ' apps')
            : rows.length === 1 ? '1 driver record' : rows.length + ' driver records'}
        </p>
        {apps && selectedDrive?.freeGb != null && (
          <p className="flex items-center gap-1.5">
            <HardDrive size={14} aria-hidden="true" />
            {selectedDrive.freeGb.toLocaleString(undefined, { maximumFractionDigits: 1 })} GB free
          </p>
        )}
      </div>
      {rows.length > 0 && (
        <ul className="divide-y divide-slate-800 overflow-hidden rounded-lg border border-slate-800 bg-[#101821]">
          {rows.map(entry => {
            const company = entry.publisher ?? entry.provider;
            const size = formatSize(entry.estimatedSizeBytes);
            const summary = apps ? [company, entry.volume ?? 'Location unknown'] : [company ?? entry.source, entry.version];
            return (
              <li key={entry.id}>
                <details className="group">
                  <summary className="flex cursor-pointer list-none items-center justify-between gap-3 px-4 py-3 hover:bg-slate-800/50 focus-visible:outline focus-visible:outline-cyan-400 [&::-webkit-details-marker]:hidden">
                    <span className="flex min-w-0 items-center gap-3">
                      <InventoryIcon apps={apps} iconPng={entry.iconPng} />
                      <span className="min-w-0">
                        <span className="block break-words text-sm font-medium text-white">{entry.name}</span>
                        <span className="mt-1 block break-words text-xs text-slate-400">{summary.filter(Boolean).join(' · ')}</span>
                      </span>
                    </span>
                    <span className="flex shrink-0 items-center gap-2 text-xs text-slate-400">
                      {apps && size && <span title="Estimated install size">~{size}</span>}
                      <ChevronDown size={14} aria-hidden="true" className="group-open:rotate-180" />
                    </span>
                  </summary>
                  <dl className="space-y-1 border-t border-slate-800 px-4 py-3 text-xs text-slate-300">
                    <div><dt className="inline text-slate-400">Source · </dt><dd className="inline">{entry.source}</dd></div>
                    {apps ? (
                      <>
                        <div className="break-all"><dt className="inline text-slate-400">Install path · </dt><dd className="inline">{entry.installPath ?? 'Not reported'}</dd></div>
                        <div><dt className="inline text-slate-400">Version · </dt><dd className="inline">{entry.version ?? 'Not reported'}</dd></div>
                        <div><dt className="inline text-slate-400">Estimated size · </dt><dd className="inline">{size ?? 'Not reported'}</dd></div>
                      </>
                    ) : (
                      <>
                        {entry.inf && <div><dt className="inline text-slate-400">INF · </dt><dd className="inline">{entry.inf}</dd></div>}
                        {entry.status && <div><dt className="inline text-slate-400">Status · </dt><dd className="inline">{entry.status}</dd></div>}
                        {entry.details && <div><dt className="sr-only">Driver information</dt><dd className="whitespace-pre-wrap break-all">{entry.details}</dd></div>}
                      </>
                    )}
                  </dl>
                </details>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
});
