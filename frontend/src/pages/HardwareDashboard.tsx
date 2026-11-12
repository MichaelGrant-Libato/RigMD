import CloudAiAccountPanel from '../components/CloudAiAccountPanel';
import ActionRecoveryNotice from '../components/ActionRecoveryNotice';
//HardwareDashboard.tsx

import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import { Wrench } from 'lucide-react';
import HomeDashboardContent from '../components/HomeDashboardContent';
import DeviceDataSkeleton from '../components/DeviceDataSkeleton';
import PageLoadBoundary from '../components/PageLoadBoundary';

import AppSidebar from '../components/AppSidebar';
import TopHeader, { HeaderStatusProvider, type LiveDataStatus } from '../components/TopHeader';
const SystemProfileView = lazy(() => import('./SystemProfileView'));
const DiagnosticHistoryView = lazy(() => import('./DiagnosticHistoryView'));
import { DEFAULT_TREND_SELECTION, type TrendSelection } from '../lib/diagnosticTrends';
const RecurringPatternsView = lazy(() => import('./RecurringPatternsView'));
const WarningSignsView = lazy(() => import('./WarningSignsView'));
const NewDiagnosisView = lazy(() => import('./NewDiagnosisView'));
const HelpScopeView = lazy(() => import('./HelpScopeView'));
const DiagnosticSessionDetailView = lazy(() => import('./DiagnosticSessionDetailView'));
const ShareReportView = lazy(() => import('./ShareReportView'));
const SettingsView = lazy(() => import('./SettingsView'));
const AppSpeedView = lazy(() => import('./AppSpeedView'));
import { useHomeOverview } from '../lib/useHomeOverview';
import { fullInventoryNeeded, sensorStreamingNeeded, type HomeOverview } from '../lib/homeOverview';

import {
  cardTransition,
  pageFade,
} from '../lib/motion';
import { API_BASE_URL, apiGet } from '../lib/api';
import * as signalR from '@microsoft/signalr';
import { getLocalApiToken } from '../lib/localApiSession';
import { mergeLiveUsage, type LiveUsageSample } from '../lib/liveReadings';
import type {
  AgentStatus,
  DashboardSessionSummary,
  DashboardSummary,
  HardwareStats,
  PageKey,
  SessionSummary,
} from '../types/rigmd';

interface LegacyDashboardSummary {
  total_sessions?: number;
  resolved_sessions?: number;
  needs_recheck_sessions?: number;
  open_sessions?: number;
  recent_sessions?: Array<{
    session_id: string;
    diagnosed_category?: string;
    confidence_label?: string;
    resolution_status?: string;
    created_at?: string;
  }>;
}

const emptyDashboard: DashboardSummary = {
  server_time: new Date().toISOString(),
  totals: {
    total_sessions: 0,
    this_month_count: 0,
    escalated_count: 0,
  },
  last_diagnosis: null,
  current_action_status: null,
  recurring_issues_count: 0,
  warning_signs_active_count: 0,
  action_distribution: [
    { label: 'Monitor', count: 0 },
    { label: 'Maintain', count: 0 },
    { label: 'Troubleshoot', count: 0 },
    { label: 'Escalate', count: 0 },
  ],
  session_frequency: [],
  recent_warning_signs: [],
  last_saved_session: null,
};

function formatTodayLabel() {
  return new Intl.DateTimeFormat('en-US', {
    month: 'long',
    day: '2-digit',
    year: 'numeric',
  }).format(new Date());
}

function normalizeAction(action: string | undefined | null) {
  const value = (action ?? '').toLowerCase();

  if (value.includes('monitor')) return 'Monitor';
  if (value.includes('maintain')) return 'Maintain';
  if (value.includes('troubleshoot')) return 'Troubleshoot';
  if (value.includes('escalate')) return 'Escalate';

  return '';
}

function hasDashboardContract(value: unknown): value is DashboardSummary {
  return Boolean(value && typeof value === 'object' && 'totals' in value);
}

function extractSessions(value: unknown): SessionSummary[] {
  if (Array.isArray(value)) return value as SessionSummary[];

  if (value && typeof value === 'object') {
    const response = value as { sessions?: unknown; value?: unknown };

    if (Array.isArray(response.sessions)) return response.sessions as SessionSummary[];
    if (Array.isArray(response.value)) return response.value as SessionSummary[];
  }

  return [];
}

