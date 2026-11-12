import { API_BASE_URL, apiGet, apiPost } from '../lib/api';
import * as signalR from '@microsoft/signalr';
import { getLocalApiToken } from '../lib/localApiSession';
import { readUninstallApp, readUninstallOperation, readUninstallPreview, type UninstallApp } from '../lib/appUninstall';
import { readStorageOverview } from '../lib/storageOverview';

async function remediationPost<T>(path: string, data: unknown) {
  try {
    return await apiPost<T>(path, data);
  } catch (error) {
    const response = (error as { response?: { status?: number; data?: { remediation_guard?: unknown } } }).response;
    if (response?.status === 409 && response.data?.remediation_guard) {
      window.dispatchEvent(new Event('rigmd-remediation-evidence-changed'));
    }
    throw error;
  }
}

export interface AutonomyActionDef {
  id?: string;
  name?: string;
  description?: string;
  category?: string;
  riskLevel?: string;
  safetyTier?: string;
  toolArgumentsJson?: string;
  isReversible?: boolean;
  requiresUserConfirmation?: boolean;
}

export interface AutonomyPlan {
  sessionId?: string;
  plannedActions?: AutonomyActionDef[];
  strategyReasoning?: string;
}

export interface AutonomySafety {
  isApproved?: boolean;
  requiresUserConfirmation?: boolean;
  rejectionReason?: string;
  warnings?: string[];
}

export interface AutonomyExecutionProof {
  label?: string;
  status?: string;
  meaning?: string;
  before?: string;
  after?: string;
}

export interface AutonomyExecution {
  success?: boolean;
  summary?: string;
  outputLog?: string;
  proof?: AutonomyExecutionProof[];
}

export interface AutonomyAttempt {
  action?: AutonomyActionDef;
  state?: string | number;
  execution?: AutonomyExecution;
  verification?: string | number;
  rollbackResult?: AutonomyExecution;
  notes?: string;
}

export interface ReActTraceStep {
  stepIndex: number;
  stepType: string;
  title: string;
  content: string;
  toolName?: string;
  toolArgumentsJson?: string;
  observationJson?: string;
  durationMs?: number;
  timestampUtc?: string;
}

export interface ToolDryRunPreview {
  approvalToken?: string;
  approvalExpiresAt?: string;
  toolName: string;
  displayName: string;
  safetyTier: string | number;
  canExecute: boolean;
  requiresAdmin: boolean;
  isRunningAsAdmin: boolean;
  requiresUserConfirmation: boolean;
  whatWillHappen: string;
  affectedItemsCount: number;
  estimatedBytesAffected: number;
  estimatedBytesKnown?: boolean;
  affectedTargets?: string[];
  warnings?: string[];
}

export interface ProposedToolInvocation {
  toolName: string;
  displayName: string;
  safetyTier: string;
  argumentsJson: string;
  rootCauseAnalysis: string;
  remediationRationale: string;
  evidenceCitations?: string[];
  dryRunPreview?: ToolDryRunPreview;
}

export interface PostExecutionVerificationReport {
  verificationToolName: string;
  status: string | number;
  summary: string;
  beforeSnapshotJson?: string;
  afterSnapshotJson?: string;
  metricDeltas?: AutonomyExecutionProof[];
}

export interface RegisteredAgentTool {
  name: string;
  displayName: string;
  description: string;
  safetyTier: string;
}

export interface AutonomyResult {
  persistenceWarning?: string;
  engineMode?: string;
  rootCauseAnalysis?: string;
  plan?: AutonomyPlan;
  safety?: AutonomySafety;
  execution?: AutonomyExecution;
  verification?: string | number;
  attempts?: AutonomyAttempt[];
  reasoningSteps?: ReActTraceStep[];
  proposedTool?: ProposedToolInvocation;
  dryRunPreview?: ToolDryRunPreview;
  verificationReport?: PostExecutionVerificationReport;
  escalated?: boolean;
  trace?: string;
}

export interface AutonomyRequest {
  findingMetricId?: string;
  approvalToken?: string;
  sessionId: string;
  diagnosedCategory: string;
  userConsentProvided?: boolean;
  toolName?: string;
  toolArgumentsJson?: string;
}

export interface MemoryAppCandidate {
  id: string;
  name: string;
  displayName: string;
  appKind: string;
  detail: string;
  closeWarning: string;
  processCount: number;
  memoryMb: number;
}

interface MemoryAppsResponse {
  apps?: MemoryAppCandidate[];
}


