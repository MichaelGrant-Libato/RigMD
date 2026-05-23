import { useCallback, useEffect, useState, type ReactNode } from 'react';
import axios from 'axios';
import {
  AlertTriangle,
  FileText,
  Info,
  Search,
  ShieldAlert,
} from 'lucide-react';

import TopHeader from '../components/TopHeader';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8000';

interface WarningSummary {
  observed_warning_signs: number;
  total_observed_occurrences: number;
  total_reference_items: number;
}

interface WarningSignRow {
  id: string;
  warning_sign: string;
  meaning: string;
  threshold: string;
  action: string;
  category: string;
  keywords: string[];
  observed: boolean;
  observed_count: number;
}

interface WarningSignsResponse {
  summary: WarningSummary;
  categories: string[];
  warning_signs: WarningSignRow[];
  database_warning?: string;
}

const emptyWarningSigns: WarningSignsResponse = {
  summary: {
    observed_warning_signs: 0,
    total_observed_occurrences: 0,
    total_reference_items: 0,
  },
  categories: [],
  warning_signs: [],
};

function getActionStyle(action: string) {
  const value = action.toLowerCase();

  if (value.includes('escalate')) {
    return 'border-red-500/70 bg-red-500/10 text-red-400';
  }

  if (value.includes('troubleshoot')) {
    return 'border-orange-500/70 bg-orange-500/10 text-orange-400';
  }

  if (value.includes('maintain')) {
    return 'border-emerald-500/60 bg-emerald-500/10 text-emerald-400';
  }

  if (value.includes('monitor')) {
    return 'border-blue-500/60 bg-blue-500/10 text-blue-400';
  }

  return 'border-cyan-500/60 bg-cyan-500/10 text-cyan-400';
}

function getCategoryStyle(category: string) {
  const value = category.toLowerCase();

  if (value.includes('crash') || value.includes('storage') || value.includes('boot')) {
    return 'border-red-500/20 bg-red-500/10 text-red-300';
  }

  if (value.includes('drivers') || value.includes('performance') || value.includes('thermal')) {
    return 'border-orange-500/20 bg-orange-500/10 text-orange-300';
  }

  return 'border-slate-500/20 bg-slate-500/10 text-slate-400';
}

function AlertCard({
  icon,
  title,
  description,
  tone,
}: {
  icon: ReactNode;
  title: string;
  description: string;
  tone: 'danger' | 'info' | 'warning';
}) {
  const toneClass =
    tone === 'danger'
      ? 'border-red-500/30 bg-red-500/5 text-red-400'
      : tone === 'warning'
        ? 'border-orange-500/30 bg-orange-500/5 text-orange-300'
        : 'border-cyan-500/20 bg-cyan-500/5 text-slate-400';

  return (
    <section className={`rounded-2xl border px-5 py-4 ${toneClass}`}>
      <div className="flex items-start gap-3">
        <div className="mt-0.5 shrink-0">{icon}</div>

        <div>
          <h3 className={`font-bold ${tone === 'info' ? 'text-slate-300' : ''}`}>
            {title}
          </h3>
          <p className="mt-1 text-sm leading-relaxed text-slate-400">
            {description}
          </p>
        </div>
      </div>
    </section>
  );
}

function CategoryButton({
  label,
  active,
  onClick,
}: {
  label: string;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`shrink-0 rounded-full border px-3.5 py-2 text-[11px] font-bold uppercase transition ${
        active
          ? 'border-cyan-500/60 bg-cyan-500/10 text-cyan-400'
          : 'border-[#30363d] bg-[#0d1117] text-slate-500 hover:border-cyan-500/40 hover:text-cyan-400'
      }`}
    >
      {label}
    </button>
  );
}

