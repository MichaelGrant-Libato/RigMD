import { motion } from 'motion/react';
import { AlertTriangle, History, Stethoscope } from 'lucide-react';
import type { DashboardSummary, HardwareStats, PageKey } from '../types/rigmd';
// HealthScoreRing is disabled for now — too risky to claim computer health with lacking parameters
// import HealthScoreRing from './dashboard/HealthScoreRing';
import StatusPill from './dashboard/StatusPill';
import { CpuCard, MemoryCard, StorageCard, NetworkCard } from './dashboard/MetricCards';
import RecentDiagnosticsTimeline from './dashboard/RecentDiagnosticsTimeline';
import AlertsSummaryCard from './dashboard/AlertsSummaryCard';
import { REVIEW_THRESHOLDS, networkReviewStatus } from '../lib/reviewThresholds';
import { storageReviewStatus } from '../lib/storageTelemetry';
import { getHomeDashboardMessage } from '../lib/homeDashboardMessage';

interface Props {
  stats: HardwareStats | null;
  dashboard: DashboardSummary;
  setActivePage: (page: PageKey) => void;
  onViewSession: (sessionId: string) => void;
}

/*
 * ─── Composite Health Score (Disabled for now) ───
 * Disabled per user request: too risky to claim a computer's overall health score
 * with incomplete hardware telemetry parameters during panel review.
 * Retained here for future reference when comprehensive telemetry is available.
 *
 * function computeHealthScore(stats: HardwareStats | null, dashboard: DashboardSummary): number {
 *   if (!stats) return 0;
 *   let score = 100;
 *   const diskPct = stats.disk?.usage_percent ?? 0;
 *   if (diskPct >= 95) score -= 25;
 *   else if (diskPct >= 90) score -= 18;
 *   else if (diskPct >= 80) score -= 10;
 *   else if (diskPct >= 75) score -= 5;
 *   const ramPct = stats.ram?.usage_percent ?? 0;
 *   if (ramPct >= 95) score -= 20;
 *   else if (ramPct >= 90) score -= 14;
 *   else if (ramPct >= 80) score -= 7;
 *   else if (ramPct >= 75) score -= 3;
 *   const failingDrives = (stats.storage_drives ?? []).filter(d => d.is_failing_smart === true).length;
 *   if (failingDrives > 0) score -= Math.min(failingDrives * 10, 20);
 *   const cpuTemp = stats.cpu?.temperature_celsius;
 *   if (typeof cpuTemp === 'number' && cpuTemp > 0) {
 *     if (cpuTemp >= 95) score -= 15;
 *     else if (cpuTemp >= 90) score -= 10;
 *     else if (cpuTemp >= 80) score -= 5;
 *   }
 *   const deviceErrorCount = stats.device_errors?.length ?? 0;
 *   if (deviceErrorCount > 0) score -= Math.min(deviceErrorCount * 3, 10);
 *   const packetLoss = stats.network?.packet_loss_percent;
 *   const latency = stats.network?.ping_latency_ms;
 *   if (typeof packetLoss === 'number' && packetLoss > 2) score -= 3;
 *   if (typeof latency === 'number' && latency > 200) score -= 2;
 *   const warnings = Math.min(dashboard.warning_signs_active_count, 3);
 *   score -= warnings * 5;
 *   return Math.max(0, Math.min(100, Math.round(score)));
 * }
 */

/* ─── Status pills from live data ─── */
function getStatusPills(stats: HardwareStats | null, dashboard: DashboardSummary) {
  const pills: Array<{ label: string; variant: 'ok' | 'warning' | 'critical' | 'neutral' }> = [];

  if (stats?.ram) {
    const pct = stats.ram.usage_percent;
    pills.push({
      label: 'Memory',
      variant: typeof pct !== 'number' || !Number.isFinite(pct) || pct < 0 || pct > 100 ? 'neutral' : pct >= REVIEW_THRESHOLDS.memory ? 'warning' : 'ok',
    });
  }
  if (stats?.disk) {
    const state = storageReviewStatus(stats);
    pills.push({
      label: 'Storage',
      variant: state === 'high' ? 'warning' : state === 'normal' ? 'ok' : 'neutral',
    });
  }
  const cpu = stats?.cpu.usage_percent;
  if (typeof cpu === 'number' && Number.isFinite(cpu) && cpu >= REVIEW_THRESHOLDS.cpu && cpu <= 100) pills.push({ label: 'Processor', variant: 'warning' });
  if (stats?.network) {
    const state = networkReviewStatus(stats.network);
    pills.push({ label: 'Network', variant: state === 'high' ? 'warning' : state === 'normal' ? 'ok' : 'neutral' });
  }
  if (dashboard.warning_signs_active_count > 0) {
    pills.push({ label: `${dashboard.warning_signs_active_count} Alert${dashboard.warning_signs_active_count !== 1 ? 's' : ''}`, variant: 'warning' });
  }

  return pills;
}

