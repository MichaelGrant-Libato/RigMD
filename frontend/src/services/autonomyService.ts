import { API_BASE_URL, apiGet, apiPost } from '../lib/api';
import * as signalR from '@microsoft/signalr';

export interface AutonomyActionDef {
  id?: string;
  name?: string;
  description?: string;
  category?: string;
  riskLevel?: string;
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

export interface AutonomyResult {
  plan?: AutonomyPlan;
  safety?: AutonomySafety;
  execution?: AutonomyExecution;
  verification?: string | number;
  attempts?: AutonomyAttempt[];
  escalated?: boolean;
  trace?: string;
}

export interface AutonomyRequest {
  sessionId: string;
  diagnosedCategory: string;
  userConsentProvided?: boolean;
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

export async function startRemediationStream(onProgress: (message: string) => void) {
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
}: AutonomyRequest) {
  const response = await apiPost<AutonomyResult>(
    '/api/autonomy/execute',
    {
      sessionId,
      diagnosedCategory,
      userConsentProvided,
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
