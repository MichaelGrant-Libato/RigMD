//rigmd.ts

export interface HardwareStats {
  device_name: string;
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
    process_insights?: {
    browser_detected: boolean;
    browser_process_count: number;
    browser_memory_mb: number;
    browser_heavy: boolean;
    game_detected: boolean;
    game_processes: string[];
    top_memory_apps: Array<{
      name: string;
      process_count: number;
      memory_mb: number;
    }>;
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

// Map SessionSummary to your DashboardSessionSummary structure so DiagnosticHistoryView is satisfied
export type SessionSummary = DashboardSessionSummary;

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

// ─── ADDED MISSING DATA SCHEMAS FOR THE INTAKE/RESULT VIEWS ───

export interface DiagnosisResult {
  session_id: string;
  symptom_type: string;
  diagnosed_category: string;
  confidence_label: string;
  action_category: string;
  is_recurring: boolean;
  

  frequency: string;
  severity: string;
  affected_activity?: string; 
  warning_signs?: string;     
  recent_changes?: string;    
  
  ai_explanation?: string;
  created_at?: string;
  recommendations: RecentWarningSign[];
}

export interface SymptomIntakePayload {
  profile_id: string;
  symptom_type: string;
  affected_activity?: string;
  frequency: string;
  severity: string;
  duration?: string;
  recent_changes?: string;
  system_state?: string;
  warning_signs?: string;
}

// ─── ADDED MISSING STATIC EXPORTS UTILIZED BY RECENT COMPONENTS ───

export const ACTION_COLORS: Record<string, { bg: string; text: string; border: string }> = {
  Monitor: { bg: "bg-blue-950/40", text: "text-blue-400", border: "border-blue-800/50" },
  Maintain: { bg: "bg-green-950/40", text: "text-green-400", border: "border-green-800/50" },
  Troubleshoot: { bg: "bg-amber-950/40", text: "text-amber-400", border: "border-amber-800/50" },
  Escalate: { bg: "bg-red-950/40", text: "text-red-400", border: "border-red-800/50" },
};

export const CONFIDENCE_COLORS: Record<string, string> = {
  High: "text-green-400",
  Moderate: "text-amber-400",
  Low: "text-slate-400",
};

export const SYMPTOM_TYPES = ["OS performance degradation", "Thermal condition", "Storage health behavior", "Driver conflict", "Boot and startup failure", "Display driver behavior"] as const;
export const FREQUENCY_OPTIONS = ["Intermittent", "Consistent", "Rarely"] as const;
export const SEVERITY_OPTIONS = ["Low", "Moderate", "High"] as const;
export const DURATION_OPTIONS = ["Less than a day", "A few days", "Weeks", "Months"] as const;
export const AFFECTED_ACTIVITY_OPTIONS = ["Gaming", "Office Work", "Booting up", "Idle"] as const;
export const RECENT_CHANGES_OPTIONS = ["None", "Updated Drivers", "Installed New Hardware", "Windows Update"] as const;
export const SYSTEM_STATE_OPTIONS = ["Running fine", "Sluggish", "Freezing", "No Boot"] as const;
export const WARNING_SIGNS_OPTIONS = ["High Temps", "Blue Screen (BSOD)", "Loud Fan Noise", "None"] as const;
