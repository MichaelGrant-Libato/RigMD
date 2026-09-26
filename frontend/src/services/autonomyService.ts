import { API_BASE_URL, apiGet, apiPost } from '../lib/api';
import * as signalR from '@microsoft/signalr';

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
    .withUrl(`${API_BASE_URL}/hubs/remediation`)
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

export async function runAutonomyPreview({
  sessionId,
  diagnosedCategory,
}: AutonomyRequest) {
  const response = await apiPost<AutonomyResult>(
    '/api/autonomy/preview',
    {
      sessionId,
      diagnosedCategory,
    },
  );

  return response.data;
}

export async function runAutonomyExecution({
  sessionId,
  diagnosedCategory,
  userConsentProvided = false,
  toolName,
  toolArgumentsJson,
}: AutonomyRequest) {
  const response = await apiPost<AutonomyResult>(
    '/api/autonomy/execute',
    {
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
) {
  const response = await apiPost<ToolDryRunPreview>(
    '/api/autonomy/tools/preview',
    {
      toolName,
      arguments: args,
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
  processNames,
  confirmed,
}: CloseSelectedAppRequest) {
  const response = await apiPost<AutonomyResult>(
    '/api/autonomy/close-selected-app',
    {
      processNames,
      confirmed,
    },
  );

  return response.data;
}

export interface AgentSettingsResponse {
  preferredMode: 'auto' | 'local-only' | string;
  autoExecuteSafeTier1: boolean;
  hasGeminiApiKey: boolean;
  maskedGeminiApiKey: string;
  activeEngine: string;
  registeredToolCount: number;
  settingsFilePath?: string;
}

export interface UpdateAgentSettingsPayload {
  preferredMode?: 'auto' | 'local-only';
  autoExecuteSafeTier1?: boolean;
  geminiApiKey?: string;
  clearGeminiApiKey?: boolean;
}

export interface AutonomousDoctorResponse {
  sessionId: string;
  diagnosedCategory: string;
  actionCategory: string;
  confidenceLabel: string;
  aiExplanation: string;
  autoExecuted: boolean;
  activeEngine: string;
  orchestration: AutonomyResult;
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

export async function runAutonomousDoctor(payload: {
  userPrompt?: string;
  autoExecuteSafeFixes?: boolean;
}) {
  const response = await apiPost<AutonomousDoctorResponse>(
    '/api/autonomy/autonomous-doctor',
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
