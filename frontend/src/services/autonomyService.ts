import { apiGet,apiPost, } from '../lib/api';

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

interface AgentRemediationProof {
  Label?: string;
  Status?: string;
  Meaning?: string;
  Before?: string;
  After?: string;
}

interface AgentRemediationResult {
  Success?: boolean;
  Summary?: string;
  OutputLog?: string;
  Proof?: AgentRemediationProof[];
  ActionId?: string;
  ExecutedBy?: string;
}

interface AgentCommandResponse {
  id: string;
  agentId: string;
  commandType: string;

  status:
    | 'pending'
    | 'running'
    | 'completed'
    | 'failed';

  requestedAt: string;
  claimedAt: string | null;
  completedAt: string | null;
  errorMessage: string | null;

  result?: AgentRemediationResult | null;
}


export interface AutonomyRequest {
  sessionId: string;
  diagnosedCategory: string;
  userConsentProvided?: boolean;
}

const AGENT_ID =
  import.meta.env.VITE_AGENT_ID;

const AGENT_REMEDIATION_TIMEOUT_MS =
  45_000;

const AGENT_REMEDIATION_POLL_MS =
  1_500;

const wait = (
  milliseconds: number,
) =>
  new Promise<void>((resolve) => {
    window.setTimeout(
      resolve,
      milliseconds,
    );
  });

export async function runAutonomyDryRun({
  sessionId,
  diagnosedCategory,
}: AutonomyRequest) {
  const response = await apiPost<AutonomyResult>(
    '/api/autonomy/dry-run',
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

export type AgentRemediationActionId =
  | 'clear_user_temp_files'
  | 'flush_dns';

const AGENT_REMEDIATION_ACTIONS: Record<
  AgentRemediationActionId,
  {
    endpoint: string;
    name: string;
    description: string;
  }
> = {
  clear_user_temp_files: {
    endpoint:
      'clear-user-temp-files',

    name:
      'Clear User Temp Files',

    description:
      'Remove accessible temporary files for the active Windows user.',
  },

  flush_dns: {
    endpoint:
      'flush-dns',

    name:
      'Flush DNS Cache',

    description:
      'Flush the Windows DNS resolver cache.',
  },
};

  export async function runAgentRemediation(
    actionId: AgentRemediationActionId,
  ): Promise<AutonomyResult> {
    if (!AGENT_ID) {
      throw new Error(
        'VITE_AGENT_ID is not configured.',
      );
    }

    const action =
      AGENT_REMEDIATION_ACTIONS[actionId];

    const createResponse =
      await apiPost<AgentCommandResponse>(
        `/api/agent/${AGENT_ID}/remediation/${action.endpoint}`,
        {
          confirmed: true,
        },
        {
          headers: {
            'X-Client-ID':
              AGENT_ID,
          },
        },
      );

    const commandId =
      createResponse.data.id;

    if (!commandId) {
      throw new Error(
        'RigMD did not return a remediation command ID.',
      );
    }

    const deadline =
      Date.now() +
      AGENT_REMEDIATION_TIMEOUT_MS;

    while (
      Date.now() <
      deadline
    ) {
      await wait(
        AGENT_REMEDIATION_POLL_MS,
      );

      const commandResponse =
        await apiGet<AgentCommandResponse>(
          `/api/agent/${AGENT_ID}/commands/${commandId}`,
          {
            headers: {
              'X-Client-ID':
                AGENT_ID,
            },
          },
        );

      const command =
        commandResponse.data;

      if (
        command.status ===
        'failed'
      ) {
        throw new Error(
          command.errorMessage ||
            'The RigMD Agent reported that remediation failed.',
        );
      }

      if (
        command.status !==
        'completed'
      ) {
        continue;
      }

      const agentResult =
        command.result;

      const execution: AutonomyExecution =
        {
          success:
            agentResult?.Success ??
            true,

          summary:
            agentResult?.Summary ||
            `The installed RigMD Agent completed ${action.name}.`,

          outputLog:
            agentResult?.OutputLog,

          proof:
            agentResult?.Proof?.map(
              (item) => ({
                label:
                  item.Label,

                status:
                  item.Status,

                meaning:
                  item.Meaning,

                before:
                  item.Before,

                after:
                  item.After,
              }),
            ),
        };

      return {
        plan: {
          plannedActions: [
            {
              id:
                actionId,

              name:
                action.name,

              description:
                action.description,

              category:
                'Maintain',

              riskLevel:
                'Low',

              isReversible:
                false,

              requiresUserConfirmation:
                true,
            },
          ],
        },

        safety: {
          isApproved:
            true,

          requiresUserConfirmation:
            true,

          warnings: [
            'Execution was sent to the installed RigMD Agent after explicit user consent.',
          ],
        },

        execution,

        attempts: [
          {
            action: {
              id:
                actionId,

              name:
                action.name,

              riskLevel:
                'Low',

              isReversible:
                false,

              requiresUserConfirmation:
                true,
            },

            state:
              'Completed',

            execution,

            notes:
              agentResult?.ExecutedBy
                ? `Executed by ${agentResult.ExecutedBy}.`
                : 'Executed by the installed RigMD Agent.',
          },
        ],

        trace:
          `[AGENT] Command ${commandId} completed through the installed RigMD Agent.`,
      };
    }

    throw new Error(
      'The RigMD Agent did not finish remediation within 45 seconds.',
    );
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