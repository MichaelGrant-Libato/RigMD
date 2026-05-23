export interface HardwareStats {
  os_version: string;
  system_age: string;
  chipset_driver: string;
  storage_type: string;
  cpu: {
    name: string;
    usage_percent: number;
    cores: number;
    threads: number;
    frequency_mhz: number;
  };
  gpu: {
    name: string;
    driver: string;
    type: string;
    vram_gb: number;
  };
  ram: {
    total_gb: number;
    used_gb: number;
    usage_percent: number;
  };
  disk: {
    total_gb: number;
    usage_percent: number;
  };
}

export interface DashboardSessionSummary {
  session_id: string;
  symptom_type: string;
  diagnosed_category: string;
  action_category: string;
  confidence_label: string;
  created_at: string | null;
  display_date: string | null;
  days_ago: number | null;
  is_recurring: boolean;
}

export interface ActionDistributionItem {
  label: string;
  count: number;
}

export interface SessionFrequencyItem {
  date: string;
  count: number;
}

export interface RecentWarningSign {
  id: string;
  warning_sign: string;
  threshold: string;
  recommended_action: string;
  created_at: string | null;
  display_date: string | null;
}

export interface DashboardSummary {
  server_time: string;
  totals: {
    total_sessions: number;
    this_month_count: number;
    escalated_count: number;
  };
  last_diagnosis: DashboardSessionSummary | null;
  current_action_status: DashboardSessionSummary | null;
  recurring_issues_count: number;
  warning_signs_active_count: number;
  action_distribution: ActionDistributionItem[];
  session_frequency: SessionFrequencyItem[];
  recent_warning_signs: RecentWarningSign[];
  last_saved_session: DashboardSessionSummary | null;
  database_warning?: string;
}

export type PageKey =
  | 'home'
  | 'systemProfile'
  | 'newDiagnosis'
  | 'diagnosticHistory'
  | 'recurringPatterns'
  | 'warningSigns'
  | 'reports'
  | 'settings'
  | 'help';