interface CloseSelectedAppRequest {
  sessionId: string;
  approvalToken?: string;
  processNames: string[];
  confirmed: boolean;
}

// --- SignalR Live Streaming Setup ---
let remediationHubConnection: signalR.HubConnection | null = null;

export async function startRemediationStream(
  onProgress: (message: string) => void,
  onReActStep?: (step: ReActTraceStep) => void,
) {
  if (remediationHubConnection) {
    await stopRemediationStream();
  }

  remediationHubConnection = new signalR.HubConnectionBuilder()
    .withUrl(`${API_BASE_URL}/hubs/remediation`, { accessTokenFactory: getLocalApiToken })
    .withAutomaticReconnect()
    .build();

  remediationHubConnection.on('ReceiveProgress', (message: string) => {
    onProgress(message);
  });

  if (onReActStep) {
    remediationHubConnection.on('ReceiveReActStep', (step: ReActTraceStep) => {
      onReActStep(step);
    });
  }

  try {
    await remediationHubConnection.start();
  } catch (err) {
    console.error('SignalR Connection Error: ', err);
  }
}

export async function stopRemediationStream() {
  if (remediationHubConnection) {
    await remediationHubConnection.stop();
    remediationHubConnection = null;
  }
}

async function openWindowsReview(target: 'storage settings' | 'reliability monitor' | 'installed apps', fallbackMessage: string) {
  try {
    const { data } = await apiPost<{ success: boolean; summary?: string }>(
      '/api/remediation/open-target', { target }, { timeout: 15000 },
    );
    if (data.success !== true) throw new Error(data.summary || fallbackMessage);
    return data;
  } catch (error) {
    const response = (error as { response?: { data?: { summary?: string } } }).response;
    throw new Error(response?.data?.summary || (error instanceof Error ? error.message : fallbackMessage));
  }
}

export async function openStorageSettings() {
  return openWindowsReview('storage settings', 'Could not open Windows Storage.');
}

export async function openReliabilityMonitor() {
  return openWindowsReview('reliability monitor', 'Could not open Windows Reliability Monitor.');
}

export async function openInstalledApps() {
  return openWindowsReview('installed apps', 'Could not open Windows Installed apps.');
}

export async function getStorageOverview(metricId: string, signal: AbortSignal) {
  const { data } = await apiGet<unknown>(`/api/autonomy/storage-overview?metricId=${encodeURIComponent(metricId)}`, { signal, timeout: 15000, headers: { 'Cache-Control': 'no-store' } });
  return readStorageOverview(data, metricId);
}

export interface StorageAppSnapshot {
  capturedAt: string;
  partial: boolean;
  summary: string;
  apps: UninstallApp[];
}

export async function getStorageApps(signal: AbortSignal, includeStore = false): Promise<StorageAppSnapshot> {
  const { data } = await apiGet<StorageAppSnapshot>(`/api/autonomy/storage-apps?includeStore=${includeStore}`, { signal, timeout: 12000, headers: { 'Cache-Control': 'no-store' } });
  if (!data || typeof data.capturedAt !== 'string' || !Number.isFinite(Date.parse(data.capturedAt)) ||
    typeof data.partial !== 'boolean' || typeof data.summary !== 'string' || !Array.isArray(data.apps) || data.apps.length > 2048 ||
    data.apps.some(app => !app || typeof app.name !== 'string' || !app.name.trim() ||
      typeof app.publisher !== 'string' || typeof app.version !== 'string' || typeof app.scope !== 'string' ||
      app.estimatedBytes !== null && (!Number.isSafeInteger(app.estimatedBytes) || app.estimatedBytes <= 0))) {
    throw new Error('The installed-app inventory could not be verified.');
  }
  data.apps.forEach(readUninstallApp);
  return data;
}

