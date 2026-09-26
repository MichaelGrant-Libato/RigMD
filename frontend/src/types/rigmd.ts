//rigmd.ts

export interface AgentStatus {
  agentId: string;
  clientId: string;
  deviceName: string;
  agentVersion: string;
  registeredAt: string;
  lastSeen: string;
  isOnline: boolean;
}

export interface ComponentPresenceInfo {
  componentId: string;
  status: 'Present' | 'NotPresent';
  isSelectable: boolean;
  badge: string;
  reason: string;
}

export interface HardwarePresenceProbe {
  deviceType: string;
  hasBattery: boolean;
  hasGpu: boolean;
  hasDedicatedGpu: boolean;
  components: Record<string, ComponentPresenceInfo>;
}

export interface AgentHardwareSnapshot {
  deviceName: string;
  deviceType?: string;
  activePowerPlan?: string;
  connectedDisplays?: number;
  presence?: HardwarePresenceProbe | null;
  battery?: {
    isCharging: boolean;
    chargePercent: number;
    healthStatus: string;
  } | null;
  network?: {
    isWifi: boolean;
    wifiSignalStrength: number | null;
    macAddress: string;
    ipAddress: string;
    pingLatencyMs?: number | null;
    packetLossPercent?: number | null;
  } | null;
  displays?: Array<{
    name: string;
    resolution: string;
    refreshRate: number;
  }>;
  deviceErrors?: Array<{
    name: string;
    deviceId: string;
    errorCode: number;
    description: string;
  }>;
  osVersion: string;
  systemAge: string;
  chipsetDriver: string;
  primaryStorageType: string;

  cpu: {
    name: string;
    cores: number;
    threads: number;
    frequencyMhz: number;
    usagePercent: number;
    maxFrequencyMhz?: number;
    sockets?: number;
    virtualizationEnabled?: boolean;
    l1CacheKb?: number;
    l2CacheMb?: number;
    l3CacheMb?: number;
    processes?: number;
    handles?: number;
    temperatureCelsius?: number | null;
  };

  gpu: {
    name: string;
    type: string;
    driver: string;
    vramGb: number;
    hasGpu?: boolean;
    hasDedicatedGpu?: boolean;
    dedicatedMemoryGb?: number;
    sharedMemoryGb?: number;
    driverDate?: string;
    directxVersion?: string;
    physicalLocation?: string;
    temperatureCelsius?: number;
  };

  ram: {
    usedGb: number;
    totalGb: number;
    usagePercent: number;
    speedMtps?: number;
    slotsUsed?: number;
    slotsTotal?: number;
    formFactor?: string;
    hardwareReservedMb?: number;
    committedGb?: number;
    cachedGb?: number;
  };

  storageDrives: Array<{
    model: string;
    type: string;
    sizeGb: number;
    interface: string;
    mediaType?: string | null;
    busType?: string | null;
    detectionSource?: string | null;
    diskIndex?: number | null;
    usedGb?: number | null;
    usagePercent?: number | null;
    isFailingSmart?: boolean;
    status?: string | null;
    volumes?: Array<{
      drive: string;
      mountpoint: string;
      fsType?: string;
      fstype?: string;
      diskIndex?: number | null;
      disk_index?: number | null;
      totalGb?: number;
      total_gb?: number;
      usedGb?: number;
      used_gb?: number;
      usagePercent?: number;
      usage_percent?: number;
    }>;
  }>;

  allDisks: Array<{
    drive: string;
    mountpoint: string;
    fsType: string;
    diskIndex?: number | null;
    totalGb: number;
    usedGb: number;
    usagePercent: number;
  }>;

  processInsights?: {
    browserDetected: boolean;
    browserProcessCount: number;
    browserMemoryMb: number;
    browserHeavy: boolean;
    gameDetected: boolean;
    gameProcesses: string[];
    topMemoryApps: Array<{
      name: string;
      processCount: number;
      memoryMb: number;
    }>;
  };
}

export interface AgentSnapshotResponse {
  agentId: string;
  capturedAt: string;
  hardware: AgentHardwareSnapshot;
}

export interface AgentCommandResponse {
  id: string;
  agentId: string;
  commandType: string;
  status: 'pending' | 'running' | 'completed' | 'failed';
  requestedAt: string;
  claimedAt: string | null;
  completedAt: string | null;
  errorMessage: string | null;
}

export type AutomaticScanStage =
  | 'idle'
  | 'requesting'
  | 'scanning'
  | 'loading-evidence'
  | 'completed'
  | 'failed';

export interface HardwareStats {
  device_name: string;
  device_type?: string;
  active_power_plan?: string;
  connected_displays?: number;
  battery?: {
    is_charging: boolean;
    charge_percent: number;
    health_status: string;
  } | null;
  network?: {
    is_wifi: boolean;
    wifi_signal_strength: number | null;
    mac_address: string;
    ip_address: string;
    ping_latency_ms?: number | null;
    packet_loss_percent?: number | null;
  } | null;
  displays?: Array<{
    name: string;
    resolution: string;
    refresh_rate: number;
  }>;
  device_errors?: Array<{
    name: string;
    device_id: string;
    error_code: number;
    description: string;
  }>;
  os_version: string;
  system_age: string;
  chipset_driver: string;
  storage_type: string;
  storage_drives?: Array<{
    model: string;
    type: string;
    size_gb: number;
    interface: string;
    media_type?: string | null;
    bus_type?: string | null;
    detection_source?: string | null;
    disk_index?: number | null;
    used_gb?: number | null;
    usage_percent?: number | null;
    is_failing_smart?: boolean;
    status?: string | null;
    volumes?: Array<{
      drive: string;
      mountpoint: string;
      fstype: string;
      disk_index?: number | null;
      total_gb: number;
      used_gb: number;
      usage_percent: number;
    }>;
  }>;
  all_disks?: Array<{
    drive: string;
    mountpoint: string;
    fstype: string;
    disk_index?: number | null;
    total_gb: number;
    used_gb: number;
    usage_percent: number;
  }>;
  cpu: {
    name: string;
    usage_percent: number;
    cores: number;
    threads: number;
    frequency_mhz: number;
    max_frequency_mhz?: number;
    sockets?: number;
    virtualization_enabled?: boolean;
    l1_cache_kb?: number;
    l2_cache_mb?: number;
    l3_cache_mb?: number;
    processes?: number;
    handles?: number;
    temperature_celsius?: number;
  };
  gpu: {
    name: string;
    driver: string;
    type: string;
    vram_gb: number;
    dedicated_memory_gb?: number;
    shared_memory_gb?: number;
    driver_date?: string;
    directx_version?: string;
    physical_location?: string;
    temperature_celsius?: number;
  };
  ram: {
    total_gb: number;
    used_gb: number;
    usage_percent: number;
    speed_mtps?: number;
    slots_used?: number;
    slots_total?: number;
    form_factor?: string;
    hardware_reserved_mb?: number;
    committed_gb?: number;
    cached_gb?: number;
  };
  disk: {
    total_gb: number;
    used_gb?: number;
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
  resolution_status?: string;
  resolution_checked_at?: string | null;
  resolution_summary?: string;
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
  | 'aiAgent'
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
