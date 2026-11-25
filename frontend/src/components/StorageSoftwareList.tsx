import { memo, useEffect, useState } from 'react';
import { ChevronDown, ExternalLink, RefreshCw } from 'lucide-react';
import { apiGet, apiPost } from '../lib/api';

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
  inf?: string | null;
  status?: string | null;
  details?: string | null;
}

interface Inventory {
  entries: InventoryEntry[];
  partial: boolean;
}

type LocationFilter = 'drive' | 'all' | 'unknown';

const buttonClass = 'inline-flex items-center justify-center gap-2 rounded-lg border border-slate-700 px-3 py-2 text-sm text-slate-200 hover:bg-slate-800 disabled:opacity-50';
const fieldClass = 'rounded-lg border border-slate-700 bg-[#101821] px-3 py-2 text-sm text-slate-200 focus-visible:outline focus-visible:outline-cyan-400';

function estimatedSize(bytes: number | null | undefined) {
  if (typeof bytes !== 'number' || !Number.isFinite(bytes) || bytes <= 0) return null;
  const units = [[1024 ** 3, 'GB'], [1024 ** 2, 'MB'], [1024, 'KB']] as const;
  const [divisor, unit] = units.find(([threshold]) => bytes >= threshold) ?? units[2];
  return `~${(bytes / divisor).toLocaleString(undefined, { maximumFractionDigits: 1 })} ${unit}`;
}

export default memo(function StorageSoftwareList({
  kind,
  diskIndex,
  driveName,
}: {
  kind: 'apps' | 'drivers';
  diskIndex?: number | null;
  driveName?: string;
}) {
  const [inventory, setInventory] = useState<Inventory | null>(null);
  const [error, setError] = useState(false);
  const [revision, setRevision] = useState(0);
  const [search, setSearch] = useState('');
  const [location, setLocation] = useState<LocationFilter>('drive');
  const [opening, setOpening] = useState(false);
  const [launchError, setLaunchError] = useState(false);
  const apps = kind === 'apps';
  const loading = inventory === null && !error;

  useEffect(() => {
    const controller = new AbortController();
    apiGet<Inventory>('/api/software-inventory/' + kind, { signal: controller.signal })
      .then(({ data }) => {
        if (!controller.signal.aborted) setInventory(data);
      })
      .catch(() => {
        if (!controller.signal.aborted) setError(true);
      });
    return () => controller.abort();
  }, [kind, revision]);

  function refresh() {
    setError(false);
    setInventory(null);
    setRevision(value => value + 1);
  }

  async function openWindows() {
    setOpening(true);
    setLaunchError(false);
    try {
      const { data } = await apiPost<{ success: boolean }>('/api/remediation/open-target', {
        target: apps ? 'installed apps' : 'device manager',
      });
      if (!data.success) setLaunchError(true);
    } catch {
      setLaunchError(true);
    } finally {
      setOpening(false);
    }
  }

  const visibleLocation = diskIndex == null && location === 'drive' ? 'all' : location;
  const query = search.trim().toLocaleLowerCase();
  const rows = (inventory?.entries ?? []).filter(entry => {
    if (apps && visibleLocation === 'drive' && entry.diskIndex !== diskIndex) return false;
    if (apps && visibleLocation === 'unknown' && entry.diskIndex != null) return false;
    return !query || [entry.name, entry.publisher, entry.provider, entry.installPath, entry.inf, entry.details]
      .some(value => value?.toLocaleLowerCase().includes(query));
  });
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
        {apps && (
          <select
            aria-label="App location"
            value={visibleLocation}
            onChange={event => setLocation(event.target.value as LocationFilter)}
            className={fieldClass + ' max-w-full'}
          >
            {diskIndex != null && <option value="drive">{driveName ?? 'Selected drive'}</option>}
            <option value="all">All locations</option>
            <option value="unknown">Location unknown</option>
          </select>
        )}
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
      <p role="status" className="text-xs text-slate-400">
        {loading ? 'Reading Windows inventory…'
          : error ? 'The inventory could not be loaded. Try refreshing.'
          : rows.length === 0 ? (apps && visibleLocation === 'drive' && !query ? 'No apps with a known location on this drive.' : 'No matching entries.')
          : rows.length + (apps ? ' apps' : ' driver records')}
      </p>
      {rows.length > 0 && (
        <ul className="divide-y divide-slate-800 overflow-hidden rounded-lg border border-slate-800 bg-[#101821]">
          {rows.map(entry => {
            const company = entry.publisher ?? entry.provider;
            const size = estimatedSize(entry.estimatedSizeBytes);
            const summary = apps ? [company, entry.version, entry.volume ?? 'Location unknown'] : [entry.source, company, entry.version];
            const trailing = apps ? size : entry.status ?? entry.inf;
            return (
              <li key={entry.id}>
                <details className="group">
                  <summary className="flex cursor-pointer list-none items-center justify-between gap-3 px-4 py-3 hover:bg-slate-800/50 focus-visible:outline focus-visible:outline-cyan-400 [&::-webkit-details-marker]:hidden">
                    <span className="min-w-0">
                      <span className="block break-words text-sm font-medium text-white">{entry.name}</span>
                      <span className="mt-1 block break-words text-xs text-slate-400">{summary.filter(Boolean).join(' · ')}</span>
                    </span>
                    <span className="flex shrink-0 items-center gap-2 text-xs text-slate-400">
                      {trailing}
                      <ChevronDown size={14} aria-hidden="true" className="group-open:rotate-180" />
                    </span>
                  </summary>
                  <dl className="space-y-1 border-t border-slate-800 px-4 py-3 text-xs text-slate-300">
                    <div><dt className="inline text-slate-400">Source · </dt><dd className="inline">{entry.source}</dd></div>
                    {apps ? (
                      <>
                        <div className="break-all"><dt className="inline text-slate-400">Install path · </dt><dd className="inline">{entry.installPath ?? 'Not reported'}</dd></div>
                        <div><dt className="inline text-slate-400">Estimated size · </dt><dd className="inline">{size?.slice(1) ?? 'Not reported'}</dd></div>
                      </>
                    ) : (
                      <>
                        {entry.inf && <div><dt className="inline text-slate-400">INF · </dt><dd className="inline">{entry.inf}</dd></div>}
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
