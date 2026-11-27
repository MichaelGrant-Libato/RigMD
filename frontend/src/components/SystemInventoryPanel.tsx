import { memo, useEffect, useMemo, useState } from 'react';
import { ChevronDown, CircuitBoard, RefreshCw, Search, Settings } from 'lucide-react';
import { apiGet } from '../lib/api';
import { getApiErrorMessage } from '../lib/apiErrorMessage';

interface Entry {
  id: string;
  name: string;
  identifier: string;
  source: string;
  description?: string | null;
  state?: string | null;
  fields: { label: string; value: string }[];
}

interface Snapshot {
  entries: Entry[];
  unavailableSources: string[];
  limitedSources: string[];
}

function InventoryRow({ entry, drivers }: { entry: Entry; drivers: boolean }) {
  const Icon = drivers ? CircuitBoard : Settings;
  const details = [['Identifier', entry.identifier], ...entry.fields.map(field => [field.label, field.value])];
  const summary = drivers
    ? [entry.source, entry.fields.find(field => field.label === 'Provider')?.value, entry.fields.find(field => field.label === 'Version')?.value]
    : [entry.identifier === entry.name ? null : entry.identifier, entry.fields.find(field => field.label === 'Startup')?.value];
  return <li>
    <details className="group/inventory">
      <summary className="flex min-h-16 cursor-pointer list-none items-center gap-3 px-3 py-2.5 hover:bg-white/[.035] focus-visible:outline focus-visible:outline-inset focus-visible:outline-cyan-400 [&::-webkit-details-marker]:hidden">
        <span className="grid size-10 shrink-0 place-items-center rounded-lg bg-cyan-400/10 text-cyan-300">
          <Icon size={19} aria-hidden="true" />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block break-words text-sm font-semibold text-slate-100">{entry.name}</span>
          <span className="mt-0.5 block break-words text-xs text-slate-400">{summary.filter(Boolean).join(' · ')}</span>
        </span>
        {entry.state && <span className="rounded-full bg-slate-800 px-2 py-1 text-xs text-slate-200">{entry.state}</span>}
        <ChevronDown size={15} aria-hidden="true" className="shrink-0 text-slate-400 group-open/inventory:rotate-180" />
      </summary>
      <div className="space-y-3 border-t border-white/5 bg-black/10 px-4 py-3 sm:pl-16">
        {entry.description && entry.description !== entry.name && <p className="break-words text-sm text-slate-300">{entry.description}</p>}
        <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-xs sm:grid-cols-3">
          {details.map(([label, value]) => <div key={label} className={label === 'Identifier' ? 'col-span-full min-w-0' : 'min-w-0'}>
            <dt className="text-slate-400">{label}</dt>
            <dd className="mt-0.5 break-words text-slate-200">{value}</dd>
          </div>)}
        </dl>
      </div>
    </details>
  </li>;
}

export default memo(function SystemInventoryPanel({ kind }: { kind: 'drivers' | 'services' }) {
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [revision, setRevision] = useState(0);
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState('');
  const drivers = kind === 'drivers';
  const title = drivers ? 'Drivers' : 'Services';

  useEffect(() => {
    const controller = new AbortController();
    void apiGet<Snapshot>('/api/device-diagnostics/' + kind, { signal: controller.signal, timeout: 18000 })
      .then(({ data }) => {
        if (controller.signal.aborted) return;
        setSnapshot(data);
        setFilter(current => data.entries.some(entry => (drivers ? entry.source : entry.state) === current) ? current : '');
      })
      .catch(reason => {
        if (!controller.signal.aborted) setError(getApiErrorMessage(reason, 'Windows could not provide the list. Try refreshing.'));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [drivers, kind, revision]);

  const options = useMemo(() => [...new Set(snapshot?.entries.flatMap(entry => {
    const option = drivers ? entry.source : entry.state;
    return option ? [option] : [];
  }))].sort(), [drivers, snapshot]);

  const rows = useMemo(() => {
    const query = search.trim().toLowerCase();
    return snapshot?.entries.filter(entry => (!filter || filter === (drivers ? entry.source : entry.state)) &&
      (!query || [entry.name, entry.identifier, entry.description, ...entry.fields.map(field => field.value)]
        .some(value => value?.toLowerCase().includes(query)))) ?? [];
  }, [drivers, filter, search, snapshot]);

  return <section aria-label={title} className="space-y-3">
    <p className="text-sm text-slate-400">
      {drivers ? 'Browse device drivers, registered driver services, and packages in the Windows Driver Store.'
        : 'Browse registered Windows services and their current status and startup settings.'}
    </p>
    <div className="flex flex-wrap gap-2">
      <label className="relative min-w-40 flex-1">
        <span className="sr-only">{'Search ' + kind}</span>
        <Search aria-hidden="true" size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
        <input type="search" placeholder={'Search ' + kind} value={search} onChange={event => setSearch(event.target.value)}
          className="h-10 w-full rounded-lg border border-[var(--rigmd-border)] bg-[#101821] pl-9 pr-3 text-sm text-slate-100 placeholder:text-slate-400 focus-visible:outline-cyan-400" />
      </label>
      <select aria-label={drivers ? 'Filter by driver source' : 'Filter by service status'} value={filter} onChange={event => setFilter(event.target.value)}
        className="h-10 rounded-lg border border-[var(--rigmd-border)] bg-[#101821] px-3 text-sm text-slate-200 focus-visible:outline-cyan-400">
        <option value="">{drivers ? 'All sources' : 'All statuses'}</option>
        {options.map(option => <option key={option} value={option}>{option}</option>)}
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
      {loading ? 'Reading ' + kind + '…' : snapshot && (
        rows.length === snapshot.entries.length ? rows.length + ' entries' : rows.length + ' of ' + snapshot.entries.length + ' entries'
      )}
    </p>
    {error && <p role="alert" className="text-sm text-amber-200">{error}{snapshot && ' Showing the previous list.'}</p>}
    {!!snapshot?.unavailableSources.length && <p className="text-sm text-amber-200">Windows could not read {snapshot.unavailableSources.join(', ')}.</p>}
    {!!snapshot?.limitedSources.length && <p className="text-sm text-amber-200">The list is limited for {snapshot.limitedSources.join(', ')}.</p>}
    {rows.length > 0
      ? <ul className="divide-y divide-white/10 overflow-hidden rounded-lg border border-[var(--rigmd-border)] bg-[#101821]">
          {rows.map(entry => <InventoryRow key={entry.id} entry={entry} drivers={drivers} />)}
        </ul>
      : snapshot && <p className="rounded-lg border border-[var(--rigmd-border)] p-4 text-sm text-slate-300">
          {snapshot.entries.length > 0 ? 'No entries match these filters.'
            : snapshot.unavailableSources.length > 0 ? 'The inventory is unavailable.' : 'Windows returned no entries.'}
        </p>}
  </section>;
});