export default function HomeDashboardContent({ stats, dashboard, setActivePage, onViewSession }: Props) {
  const hasWarnings = dashboard.warning_signs_active_count > 0;
  const pills = getStatusPills(stats, dashboard);
  const currentWarnings = pills.filter(pill => pill.variant === 'warning' && !pill.label.includes('Alert')).map(pill => pill.label);
  const { title, subtitle, latest, primary } = getHomeDashboardMessage(dashboard, currentWarnings);

  return (
    <div className="mx-auto w-full max-w-[1500px] space-y-5">

      {/* ═══════ HERO — Device Checkup & Action ═══════ */}
      <motion.section
        initial={{ opacity: 0, y: 14 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.35 }}
        className={`rigmd-card-surface relative overflow-hidden rounded-xl border p-6 md:p-8
          ${hasWarnings || currentWarnings.length ? 'border-amber-400/30' : ''}`}
      >
        {/* Subtle gradient glow accent on left */}
        <div className="pointer-events-none absolute inset-y-0 left-0 w-1 rounded-l-xl bg-gradient-to-b from-cyan-400/60 via-cyan-400/20 to-transparent" />

        <div className="flex flex-col gap-6 md:flex-row md:items-center md:justify-between">
          {/* Text content */}
          <div className="min-w-0 flex-1">
            <p className="text-[11px] font-bold uppercase tracking-[0.18em] text-cyan-400">Device Checkup</p>
            <h2 className="mt-1 max-w-2xl text-xl font-bold leading-snug text-white md:text-2xl">{title}</h2>
            <p className="mt-2 max-w-xl text-sm leading-relaxed text-slate-300">{subtitle}</p>

            {/* Status pills */}
            {pills.length > 0 && (
              <div className="mt-4 flex flex-wrap gap-2">
                {pills.map((p) => (
                  <StatusPill key={p.label} label={p.label} variant={p.variant} />
                ))}
              </div>
            )}

            {/* CTA buttons */}
            <div className="mt-5 flex flex-wrap items-center gap-4">
              <button
                type="button"
                onClick={() =>
                  primary === 'alerts' ? setActivePage('warningSigns')
                    : primary === 'latest' && latest ? onViewSession(latest.session_id)
                      : setActivePage('newDiagnosis')
                }
                className="inline-flex items-center gap-2 rounded-lg bg-[#1fb6c9] px-5 py-2.5 text-sm font-bold text-[#041014] shadow-lg shadow-cyan-400/10 transition hover:bg-[#38c7d7] hover:shadow-cyan-400/20 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-cyan-300"
              >
                {primary === 'alerts' ? <AlertTriangle size={16} /> : primary === 'latest' ? <History size={16} /> : <Stethoscope size={16} />}
                {primary === 'alerts' ? 'Review Alerts' : primary === 'latest' ? 'View Latest Result' : primary === 'current' ? 'Check current readings' : 'Check My Device'}
              </button>
              {(latest || hasWarnings) && (
                <button
                  type="button"
                  onClick={() => setActivePage('newDiagnosis')}
                  className="inline-flex items-center gap-2 rounded text-sm font-semibold text-cyan-300 transition hover:text-cyan-200"
                >
                  <Stethoscope size={15} />
                  New Check
                </button>
              )}
            </div>
          </div>
        </div>
      </motion.section>

      {/* ═══════ METRIC CARDS ═══════ */}
      <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <CpuCard stats={stats} />
        <MemoryCard stats={stats} />
        <StorageCard stats={stats} />
        <NetworkCard stats={stats} />
      </section>

      {/* ═══════ BOTTOM ROW — Timeline + Alerts ═══════ */}
      <div className="grid gap-4 lg:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)]">
        <RecentDiagnosticsTimeline
          dashboard={dashboard}
          onViewSession={onViewSession}
          setActivePage={setActivePage}
        />
        <AlertsSummaryCard
          dashboard={dashboard}
          setActivePage={setActivePage}
        />
      </div>

    </div>
  );
}