function ObservedToggle({
  checked,
  onChange,
}: {
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <button
      type="button"
      onClick={() => onChange(!checked)}
      className={`flex shrink-0 items-center gap-3 rounded-xl border px-4 py-2 text-sm font-semibold transition ${
        checked
          ? 'border-cyan-500/50 bg-cyan-500/10 text-cyan-400'
          : 'border-[#30363d] bg-[#0d1117] text-slate-400 hover:border-cyan-500/40 hover:text-cyan-400'
      }`}
    >
      <span
        className={`flex h-5 w-9 items-center rounded-full border p-0.5 transition ${
          checked
            ? 'border-cyan-500/60 bg-cyan-500/20'
            : 'border-slate-600 bg-[#111827]'
        }`}
      >
        <span
          className={`h-3.5 w-3.5 rounded-full transition ${
            checked ? 'translate-x-4 bg-cyan-400' : 'translate-x-0 bg-slate-500'
          }`}
        />
      </span>

      Observed Only
    </button>
  );
}

function ActionBadge({ action }: { action: string }) {
  return (
    <span
      className={`inline-flex items-center justify-center rounded border px-3 py-1 text-xs font-bold uppercase ${getActionStyle(
        action
      )}`}
    >
      {action}
    </span>
  );
}

function CategoryBadge({ category }: { category: string }) {
  return (
    <span
      className={`inline-flex items-center justify-center rounded-full border px-3 py-1 text-xs font-semibold ${getCategoryStyle(
        category
      )}`}
    >
      {category}
    </span>
  );
}