function parseDate(value: string | null | undefined) {
  if (!value) return null;

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

function formatDashboardDate(value: string | null | undefined) {
  const date = parseDate(value);
  return date ? date.toLocaleDateString(undefined, { month: 'short', day: '2-digit', year: 'numeric' }) : null;
}

function getDaysAgo(value: string | null | undefined) {
  const date = parseDate(value);
  if (!date) return null;

  const today = new Date();
  today.setHours(0, 0, 0, 0);

  const sessionDate = new Date(date);
  sessionDate.setHours(0, 0, 0, 0);

  return Math.max(Math.floor((today.getTime() - sessionDate.getTime()) / 86400000), 0);
}

function sessionToDashboardSummary(session: SessionSummary | null | undefined): DashboardSessionSummary | null {
  if (!session) return null;

  return {
    session_id: session.session_id,
    symptom_type: session.symptom_type || 'Saved diagnosis',
    diagnosed_category: session.diagnosed_category || 'Unknown',
    action_category: normalizeAction(session.action_category) || 'Monitor',
    confidence_label: session.confidence_label || 'Not available',
    created_at: session.created_at,
    display_date: session.display_date || formatDashboardDate(session.created_at),
    days_ago: session.days_ago ?? getDaysAgo(session.created_at),
    is_recurring: Boolean(session.is_recurring),
    resolution_status: session.resolution_status,
    resolution_checked_at: session.resolution_checked_at,
    resolution_summary: session.resolution_summary,
  };
}

  type LegacyRecentSession = NonNullable<
    LegacyDashboardSummary['recent_sessions']
  >[number];

  function legacySessionToDashboardSummary(
    session: LegacyRecentSession
  ): DashboardSessionSummary {
    return {
      session_id: session.session_id,
      symptom_type: 'Saved diagnosis',
      diagnosed_category: session.diagnosed_category || 'Unknown',
      action_category: 'Monitor',
      confidence_label: session.confidence_label || 'Not available',
      created_at: session.created_at ?? null,
      display_date: formatDashboardDate(session.created_at),
      days_ago: getDaysAgo(session.created_at),
      is_recurring: false,
      resolution_status: session.resolution_status,
    };
  }

function normalizeDashboardSummary(rawSummary: unknown, sessions: SessionSummary[] = []): DashboardSummary {
  if (hasDashboardContract(rawSummary)) {
    return {
      ...emptyDashboard,
      ...rawSummary,
      totals: {
        ...emptyDashboard.totals,
        ...(rawSummary.totals || {}),
      },
      action_distribution: rawSummary.action_distribution?.length
        ? rawSummary.action_distribution
        : emptyDashboard.action_distribution,
      session_frequency: rawSummary.session_frequency || [],
      recent_warning_signs: rawSummary.recent_warning_signs || [],
    };
  }

  const legacy = (rawSummary || {}) as LegacyDashboardSummary;
  const sortedSessions = [...sessions].sort(
    (a, b) => (parseDate(b.created_at)?.getTime() ?? 0) - (parseDate(a.created_at)?.getTime() ?? 0)
  );
  const latestSession = sessionToDashboardSummary(sortedSessions[0]);
  const legacyLatestSession = legacy.recent_sessions?.[0]
    ? legacySessionToDashboardSummary(legacy.recent_sessions[0])
    : null;
  const sourceSessions = sortedSessions.length > 0
    ? sortedSessions
    : (legacy.recent_sessions || []).map((session) => ({
        session_id: session.session_id,
        symptom_type: 'Saved diagnosis',
        diagnosed_category: session.diagnosed_category || 'Unknown',
        action_category: 'Monitor',
        confidence_label: session.confidence_label || 'Not available',
        created_at: session.created_at ?? null,
        display_date: formatDashboardDate(session.created_at),
        days_ago: getDaysAgo(session.created_at),
        is_recurring: false,
      }));

  const actionCounts = emptyDashboard.action_distribution.map((item) => ({
    ...item,
    count: sourceSessions.filter((session) => normalizeAction(session.action_category) === item.label).length,
  }));

  const now = new Date();
  const thisMonthCount = sourceSessions.filter((session) => {
    const date = parseDate(session.created_at);
    return date && date.getMonth() === now.getMonth() && date.getFullYear() === now.getFullYear();
  }).length;
  const thirtyDaysAgo = new Date(now.getTime() - 30 * 86400000);
  const frequencyCounts = new Map<string, number>();

  sourceSessions.forEach((session) => {
    const date = parseDate(session.created_at);
    if (!date || date < thirtyDaysAgo) return;

    const key = date.toISOString().slice(0, 10);
    frequencyCounts.set(key, (frequencyCounts.get(key) || 0) + 1);
  });

  return {
    ...emptyDashboard,
    server_time: new Date().toISOString(),
    totals: {
      total_sessions: legacy.total_sessions ?? sourceSessions.length,
      this_month_count: thisMonthCount,
      escalated_count: sourceSessions.filter((session) => normalizeAction(session.action_category) === 'Escalate').length,
    },
    last_diagnosis: latestSession ?? legacyLatestSession,
    current_action_status: latestSession ?? legacyLatestSession,
    recurring_issues_count: new Set(
      sourceSessions
        .map((session) => session.symptom_type)
        .filter((symptom) => sourceSessions.filter((session) => session.symptom_type === symptom).length >= 2)
    ).size,
    warning_signs_active_count: sourceSessions.filter((session) => {
      const warning = 'warning_signs' in session ? String(session.warning_signs || '').toLowerCase() : '';
      return warning.length > 0 && warning !== 'none';
    }).length,
    action_distribution: actionCounts,
    session_frequency: Array.from(frequencyCounts.entries()).map(([date, count]) => ({ date, count })),
    recent_warning_signs: [],
    last_saved_session: latestSession ?? legacyLatestSession,
  };
}

function HomeDashboardView({
  stats,
  overview,
  overviewError,
  summaryState,
  dashboard,
  setActivePage,
  onViewSession,
}: {
  stats: HardwareStats | null;
  overview: HomeOverview | null;
  overviewError: string | null;
  summaryState: 'pending' | 'completed' | 'failed';
  dashboard: DashboardSummary;
  setActivePage: (page: PageKey) => void;
  onViewSession: (sessionId: string) => void;
}) {
  return (
    <>
      <TopHeader title="Home Dashboard" subtitle={`System overview and diagnostic status - ${formatTodayLabel()}`} />
      <CloudAiAccountPanel compact />
      <motion.div
        key="home-dashboard"
        variants={pageFade}
        initial="hidden"
        animate="visible"
        transition={cardTransition}
        className="custom-scrollbar min-h-0 flex-1 overflow-y-auto overscroll-contain px-4 py-5 sm:px-6 lg:px-8"
      >
        <HomeDashboardContent
          stats={stats}
          overview={overview}
          overviewError={overviewError}
          summaryState={summaryState}
          dashboard={dashboard}
          setActivePage={setActivePage}
          onViewSession={onViewSession}
        />
      </motion.div>
    </>
  );
}

function PlaceholderView({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <>
      <TopHeader title={title} subtitle={subtitle} />

      <div className="custom-scrollbar flex-1 overflow-y-auto p-8">
        <section className="rigmd-glass rounded-2xl border p-8">
          <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-lg border border-cyan-400/25 bg-cyan-400/[0.06] text-cyan-300">
            <Wrench size={24} />
          </div>

          <h3 className="text-xl font-bold text-white">{title}</h3>

          <p className="mt-2 max-w-2xl text-sm leading-relaxed text-gray-400">
            This screen is ready for frontend implementation. The layout shell, sidebar, theme, and routing state are already prepared.
          </p>
        </section>
      </div>
    </>
  );
}

void PlaceholderView;

export default function HardwareDashboard({ onStartupReady, onSummaryReady }: { onStartupReady?: (complete: boolean) => void; onSummaryReady?: (complete: boolean) => void } = {}) {
  const [initialDashboard, setInitialDashboard] = useState<'pending' | 'completed' | 'failed'>('pending');
  const [stats, setStats] = useState<HardwareStats | null>(null);
  const [agentStatus, setAgentStatus] = useState<AgentStatus | null>(null);
  const [dashboard, setDashboard] = useState<DashboardSummary>(emptyDashboard);
  const [error, setError] = useState<string | null>(null);
  const [reportWithoutInventory, setReportWithoutInventory] = useState(false);
  const [activePage, setActivePage] = useState<PageKey>('home');
  const overview = useHomeOverview(!fullInventoryNeeded(activePage));
  const needsFullInventory = fullInventoryNeeded(activePage);
  const needsSensorStreaming = sensorStreamingNeeded(activePage);
  const startupReported = useRef(false);
  const summaryReported = useRef(false);
  const [restoreAppSpeedFocus, setRestoreAppSpeedFocus] = useState(false);
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);
  const [historyView, setHistoryView] = useState<'saved' | 'trends'>('saved');
  const [trendSelection, setTrendSelection] = useState<TrendSelection>(DEFAULT_TREND_SELECTION);
  const [newSessionId, setNewSessionId] = useState<string | null>(null);
  const [diagnosisPreset, setDiagnosisPreset] = useState<'memory' | null>(null);
  const [hardwareUpdatedAt, setHardwareUpdatedAt] = useState<Date | null>(null);
  const [isRefreshingHardware, setIsRefreshingHardware] = useState(false);
  const [isInventoryPageLoading, setIsInventoryPageLoading] = useState(false);
  const [mobileSidebarOpen, setMobileSidebarOpen] = useState(false);

  const visibleError = needsFullInventory ? error : overview.error;
  const hasVisibleReadings = needsFullInventory ? !!stats : !!overview.data;
  const visibleLoading = needsFullInventory ? isRefreshingHardware : overview.state === 'pending';
  const readingTimestamp = needsFullInventory ? hardwareUpdatedAt?.getTime() ?? null : overview.data ? Date.parse(overview.data.capturedAt) : null;
  const visibleUpdatedAt = readingTimestamp === null ? null : new Date(readingTimestamp);
  const liveStatus: LiveDataStatus = useMemo(() => {
    if (visibleLoading || (!hasVisibleReadings && !visibleError)) return 'syncing';
    if (visibleError || agentStatus?.isOnline === false) return 'offline';
    return readingTimestamp === null || Date.now() - readingTimestamp > 120000 ? 'stale' : 'live';
  }, [visibleLoading, hasVisibleReadings, visibleError, agentStatus, readingTimestamp]);

  useEffect(() => {
    if (startupReported.current) return;
    const failed = activePage !== 'home' || overview.state === 'failed';
    const complete = overview.state === 'completed';
    if (failed || complete) { startupReported.current = true; onStartupReady?.(!failed && complete); }
  }, [overview.state, activePage, onStartupReady]);
  useEffect(() => {
    if (summaryReported.current) return;
    const failed = activePage !== 'home' || initialDashboard === 'failed';
    if (failed || initialDashboard === 'completed') { summaryReported.current = true; onSummaryReady?.(!failed); }
  }, [initialDashboard, activePage, onSummaryReady]);

  const deviceName = overview.data?.deviceName?.trim() || stats?.device_name?.trim() || 'Detecting Device';
  const isFetchingHardwareRef = useRef(false);
  const isFetchingDashboardRef = useRef(false);
  const latestUsageRef = useRef<{ sample: LiveUsageSample; receivedAt: number } | null>(null);
  const usageUpdatedAtRef = useRef(0);

  useEffect(() => {
    if (!needsSensorStreaming) return;
    let disposed = false;
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE_URL}/hubs/telemetry`, { accessTokenFactory: getLocalApiToken })
      .withAutomaticReconnect().build();
    connection.on('ReceiveTelemetry', (sample: LiveUsageSample) => {
      if (disposed) return;
      usageUpdatedAtRef.current = Date.now();
      latestUsageRef.current = { sample, receivedAt: Date.now() };
      setStats(previous => previous ? mergeLiveUsage(previous, sample) : previous);
      setHardwareUpdatedAt(new Date());
    });
    void connection.start().then(() => { if (disposed) void connection.stop(); }).catch(() => {});
    const staleTimer = window.setInterval(() => {
      if (usageUpdatedAtRef.current && Date.now() - usageUpdatedAtRef.current > 5000) {
        latestUsageRef.current = null;
        setStats(previous => previous ? mergeLiveUsage(previous, {}) : previous);
      }
    }, 1000);
    return () => { disposed = true; window.clearInterval(staleTimer); void connection.stop(); };
  }, [needsSensorStreaming]);

  const fetchHardware = useCallback(async () => {
    if (isFetchingHardwareRef.current) {
      return;
    }

    isFetchingHardwareRef.current = true;
    setIsRefreshingHardware(true);
    setError(null);
    try {
      const liveResponse = await apiGet<HardwareStats>('/api/hardware/live', { timeout: 60000 });
      const fresh = latestUsageRef.current;
      setStats(fresh && Date.now() - fresh.receivedAt <= 5000 ? mergeLiveUsage(liveResponse.data, fresh.sample) : liveResponse.data);
      usageUpdatedAtRef.current = fresh?.receivedAt ?? Date.now();
      setHardwareUpdatedAt(new Date());
      setError(null);
    } catch {
      setAgentStatus(null);
      setError('Unable to retrieve Windows hardware telemetry.');
    } finally {
      isFetchingHardwareRef.current = false;
      setIsRefreshingHardware(false);
      setIsInventoryPageLoading(false);
    }
  }, []);

  const refreshHardware = useCallback(async () => {
    if (error || !stats) setIsInventoryPageLoading(true);
    await fetchHardware();
  }, [fetchHardware, error, stats]);

  useEffect(() => {
    if (!needsFullInventory) return;
    void fetchHardware();

    const interval = window.setInterval(() => {
      if (document.visibilityState === 'hidden') {
        return;
      }
      fetchHardware();
    }, 30000);

    return () => window.clearInterval(interval);
  }, [fetchHardware, needsFullInventory, activePage]);

  useEffect(() => {
    if (activePage !== 'home') {
      return;
    }

    const fetchDashboard = async () => {
      if (isFetchingDashboardRef.current) {
        return;
      }

      isFetchingDashboardRef.current = true;
      try {
        const response = await apiGet<DashboardSummary | LegacyDashboardSummary>('/api/dashboard/summary', { timeout: 10000 });
        let sessions: SessionSummary[] = [];

        if (!hasDashboardContract(response.data)) {
          try {
            const sessionsResponse = await apiGet<SessionSummary[] | { sessions?: SessionSummary[]; value?: SessionSummary[] }>(
              '/api/diagnosis/sessions'
            );
            sessions = extractSessions(sessionsResponse.data);
          } catch {
            sessions = [];
          }
        }

        setDashboard(normalizeDashboardSummary(response.data, sessions));
        setInitialDashboard('database_warning' in response.data && response.data.database_warning ? 'failed' : 'completed');
      } catch {
        setDashboard(previous => ({ ...previous, database_warning: 'Saved-check summary could not be refreshed.' }));
        setInitialDashboard('failed');
      } finally {
        isFetchingDashboardRef.current = false;
      }
    };

    fetchDashboard();

    const interval = window.setInterval(() => {
      if (document.visibilityState === 'hidden') {
        return;
      }
      fetchDashboard();
    }, 30000);

    return () => window.clearInterval(interval);
  }, [activePage]);

  const handleSetActivePage = (page: PageKey) => {
  if (page !== activePage) setReportWithoutInventory(false);
  if (fullInventoryNeeded(page) && page !== activePage) { setIsInventoryPageLoading(true); setError(null); }
  if (page === 'newDiagnosis') { setDiagnosisPreset(null); setNewSessionId(null); }
  if (page === 'diagnosticHistory') {
    setSelectedSessionId(null);
  }

  setActivePage(page);
};

  const openMemoryCheck = () => {
    setNewSessionId(null);
    setDiagnosisPreset('memory');
    setActivePage('newDiagnosis');
  };

  const renderPage = () => {
    if (needsFullInventory && !(activePage === 'reports' && reportWithoutInventory) && (error || !stats || isInventoryPageLoading)) {
      if (!error) return <DeviceDataSkeleton report={activePage === 'reports'} />;
      return <><TopHeader title={activePage === 'reports' ? 'Share Report' : 'My Device Info'} subtitle="Device details are unavailable" /><div className="p-8" role="alert">
        <h2 className="text-xl font-semibold">{error ? 'Device details could not be loaded' : 'Loading full device details…'}</h2>
        <p className="mt-2 text-slate-400">{error ?? 'Reading hardware inventory and sensors. Home essentials are available separately.'}</p>
        {error && <div className="mt-4 flex flex-wrap gap-3">
          <button className="rounded border px-4 py-2 text-cyan-300" onClick={() => { void refreshHardware(); }}>Retry device details</button>
          {activePage === 'reports' && <button className="rounded border px-4 py-2 text-cyan-300" onClick={() => setReportWithoutInventory(true)}>Use saved check results only</button>}
        </div>}
      </div></>;
    }
    switch (activePage) {
      case 'home':
        return (
          <HomeDashboardView
            stats={null}
            overview={overview.data}
            overviewError={overview.error}
            summaryState={initialDashboard}
            dashboard={dashboard}
            setActivePage={handleSetActivePage}
            onViewSession={(sessionId) => {
              setSelectedSessionId(sessionId);
              setActivePage('diagnosticHistory');
            }}
          />
        );

      case 'systemProfile':
        return (
          <SystemProfileView
            stats={stats}
            error={error}
            hardwareUpdatedAt={hardwareUpdatedAt}
            isRefreshingHardware={isRefreshingHardware}
            onRefreshHardware={refreshHardware}
          />
        );

    case 'diagnosticHistory':
      return selectedSessionId ? (
        <DiagnosticSessionDetailView
          sessionId={selectedSessionId}
          onOpenSession={setSelectedSessionId}
          onBack={() => setSelectedSessionId(null)}
          onOpenMemoryCheck={openMemoryCheck}
        />
      ) : (
        <DiagnosticHistoryView
          view={historyView}
          onViewChange={setHistoryView}
          trendSelection={trendSelection}
          onTrendSelectionChange={setTrendSelection}
          onViewSession={(sessionId) => setSelectedSessionId(sessionId)}
          selectedSessionId={selectedSessionId}
          onStartNewDiagnosis={() => handleSetActivePage('newDiagnosis')}
        />
      );

      case 'recurringPatterns':
        return <RecurringPatternsView setActivePage={setActivePage} onOpenMemoryCheck={openMemoryCheck} />;

      case 'warningSigns':
        return <WarningSignsView onOpenMemoryCheck={openMemoryCheck} />;
      case 'newDiagnosis':
        return newSessionId ? (
          <DiagnosticSessionDetailView
            sessionId={newSessionId}
            onOpenSession={setNewSessionId}
            onBack={() => setNewSessionId(null)}
            backLabel="Back to checks"
            onOpenMemoryCheck={openMemoryCheck}
          />
        ) : (
          <NewDiagnosisView
            key={diagnosisPreset ?? 'default'}
            initialComponentIds={diagnosisPreset === 'memory' ? ['memory'] : undefined}
            onOpenMemoryCheck={openMemoryCheck}
            onDiagnosisComplete={(sessionId) => {
              setNewSessionId(sessionId);
            }}
          />
        );

      case 'reports':
        return (<>
          {reportWithoutInventory && <p role="status" className="mx-6 mt-4 text-sm text-amber-200">Using saved check results only. Current device details are unavailable and will be omitted from the report.</p>}
          <ShareReportView
            stats={reportWithoutInventory ? null : stats}
            hardwareUpdatedAt={reportWithoutInventory ? null : hardwareUpdatedAt}
            onStartNewDiagnosis={() => handleSetActivePage('newDiagnosis')}
          />
        </>);

      case 'settings':
        return <SettingsView onOpenAppSpeed={() => { setRestoreAppSpeedFocus(false); handleSetActivePage('appSpeed'); }} restoreAppSpeedFocus={restoreAppSpeedFocus} />;
      case 'appSpeed':
        return <AppSpeedView onBack={() => { setRestoreAppSpeedFocus(true); handleSetActivePage('settings'); }} />;

      case 'help':
        return <HelpScopeView />;

      default:
        return (
          <HomeDashboardView
            stats={null}
            overview={overview.data}
            overviewError={overview.error}
            summaryState={initialDashboard}
            dashboard={dashboard}
            setActivePage={handleSetActivePage}
            onViewSession={(sessionId) => {
              setSelectedSessionId(sessionId);
              setActivePage('diagnosticHistory');
            }}
          />
        );
    }
  };

  return (
    <div className="rigmd-app-shell flex overflow-hidden bg-transparent font-sans text-gray-200">
      <AppSidebar
        activePage={activePage}
        setActivePage={handleSetActivePage}
        dashboard={dashboard}
        liveStatus={liveStatus}
        hardwareUpdatedAt={visibleUpdatedAt}
        mobileOpen={mobileSidebarOpen}
        onMobileClose={() => setMobileSidebarOpen(false)}
      />

      <main className="rigmd-main-surface flex h-full min-h-0 min-w-0 flex-1 flex-col overflow-hidden">
        <ActionRecoveryNotice />
        <HeaderStatusProvider
          deviceName={deviceName}
          liveStatus={liveStatus}
          onMenuClick={() => setMobileSidebarOpen(true)}
        >
          <AnimatePresence mode="wait">
            <div key={`${activePage}-${(activePage === 'newDiagnosis' ? newSessionId : selectedSessionId) ?? 'list'}`} className="flex min-h-0 flex-1 flex-col overflow-hidden">
              <PageLoadBoundary key={activePage} onHome={() => setActivePage('home')}>
                <Suspense fallback={activePage === 'systemProfile' || activePage === 'reports'
                  ? <DeviceDataSkeleton report={activePage === 'reports'} />
                  : <div role="status" aria-live="polite" className="p-8 text-slate-300">Opening this page…</div>}>
                  {renderPage()}
                </Suspense>
              </PageLoadBoundary>
            </div>
          </AnimatePresence>
        </HeaderStatusProvider>
      </main>
    </div>
  );
}
