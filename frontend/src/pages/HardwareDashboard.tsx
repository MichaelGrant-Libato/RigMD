//HardwareDashboard.tsx

import { useCallback, useEffect, useMemo, useState } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import { Wrench } from 'lucide-react';
import HomeDashboardContent from '../components/HomeDashboardContent';

import AppSidebar from '../components/AppSidebar';
import TopHeader, { HeaderStatusProvider, type LiveDataStatus } from '../components/TopHeader';
import SystemProfileView from './SystemProfileView';
import DiagnosticHistoryView from './DiagnosticHistoryView';
import RecurringPatternsView from './RecurringPatternsView';
import WarningSignsView from './WarningSignsView';
import NewDiagnosisView from './NewDiagnosisView'; // Imported the separated module view
import HelpScopeView from './HelpScopeView';
import DiagnosticSessionDetailView from './DiagnosticSessionDetailView';

import {
  cardTransition,
  pageFade,
} from '../lib/motion';
import { apiGet } from '../lib/api';
import type {
  AgentSnapshotResponse,
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

const AGENT_ID = import.meta.env.VITE_AGENT_ID;

function agentSnapshotToHardwareStats(
  snapshot: AgentSnapshotResponse
): HardwareStats {
  const hardware = snapshot.hardware;
  const primaryDisk = hardware.allDisks?.[0];

  return {
    device_name: hardware.deviceName,
    os_version: hardware.osVersion,
    system_age: hardware.systemAge,
    chipset_driver: hardware.chipsetDriver,
    storage_type: hardware.primaryStorageType,

    cpu: {
      name: hardware.cpu.name,
      usage_percent: hardware.cpu.usagePercent,
      cores: hardware.cpu.cores,
      threads: hardware.cpu.threads,
      frequency_mhz: hardware.cpu.frequencyMhz,
    },

    gpu: {
      name: hardware.gpu.name,
      driver: hardware.gpu.driver,
      type: hardware.gpu.type,
      vram_gb: hardware.gpu.vramGb,
    },

    ram: {
      total_gb: hardware.ram.totalGb,
      used_gb: hardware.ram.usedGb,
      usage_percent: hardware.ram.usagePercent,
    },

    disk: {
      total_gb: primaryDisk?.totalGb ?? 0,
      used_gb: primaryDisk?.usedGb ?? 0,
      usage_percent: primaryDisk?.usagePercent ?? 0,
    },

    storage_drives: (hardware.storageDrives ?? []).map((drive) => ({
      model: drive.model,
      type: drive.type,
      size_gb: drive.sizeGb,
      interface: drive.interface,
      media_type: drive.mediaType,
      bus_type: drive.busType,
      detection_source: drive.detectionSource,
      disk_index: drive.diskIndex,
      used_gb: drive.usedGb,
      usage_percent: drive.usagePercent,
      volumes: [],
    })),

    process_insights: hardware.processInsights
      ? {
          browser_detected:
            hardware.processInsights.browserDetected,

          browser_process_count:
            hardware.processInsights.browserProcessCount,

          browser_memory_mb:
            hardware.processInsights.browserMemoryMb,

          browser_heavy:
            hardware.processInsights.browserHeavy,

          game_detected:
            hardware.processInsights.gameDetected,

          game_processes:
            hardware.processInsights.gameProcesses,

          top_memory_apps:
            hardware.processInsights.topMemoryApps.map(
              (app) => ({
                name: app.name,
                process_count: app.processCount,
                memory_mb: app.memoryMb,
              })
            ),
        }
      : undefined,
  };
}

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
  dashboard,
  setActivePage,
  onViewSession,
}: {
  stats: HardwareStats | null;
  dashboard: DashboardSummary;
  setActivePage: (page: PageKey) => void;
  onViewSession: (sessionId: string) => void;
}) {
  return (
    <>
      <TopHeader title="Home Dashboard" subtitle={`System overview and diagnostic status - ${formatTodayLabel()}`} />
      <motion.div
        key="home-dashboard"
        variants={pageFade}
        initial="hidden"
        animate="visible"
        transition={cardTransition}
        className="custom-scrollbar flex-1 overflow-y-auto px-4 py-5 sm:px-6 lg:px-8"
      >
        <HomeDashboardContent
          stats={stats}
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

export default function HardwareDashboard() {
  const [stats, setStats] = useState<HardwareStats | null>(null);
  const [agentStatus, setAgentStatus] = useState<AgentStatus | null>(null);
  const [dashboard, setDashboard] = useState<DashboardSummary>(emptyDashboard);
  const [error, setError] = useState<string | null>(null);
  const [activePage, setActivePage] = useState<PageKey>('home');
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);
  const [hardwareUpdatedAt, setHardwareUpdatedAt] = useState<Date | null>(null);
  const [isRefreshingHardware, setIsRefreshingHardware] = useState(false);
  const [mobileSidebarOpen, setMobileSidebarOpen] = useState(false);

  const liveStatus: LiveDataStatus = useMemo(() => {
    if (
      isRefreshingHardware ||
      (!stats && !error)
    ) {
      return 'syncing';
    }

    if (error || agentStatus?.isOnline === false) {
      return 'offline';
    }

    if (
      !hardwareUpdatedAt ||
      Date.now() -
        hardwareUpdatedAt.getTime() >
        120000
    ) {
      return 'stale';
    }

    return 'live';
  }, [
    agentStatus,
    error,
    hardwareUpdatedAt,
    isRefreshingHardware,
    stats,
  ]);

  const deviceName = stats?.device_name?.trim() || 'Detecting PC';

  const fetchHardware = useCallback(async () => {
    if (!AGENT_ID) {
      setError('VITE_AGENT_ID is not configured.');
      return;
    }

    try {
      const [statusResponse, snapshotResponse] =
        await Promise.all([
          apiGet<AgentStatus>(
            `/api/agent/${AGENT_ID}`,
            {
              headers: {
                'X-Client-ID': AGENT_ID,
              },
            }
          ),

          apiGet<AgentSnapshotResponse>(
            `/api/agent/${AGENT_ID}/snapshot`,
            {
              headers: {
                'X-Client-ID': AGENT_ID,
              },
            }
          ),
        ]);

      setAgentStatus(statusResponse.data);

      setStats(
        agentSnapshotToHardwareStats(
          snapshotResponse.data
        )
      );

      setHardwareUpdatedAt(
        new Date(snapshotResponse.data.capturedAt)
      );

      setError(null);
    } catch {
      setAgentStatus(null);

      setError(
        'Unable to retrieve Windows Agent telemetry.'
      );
    }
  }, []);

  const refreshHardware = useCallback(async () => {
    setIsRefreshingHardware(true);

    try {
      await fetchHardware();
    } finally {
      setIsRefreshingHardware(false);
    }
  }, [fetchHardware]);

  useEffect(() => {
    fetchHardware();

    const interval = window.setInterval(fetchHardware, 10000);

    return () => window.clearInterval(interval);
  }, [fetchHardware]);

  useEffect(() => {
    const fetchDashboard = async () => {
      try {
        const response = await apiGet<DashboardSummary | LegacyDashboardSummary>('/api/dashboard/summary');
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
      } catch {
        setDashboard(emptyDashboard);
      }
    };

    fetchDashboard();

    const interval = window.setInterval(fetchDashboard, 5000);

    return () => window.clearInterval(interval);
  }, []);

  const handleSetActivePage = (page: PageKey) => {
  if (page === 'diagnosticHistory') {
    setSelectedSessionId(null);
  }

  setActivePage(page);
};

  const renderPage = () => {
    switch (activePage) {
      case 'home':
        return (
          <HomeDashboardView
            stats={stats}
            dashboard={dashboard}
            setActivePage={setActivePage}
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
          onBack={() => setSelectedSessionId(null)}
        />
      ) : (
        <DiagnosticHistoryView
          onViewSession={(sessionId) => setSelectedSessionId(sessionId)}
          selectedSessionId={selectedSessionId}
          onStartNewDiagnosis={() => setActivePage('newDiagnosis')}
        />
      );

      case 'recurringPatterns':
        return <RecurringPatternsView setActivePage={setActivePage} />;

      case 'warningSigns':
        return <WarningSignsView />;

      case 'newDiagnosis':
        return <NewDiagnosisView />; // TARGET SWAP: Safely maps the new modular view file component

      case 'reports':
        return (
          <PlaceholderView
            title="Reports"
            subtitle="Technician-ready diagnostic report output"
          />
        );

      case 'settings':
        return <PlaceholderView title="Settings" subtitle="Application preferences and configuration" />;

      case 'help':
        return <HelpScopeView />;

      default:
        return (
          <HomeDashboardView
            stats={stats}
            dashboard={dashboard}
            setActivePage={setActivePage}
            onViewSession={(sessionId) => {
              setSelectedSessionId(sessionId);
              setActivePage('diagnosticHistory');
            }}
          />
        );
    }
  };

  return (
    <div className="flex h-screen overflow-hidden bg-transparent font-sans text-gray-200">
      <AppSidebar
        activePage={activePage}
        setActivePage={handleSetActivePage}
        dashboard={dashboard}
        liveStatus={liveStatus}
        hardwareUpdatedAt={hardwareUpdatedAt}
        mobileOpen={mobileSidebarOpen}
        onMobileClose={() => setMobileSidebarOpen(false)}
      />

      <main className="rigmd-main-surface flex h-full min-w-0 flex-1 flex-col overflow-hidden">
        <HeaderStatusProvider
          deviceName={deviceName}
          liveStatus={liveStatus}
          onMenuClick={() => setMobileSidebarOpen(true)}
        >
          <AnimatePresence mode="wait">
            <div key={`${activePage}-${selectedSessionId ?? 'list'}`} className="flex min-h-0 flex-1 flex-col overflow-hidden">
              {renderPage()}
            </div>
          </AnimatePresence>
        </HeaderStatusProvider>
      </main>
    </div>
  );
}