export default function WarningSignsView() {
  const [data, setData] = useState<WarningSignsResponse>(emptyWarningSigns);
  const [activeCategory, setActiveCategory] = useState('all');
  const [search, setSearch] = useState('');
  const [observedOnly, setObservedOnly] = useState(false);
  const [isLoading, setIsLoading] = useState(false);

  const fetchWarningSigns = useCallback(async () => {
    setIsLoading(true);

    try {
      const response = await axios.get<WarningSignsResponse>(
        `${API_BASE_URL}/api/warning-signs/reference`,
        {
          params: {
            category: activeCategory,
            search,
            observed_only: observedOnly,
          },
        }
      );

      setData(response.data);
    } catch {
      setData(emptyWarningSigns);
    } finally {
      setIsLoading(false);
    }
  }, [activeCategory, search, observedOnly]);

  useEffect(() => {
    fetchWarningSigns();
  }, [fetchWarningSigns]);

  const categories = ['all', ...data.categories];

  return (
    <>
      <TopHeader
        title="Warning Signs"
        subtitle="Reference guide for observable PC warning indicators and recommended actions"
      />

      <div className="custom-scrollbar flex-1 overflow-y-auto px-6 py-6 lg:px-8">
        <div className="mx-auto w-full max-w-[1360px] space-y-5">
          {data.summary.observed_warning_signs > 0 ? (
            <AlertCard
              icon={<ShieldAlert size={18} className="text-red-400" />}
              tone="danger"
              title={`${data.summary.observed_warning_signs} warning signs observed in your recent sessions`}
              description="These are highlighted below. Review the recommended actions for each observed warning sign."
            />
          ) : (
            <AlertCard
              icon={<ShieldAlert size={18} className="text-cyan-400" />}
              tone="info"
              title="No warning signs observed in saved sessions yet"
              description="This reference guide is ready. Observed warning signs will be highlighted after New Diagnosis saves session results."
            />
          )}

          <AlertCard
            icon={<Info size={18} className="text-cyan-400" />}
            tone="info"
            title="Warning signs are advisory indicators"
            description="Warning signs are observable indicators that suggest a possible hardware or software issue. They are not final diagnoses. They guide the recommended action category."
          />

          {data.database_warning && (
            <AlertCard
              icon={<AlertTriangle size={18} className="text-orange-400" />}
              tone="warning"
              title="Database warning"
              description="The reference guide is still available, but observed-session data could not be loaded. Check your Supabase connection and tables."
            />
          )}

          <section className="rounded-2xl border border-[#30363d] bg-[#161b22] p-4">
            <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
              <div className="flex min-w-0 flex-1 flex-wrap items-center gap-2">
                {categories.map((category) => (
                  <CategoryButton
                    key={category}
                    label={category === 'all' ? 'All' : category}
                    active={activeCategory === category}
                    onClick={() => setActiveCategory(category)}
                  />
                ))}
              </div>

              <div className="flex w-full items-center gap-2 rounded-xl border border-[#30363d] bg-[#1f2937] px-4 py-2.5 xl:w-[260px]">
                <Search size={16} className="shrink-0 text-slate-500" />
                <input
                  value={search}
                  onChange={(event) => setSearch(event.target.value)}
                  placeholder="Search warning signs..."
                  className="w-full bg-transparent text-sm text-white outline-none placeholder:text-slate-500"
                />
              </div>
            </div>
          </section>

          <section className="overflow-hidden rounded-2xl border border-[#30363d] bg-[#161b22]">
            <div className="flex items-center justify-between gap-4 border-b border-[#30363d] px-5 py-4">
              <div className="flex items-center gap-2">
                <AlertTriangle size={15} className="text-slate-400" />
                <h3 className="font-bold uppercase tracking-wider text-white">
                  Warning Signs Reference
                </h3>
                <span className="rounded-full bg-[#1f2937] px-2 py-0.5 text-xs font-bold text-slate-500">
                  {data.warning_signs.length}
                </span>
              </div>

              <div className="flex items-center gap-3">
                <ObservedToggle checked={observedOnly} onChange={setObservedOnly} />

                {isLoading && (
                  <span className="text-xs font-semibold uppercase text-cyan-400">
                    Loading...
                  </span>
                )}
              </div>
            </div>

            <div className="grid grid-cols-[minmax(240px,1fr)_minmax(300px,1.4fr)_minmax(220px,1fr)_170px_170px] border-b border-[#30363d] bg-[#111827] px-5 py-3 text-[11px] font-bold uppercase tracking-[0.18em] text-slate-500">
              <div>Warning Sign</div>
              <div>Meaning</div>
              <div>Threshold</div>
              <div className="text-center">Action</div>
              <div className="text-center">Category</div>
            </div>

            {data.warning_signs.length === 0 ? (
              <div className="flex min-h-[280px] flex-col items-center justify-center px-6 py-12 text-center">
                <FileText size={42} className="mb-4 text-slate-600" />
                <h3 className="text-lg font-bold text-white">No warning signs found</h3>
                <p className="mt-2 max-w-md text-sm text-slate-500">
                  Try changing the category filter, clearing the search field, or disabling observed-only mode.
                </p>
              </div>
            ) : (
              data.warning_signs.map((row) => (
                <div
                  key={row.id}
                  className={`grid grid-cols-[minmax(240px,1fr)_minmax(300px,1.4fr)_minmax(220px,1fr)_170px_170px] items-center border-b border-[#30363d] px-5 py-4 last:border-b-0 ${
                    row.observed ? 'bg-red-500/[0.03]' : 'bg-[#161b22]'
                  }`}
                >
                  <div className="flex min-w-0 items-start gap-2">
                    {row.observed && (
                      <AlertTriangle size={15} className="mt-0.5 shrink-0 text-red-400" />
                    )}

                    <div className="min-w-0">
                      <p className="truncate text-sm font-bold text-white">
                        {row.warning_sign}
                      </p>

                      {row.observed && (
                        <p className="mt-1 text-xs font-semibold text-red-400">
                          Observed in recent session
                          {row.observed_count > 1 ? ` · ${row.observed_count} times` : ''}
                        </p>
                      )}
                    </div>
                  </div>

                  <div className="text-sm leading-relaxed text-slate-400">
                    {row.meaning}
                  </div>

                  <div className="text-sm text-slate-400">{row.threshold}</div>

                  <div className="flex justify-center">
                    <ActionBadge action={row.action} />
                  </div>

                  <div className="flex justify-center">
                    <CategoryBadge category={row.category} />
                  </div>
                </div>
              ))
            )}
          </section>
        </div>
      </div>
    </>
  );
}