export async function previewAppUninstall(sessionId: string, metricId: string, appId: string) {
  const { data } = await remediationPost<unknown>('/api/autonomy/storage-apps/uninstall/preview', { sessionId, metricId, appId });
  return readUninstallPreview(data, appId);
}
export async function executeAppUninstall(sessionId: string, metricId: string, appId: string, approvalToken: string) {
  const { data } = await remediationPost<unknown>('/api/autonomy/storage-apps/uninstall/execute', { sessionId, metricId, appId, approvalToken, confirmed: true });
  const operation = readUninstallOperation(data, sessionId, metricId);
  if (operation.app.id !== appId) throw new Error('The uninstall identity could not be verified. Do not retry automatically.');
  return operation;
}
export async function getAppUninstallOperation(id: string, sessionId: string, metricId: string, appId: string, signal: AbortSignal) {
  const { data } = await apiGet<unknown>(`/api/autonomy/storage-apps/uninstall/operations/${encodeURIComponent(id)}`, { signal, timeout: 15000 });
  const operation = readUninstallOperation(data, sessionId, metricId, appId);
  if (operation.id !== id) throw new Error('The uninstall operation identity changed.');
  return operation;
}
export async function getRecentAppUninstalls(sessionId: string, metricId: string, signal: AbortSignal) {
  const { data } = await apiGet<unknown>(`/api/autonomy/storage-apps/uninstall/operations?sessionId=${encodeURIComponent(sessionId)}`, { signal, timeout: 15000 });
  if (!Array.isArray(data) || data.length > 16) throw new Error('Uninstall history could not be verified.');
  return data.filter(item => item && typeof item === 'object' && item.metricId === metricId)
    .map(item => readUninstallOperation(item, sessionId, metricId));
}

export async function runAutonomyPreview({
  sessionId,
  diagnosedCategory,
}: AutonomyRequest) {
  const response = await remediationPost<AutonomyResult>(
    '/api/autonomy/preview',
    {
      sessionId,
      diagnosedCategory,
    },
  );

  return response.data;
}

export async function runAutonomyExecution({
  findingMetricId,
  approvalToken,
  sessionId,
  diagnosedCategory,
  userConsentProvided = false,
  toolName,
  toolArgumentsJson,
}: AutonomyRequest) {
  const response = await remediationPost<AutonomyResult>(
    '/api/autonomy/execute',
    {
      approvalToken,
      findingMetricId,
      sessionId,
      diagnosedCategory,
      userConsentProvided,
      toolName,
      toolArgumentsJson,
    },
  );

  return response.data;
}

export async function getRegisteredAgentTools() {
  const response = await apiGet<RegisteredAgentTool[]>(
    '/api/autonomy/tools',
  );
  return response.data ?? [];
}

export async function previewAgentTool(
  toolName: string,
  args: Record<string, unknown> = {},
  sessionId?: string,
  findingMetricId?: string,
) {
  const response = await remediationPost<ToolDryRunPreview>(
    '/api/autonomy/tools/preview',
    {
      toolName,
      arguments: args,
      sessionId,
      findingMetricId,
    },
  );
  return response.data;
}

export async function getMemoryAppCandidates() {
  const response = await apiGet<MemoryAppsResponse>(
    '/api/autonomy/memory-apps',
  );

  return response.data.apps ?? [];
}

export async function closeSelectedApp({
  sessionId,
  approvalToken,
  processNames,
  confirmed,
}: CloseSelectedAppRequest) {
  const response = await remediationPost<AutonomyResult>(
    '/api/autonomy/close-selected-app',
    {
      sessionId,
      approvalToken,
      processNames,
      confirmed,
    },
  );

  return response.data;
}

export async function previewCloseSelectedApp(sessionId: string, processNames: string[]) {
  const response = await remediationPost<ToolDryRunPreview>('/api/autonomy/close-selected-app/preview', {
    sessionId, processNames,
  });
  return response.data;
}

export interface AgentSettingsResponse {
  preferredMode: 'auto' | 'local-only' | string;
  hasGeminiApiKey: boolean;
  activeEngine: string;
  registeredToolCount: number;
  settingsFilePath?: string;
}

export interface UpdateAgentSettingsPayload {
  preferredMode?: 'auto' | 'local-only';
}

export async function getAgentSettings() {
  const response = await apiGet<AgentSettingsResponse>(
    '/api/autonomy/settings',
  );
  return response.data;
}

export async function updateAgentSettings(
  payload: UpdateAgentSettingsPayload,
) {
  const response = await apiPost<AgentSettingsResponse>(
    '/api/autonomy/settings',
    payload,
  );
  return response.data;
}

export function getBackendErrorMessage(error: unknown) {
  const data = (
    error as {
      response?: {
        data?: Record<string, unknown>;
      };
    }
  )?.response?.data;

  const fields = [
    data?.message,
    data?.detail,
    data?.error,
    data?.reason,
    data?.status,
    data?.safetyReason,
    data?.rejectionReason,
    data?.consentReason,
    data?.rollbackMessage,
  ];

  const first = fields.find(
    (value) =>
      typeof value === 'string' &&
      value.trim().length > 0,
  );

  if (typeof first === 'string') {
    return first;
  }

  return 'Remediation request failed. Please check the backend connection and try again.';
}
