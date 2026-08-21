import { apiPost } from '../lib/api';

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
  state?: string;
  execution?: AutonomyExecution;
  verification?: string;
  rollbackResult?: AutonomyExecution;
  notes?: string;
}

export interface AutonomyResult {
  plan?: AutonomyPlan;
  safety?: AutonomySafety;
  execution?: AutonomyExecution;
  verification?: string;
  attempts?: AutonomyAttempt[];
  escalated?: boolean;
  trace?: string;
}

export interface AutonomyRequest {
  diagnosedCategory: string;
  userConsentProvided?: boolean;
}

export async function runAutonomyDryRun({ diagnosedCategory }: AutonomyRequest) {
  const response = await apiPost<AutonomyResult>('/api/autonomy/dry-run', {
    diagnosedCategory,
  });

  return response.data;
}

export async function runAutonomyExecution({ diagnosedCategory, userConsentProvided = false }: AutonomyRequest) {
  const response = await apiPost<AutonomyResult>('/api/autonomy/execute', {
    diagnosedCategory,
    userConsentProvided,
  });

  return response.data;
}

export function getBackendErrorMessage(error: unknown) {
  const data = (error as { response?: { data?: Record<string, unknown> } })?.response?.data;
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

  const first = fields.find((value) => typeof value === 'string' && value.trim().length > 0);
  if (typeof first === 'string') return first;

  return 'Remediation request failed. Please check the backend connection and try again.';
}
