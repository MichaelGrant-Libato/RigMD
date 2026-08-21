import {
  type DiagnosisResult,
  ACTION_COLORS,
  CONFIDENCE_COLORS,
} from "../types/rigmd";

import {
  useEffect,
  useState,
} from "react";

import { apiFetch } from "../lib/api";

import AutonomyRemediationPanel from "../components/AutonomyRemediationPanel";

interface Props {
  result: DiagnosisResult;
  onRunAnother: () => void;
}

interface RemediationAction {
  id: string;
  label: string;
  description: string;
  risk: string;

  isReversible?: boolean;
  requiresUserConfirmation?: boolean;
}

function normalizeAction(
  action: string,
): string {
  const a =
    action.toLowerCase();

  if (a.includes("escalate")) {
    return "Escalate";
  }

  if (
    a.includes("troubleshoot")
  ) {
    return "Troubleshoot";
  }

  if (a.includes("maintain")) {
    return "Maintain";
  }

  return "Monitor";
}

function ActionBadge({
  action,
}: {
  action: string;
}) {
  const key =
    normalizeAction(action);

  const colors =
    ACTION_COLORS[key] ??
    ACTION_COLORS["Monitor"];

  return (
    <span
      style={{
        display: "inline-block",
        padding: "3px 10px",
        borderRadius: 4,
        fontSize: 11,
        fontWeight: 700,
        letterSpacing: 0.8,
        textTransform:
          "uppercase" as const,
        border: "1px solid",
      }}
      className={`${colors.bg} ${colors.text} ${colors.border}`}
    >
      {key}
    </span>
  );
}

function ConfidenceBadge({
  label,
}: {
  label: string;
}) {
  const color =
    CONFIDENCE_COLORS[label] ??
    CONFIDENCE_COLORS["Low"];

  return (
    <span
      style={{
        fontSize: 13,
        fontWeight: 600,
      }}
      className={color}
    >
      {label} Confidence
    </span>
  );
}

function getNextSteps(
  action: string,
  category: string,
): Array<{
  priority: string;
  text: string;
  detail: string;
}> {
  const a =
    normalizeAction(action);

  const base: Array<{
    priority: string;
    text: string;
    detail: string;
  }> = [];

  if (
    category ===
    "Storage health behavior"
  ) {
    base.push({
      priority: "DO FIRST",
      text: "Back up your important files immediately",
      detail:
        "A SMART warning was detected. Back up documents, photos, and project files to an external drive or cloud storage before doing anything else.",
    });

    base.push({
      priority: "SOON",
      text: "Run Windows Disk Check (chkdsk)",
      detail:
        "Open Command Prompt as Administrator and run: chkdsk C: /f /r. This checks for file system errors on your storage drive.",
    });

    base.push({
      priority: "SOON",
      text: "Check drive health with CrystalDiskInfo",
      detail:
        "Download CrystalDiskInfo and look for Caution or Bad status on your drive. Share this information with a technician if needed.",
    });
  } else if (
    category ===
    "Driver conflict"
  ) {
    base.push({
      priority: "DO FIRST",
      text: "Open Device Manager and check for warnings",
      detail:
        "Press Windows + X and select Device Manager. Look for any device with a yellow exclamation mark. Review that device before changing its driver.",
    });

    base.push({
      priority: "SOON",
      text: "Review the recently installed driver",
      detail:
        "If symptoms started after a driver change, open Device Manager and review the device driver version and rollback availability.",
    });
  } else if (
    category ===
    "Boot and startup failure"
  ) {
    base.push({
      priority: "DO FIRST",
      text: "Run Windows Startup Repair",
      detail:
        "Open Windows Advanced Startup Options and select Troubleshoot → Advanced Options → Startup Repair.",
    });

    base.push({
      priority: "SOON",
      text: "Check recently installed updates",
      detail:
        "Go to Settings → Windows Update → Update History and compare recent system changes with when the problem began.",
    });
  } else if (
    category ===
    "OS performance degradation"
  ) {
    base.push({
      priority: "DO FIRST",
      text: "Review unnecessary startup programs",
      detail:
        "Open Task Manager with Ctrl+Shift+Esc and review the Startup apps list for unnecessary programs.",
    });

    base.push({
      priority: "SOON",
      text: "Review temporary and system files",
      detail:
        "Use Windows Storage settings or Disk Cleanup to review files that may be safely removed.",
    });

    base.push({
      priority: "SOON",
      text: "Check Windows Update history",
      detail:
        "Go to Settings → Windows Update → Update History and determine whether symptoms changed after a recent update.",
    });
  } else if (
    category ===
    "Display driver behavior"
  ) {
    base.push({
      priority: "DO FIRST",
      text: "Review the current display driver",
      detail:
        "Open Device Manager → Display adapters → your GPU → Properties → Driver and review its version and available recovery options.",
    });

    base.push({
      priority: "SOON",
      text: "Use the GPU manufacturer's official driver package",
      detail:
        "If a driver reinstall is required, obtain the appropriate driver from the GPU manufacturer's official support channel.",
    });
  } else if (
    category ===
    "Thermal condition"
  ) {
    base.push({
      priority: "DO FIRST",
      text: "Check CPU and GPU temperatures under load",
      detail:
        "Use a trusted hardware monitoring utility and compare temperatures while the reported symptom occurs.",
    });

    base.push({
      priority: "SOON",
      text: "Inspect cooling components",
      detail:
        "Shut down and unplug the PC before physically inspecting fans, vents, heatsinks, and dust buildup.",
    });
  } else {
    base.push({
      priority: "SOON",
      text: "Review Event Viewer for recent errors",
      detail:
        "Press Windows + R, type eventvwr, and review relevant Critical or Error events around the time the symptoms occurred.",
    });
  }

  if (a === "Escalate") {
    base.push({
      priority: "WHEN READY",
      text: "Bring this report to a qualified technician",
      detail:
        "Use the Reports section to preserve the diagnostic information and share it with a technician for further inspection.",
    });
  } else {
    base.push({
      priority: "WHEN READY",
      text: "Run another diagnosis after remediation",
      detail:
        "After the recommended actions have been completed, run another diagnostic session to determine whether the symptom has improved.",
    });
  }

  return base;
}

export default function DiagnosticResultView({
  result,
  onRunAnother,
}: Props) {
  const normalizedAction =
    normalizeAction(
      result.action_category,
    );

  const nextSteps =
    getNextSteps(
      result.action_category,
      result.diagnosed_category,
    );

  const [
    remediationActions,
    setRemediationActions,
  ] = useState<
    RemediationAction[]
  >([]);

  useEffect(() => {
    if (
      !result.diagnosed_category
    ) {
      return;
    }

    let active = true;

    apiFetch(
      `/api/remediation/actions?category=${encodeURIComponent(
        result.diagnosed_category,
      )}`,
    )
      .then((response) => {
        if (!response.ok) {
          throw new Error(
            `Unable to load remediation actions (${response.status}).`,
          );
        }

        return response.json();
      })
      .then((data) => {
        if (!active) {
          return;
        }

        setRemediationActions(
          Array.isArray(data.actions)
            ? data.actions
            : [],
        );
      })
      .catch(() => {
        if (active) {
          setRemediationActions(
            [],
          );
        }
      });

    return () => {
      active = false;
    };
  }, [
    result.diagnosed_category,
  ]);

  const actionStyle: Record<
    string,
    React.CSSProperties
  > = {
    Monitor: {
      borderColor: "#3b82f6",
      color: "#3b82f6",
    },
    Maintain: {
      borderColor: "#22c55e",
      color: "#22c55e",
    },
    Troubleshoot: {
      borderColor: "#f59e0b",
      color: "#f59e0b",
    },
    Escalate: {
      borderColor: "#ef4444",
      color: "#ef4444",
    },
  };

  const priorityStyle: Record<
    string,
    React.CSSProperties
  > = {
    "DO FIRST": {
      background:
        "rgba(239,68,68,0.15)",
      color: "#fca5a5",
      border:
        "1px solid rgba(239,68,68,0.3)",
    },

    SOON: {
      background:
        "rgba(251,191,36,0.12)",
      color: "#fcd34d",
      border:
        "1px solid rgba(251,191,36,0.3)",
    },

    "WHEN READY": {
      background:
        "rgba(148,163,184,0.10)",
      color: "#94a3b8",
      border:
        "1px solid rgba(148,163,184,0.2)",
    },
  };

  return (
    <div style={styles.root}>
      <div
        style={
          styles.advisoryBanner
        }
      >
        <span
          style={{
            color: "#f59e0b",
            marginRight: 8,
          }}
        >
          ⚠
        </span>

        <strong>
          Probable Finding:
        </strong>
        &nbsp;This result is based
        on your reported symptoms
        and system profile. It is
        advisory only and does not
        replace physical hardware
        inspection by a qualified
        technician.
      </div>

      <div style={styles.body}>
        <div
          style={styles.mainCol}
        >
          <div
            style={styles.card}
          >
            <div
              style={
                styles.cardHeader
              }
            >
              <div>
                <div
                  style={
                    styles.dimLabel
                  }
                >
                  DIAGNOSTIC RESULT
                </div>

                <div
                  style={
                    styles.dimValue
                  }
                >
                  {result.is_recurring && (
                    <span
                      style={
                        styles.recurringTag
                      }
                    >
                      ↺ Recurring
                    </span>
                  )}

                  &nbsp;Result Ready
                </div>
              </div>

              <div
                style={{
                  ...styles.readyDot,
                  background:
                    "#22c55e",
                }}
              />
            </div>

            <div
              style={
                styles.resultGrid
              }
            >
              <div>
                <div
                  style={
                    styles.fieldLabel
                  }
                >
                  REPORTED SYMPTOM
                </div>

                <div
                  style={
                    styles.fieldValue
                  }
                >
                  {
                    result.symptom_type
                  }
                </div>

                <div
                  style={
                    styles.fieldSub
                  }
                >
                  {[
                    result.affected_activity,
                    result.frequency,
                    result.severity,
                  ]
                    .filter(Boolean)
                    .join(" · ")}
                </div>
              </div>

              <div>
                <div
                  style={
                    styles.fieldLabel
                  }
                >
                  PROBABLE INTERNAL
                  CAUSE
                </div>

                <div
                  style={{
                    ...styles.fieldValue,
                    color: "#60a5fa",
                    fontSize: 18,
                  }}
                >
                  {
                    result.diagnosed_category
                  }
                </div>
              </div>

              <div>
                <div
                  style={
                    styles.fieldLabel
                  }
                >
                  DIAGNOSTIC
                  CONFIDENCE
                </div>

                <div
                  style={
                    styles.fieldValue
                  }
                >
                  <ConfidenceBadge
                    label={
                      result.confidence_label
                    }
                  />
                </div>

                <div
                  style={
                    styles.fieldSub
                  }
                >
                  {result.confidence_label ===
                  "High"
                    ? "3 of 3"
                    : result.confidence_label ===
                        "Moderate"
                      ? "2 of 3"
                      : "1 of 3"}{" "}
                  key indicators
                  matched
                </div>
              </div>

              <div>
                <div
                  style={
                    styles.fieldLabel
                  }
                >
                  RECOMMENDED ACTION
                </div>

                <div
                  style={{
                    display: "flex",
                    alignItems:
                      "center",
                    gap: 8,
                    marginTop: 4,
                  }}
                >
                  <ActionBadge
                    action={
                      result.action_category
                    }
                  />
                </div>
              </div>
            </div>

            <div
              style={
                styles.actionCards
              }
            >
              {(
                [
                  "Monitor",
                  "Maintain",
                  "Troubleshoot",
                  "Escalate",
                ] as const
              ).map((action) => (
                <div
                  key={action}
                  style={{
                    ...styles.actionCard,

                    borderColor:
                      action ===
                      normalizedAction
                        ? actionStyle[
                            action
                          ]
                            ?.borderColor ??
                          "#475569"
                        : "#1e293b",

                    opacity:
                      action ===
                      normalizedAction
                        ? 1
                        : 0.4,
                  }}
                >
                  <div
                    style={{
                      ...styles.actionCardLabel,

                      color:
                        action ===
                        normalizedAction
                          ? actionStyle[
                              action
                            ]
                              ?.color ??
                            "#94a3b8"
                          : "#475569",
                    }}
                  >
                    {action ===
                      normalizedAction && (
                      <span>
                        ●{" "}
                      </span>
                    )}

                    {action}
                  </div>

                  <div
                    style={
                      styles.actionCardDesc
                    }
                  >
                    {action ===
                      "Monitor" &&
                      "No immediate automated action is indicated. Continue observing the system and reassess if symptoms worsen."}

                    {action ===
                      "Maintain" &&
                      "Standard maintenance may be appropriate. Backend safety evaluation determines whether an automated remediation can run."}

                    {action ===
                      "Troubleshoot" &&
                      "Further diagnostic or corrective actions may be needed. Automated execution remains subject to backend safety rules."}

                    {action ===
                      "Escalate" &&
                      "Further autonomous remediation may be inappropriate. Professional or manual inspection may be required."}
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div
            style={styles.twoCol}
          >
            <div
              style={styles.card}
            >
              <div
                style={
                  styles.sectionTitle
                }
              >
                □ Plain-Language
                Explanation
              </div>

              <p
                style={
                  styles.explanationText
                }
              >
                {result.ai_explanation ??
                  "Explanation unavailable. Please follow the recommended next steps below."}
              </p>
            </div>

            <div
              style={styles.card}
            >
              <div
                style={
                  styles.sectionTitle
                }
              >
                ↗ Why This Result
                Was Given
              </div>

              <p
                style={{
                  fontSize: 12,
                  color: "#64748b",
                  lineHeight: 1.7,
                }}
              >
                RigMD mapped your
                answers against your
                system profile using
                the following factors.
                Each factor contributes
                to the probable cause
                and action category.
              </p>

              <div
                style={{
                  marginTop: 10,
                  display: "flex",
                  flexDirection:
                    "column",
                  gap: 6,
                }}
              >
                {[
                  {
                    label:
                      "Symptom",
                    value:
                      result.symptom_type,
                    tag: "PRIMARY",
                  },

                  result.affected_activity && {
                    label:
                      "Affected Activity",
                    value:
                      result.affected_activity,
                    tag: "PRIMARY",
                  },

                  result.warning_signs && {
                    label:
                      "Warning Signs",
                    value:
                      result.warning_signs,
                    tag: "SECONDARY",
                  },

                  result.recent_changes && {
                    label:
                      "Recent Changes",
                    value:
                      result.recent_changes,
                    tag: "CONTRIBUTING",
                  },
                ]
                  .filter(Boolean)
                  .map(
                    (
                      factor: any,
                      index: number,
                    ) => (
                      <div
                        key={
                          index
                        }
                        style={
                          styles.reasonRow
                        }
                      >
                        <span
                          style={
                            styles.checkIcon
                          }
                        >
                          ✓
                        </span>

                        <div>
                          <span
                            style={{
                              color:
                                "#94a3b8",
                              fontSize: 12,
                            }}
                          >
                            {
                              factor.label
                            }
                            :{" "}
                          </span>

                          <span
                            style={{
                              color:
                                "#e2e8f0",
                              fontSize: 12,
                            }}
                          >
                            {
                              factor.value
                            }
                          </span>

                          &nbsp;

                          <span
                            style={{
                              fontSize: 9,
                              fontWeight: 700,
                              letterSpacing: 0.6,
                              padding:
                                "1px 5px",
                              borderRadius: 3,

                              background:
                                factor.tag ===
                                "PRIMARY"
                                  ? "rgba(59,130,246,0.2)"
                                  : "rgba(148,163,184,0.1)",

                              color:
                                factor.tag ===
                                "PRIMARY"
                                  ? "#60a5fa"
                                  : "#64748b",
                            }}
                          >
                            {
                              factor.tag
                            }
                          </span>
                        </div>
                      </div>
                    ),
                  )}
              </div>
            </div>
          </div>

          {result.recommendations
            .length > 0 && (
            <div
              style={styles.card}
            >
              <div
                style={
                  styles.sectionTitle
                }
              >
                ⚠ Warning Signs
                Reference
              </div>

              <div
                style={{
                  overflowX:
                    "auto" as const,
                }}
              >
                <table
                  style={
                    styles.table
                  }
                >
                  <thead>
                    <tr>
                      {[
                        "Warning Sign",
                        "Threshold",
                        "Recommended Action",
                      ].map(
                        (heading) => (
                          <th
                            key={
                              heading
                            }
                            style={
                              styles.th
                            }
                          >
                            {
                              heading
                            }
                          </th>
                        ),
                      )}
                    </tr>
                  </thead>

                  <tbody>
                    {result.recommendations.map(
                      (
                        recommendation,
                      ) => (
                        <tr
                          key={
                            recommendation.id
                          }
                          style={{
                            borderBottom:
                              "1px solid #1e293b",
                          }}
                        >
                          <td
                            style={{
                              ...styles.td,
                              color:
                                "#f59e0b",
                            }}
                          >
                            {
                              recommendation.warning_sign
                            }
                          </td>

                          <td
                            style={
                              styles.td
                            }
                          >
                            {
                              recommendation.threshold
                            }
                          </td>

                          <td
                            style={{
                              ...styles.td,
                              color:
                                "#94a3b8",
                            }}
                          >
                            {
                              recommendation.recommended_action
                            }
                          </td>
                        </tr>
                      ),
                    )}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {remediationActions.length >
            0 && (
            <div
              style={styles.card}
            >
              <div
                style={
                  styles.sectionTitle
                }
              >
                ⚡ Backend Remediation
                Options&nbsp;

                <span
                  style={{
                    color:
                      "#475569",
                    fontSize: 11,
                  }}
                >
                  {
                    remediationActions.length
                  }{" "}
                  action
                  {remediationActions.length !==
                  1
                    ? "s"
                    : ""}{" "}
                  available
                </span>
              </div>

              <p
                style={{
                  fontSize: 12,
                  color: "#64748b",
                  marginBottom: 14,
                  marginTop: 0,
                  lineHeight: 1.6,
                }}
              >
                These are remediation
                actions reported by the
                backend for this
                diagnostic category.
                Their safety,
                reversibility, consent
                requirements, and
                execution eligibility
                are determined by the
                backend orchestration
                layer.
              </p>

              <div
                style={{
                  marginBottom: 14,
                }}
              >
                <AutonomyRemediationPanel
                  diagnosedCategory={
                    result.diagnosed_category
                  }
                />
              </div>

              <div
                style={{
                  display: "flex",
                  flexDirection:
                    "column",
                  gap: 10,
                }}
              >
                {remediationActions.map(
                  (action) => (
                    <div
                      key={
                        action.id
                      }
                      style={{
                        border:
                          "1px solid #1e293b",
                        borderRadius: 8,
                        padding:
                          "14px 16px",
                        background:
                          "#0b1628",
                      }}
                    >
                      <div
                        style={{
                          display:
                            "flex",
                          justifyContent:
                            "space-between",
                          alignItems:
                            "flex-start",
                          gap: 12,
                        }}
                      >
                        <div
                          style={{
                            flex: 1,
                          }}
                        >
                          <div
                            style={{
                              fontSize: 13,
                              fontWeight: 600,
                              color:
                                "#e2e8f0",
                              marginBottom: 4,
                            }}
                          >
                            {
                              action.label
                            }
                          </div>

                          <div
                            style={{
                              fontSize: 12,
                              color:
                                "#64748b",
                              lineHeight: 1.55,
                            }}
                          >
                            {
                              action.description
                            }
                          </div>

                          <div
                            style={{
                              display:
                                "flex",
                              flexWrap:
                                "wrap",
                              gap: 6,
                              marginTop: 8,
                            }}
                          >
                            <span
                              style={{
                                fontSize: 10,
                                fontWeight: 700,
                                color:
                                  "#94a3b8",
                                border:
                                  "1px solid #334155",
                                borderRadius: 4,
                                padding:
                                  "3px 7px",
                              }}
                            >
                              Risk:{" "}
                              {action.risk ||
                                "Not specified"}
                            </span>

                            {action.isReversible !==
                              undefined && (
                              <span
                                style={{
                                  fontSize: 10,
                                  fontWeight: 700,
                                  color:
                                    action.isReversible
                                      ? "#86efac"
                                      : "#fca5a5",
                                  border:
                                    action.isReversible
                                      ? "1px solid rgba(34,197,94,0.3)"
                                      : "1px solid rgba(239,68,68,0.3)",
                                  background:
                                    action.isReversible
                                      ? "rgba(34,197,94,0.06)"
                                      : "rgba(239,68,68,0.06)",
                                  borderRadius: 4,
                                  padding:
                                    "3px 7px",
                                }}
                              >
                                {action.isReversible
                                  ? "Reversible"
                                  : "Irreversible"}
                              </span>
                            )}

                            {action.requiresUserConfirmation && (
                              <span
                                style={{
                                  fontSize: 10,
                                  fontWeight: 700,
                                  color:
                                    "#fcd34d",
                                  border:
                                    "1px solid rgba(251,191,36,0.3)",
                                  background:
                                    "rgba(251,191,36,0.06)",
                                  borderRadius: 4,
                                  padding:
                                    "3px 7px",
                                }}
                              >
                                Consent may
                                be required
                              </span>
                            )}
                          </div>
                        </div>

                        <div
                          style={{
                            padding:
                              "6px 10px",
                            borderRadius: 6,
                            fontSize: 10,
                            fontWeight: 700,
                            color:
                              "#7dd3fc",
                            border:
                              "1px solid rgba(34,211,238,0.25)",
                            background:
                              "rgba(34,211,238,0.05)",
                            whiteSpace:
                              "nowrap",
                          }}
                        >
                          Backend Managed
                        </div>
                      </div>
                    </div>
                  ),
                )}
              </div>
            </div>
          )}

          <div
            style={styles.twoCol}
          >
            <div
              style={styles.card}
            >
              <div
                style={
                  styles.sectionTitle
                }
              >
                ≡ Recommended Next
                Steps&nbsp;

                <span
                  style={{
                    color:
                      "#475569",
                    fontSize: 11,
                  }}
                >
                  {nextSteps.length}{" "}
                  actions
                </span>
              </div>

              <div
                style={{
                  display: "flex",
                  flexDirection:
                    "column",
                  gap: 12,
                  marginTop: 8,
                }}
              >
                {nextSteps.map(
                  (
                    step,
                    index,
                  ) => (
                    <div
                      key={
                        index
                      }
                      style={
                        styles.stepRow
                      }
                    >
                      <div
                        style={
                          styles.stepNum
                        }
                      >
                        {index + 1}
                      </div>

                      <div
                        style={{
                          flex: 1,
                        }}
                      >
                        <div
                          style={{
                            display:
                              "flex",
                            alignItems:
                              "center",
                            gap: 8,
                            marginBottom: 4,
                          }}
                        >
                          <span
                            style={{
                              ...styles.priorityTag,
                              ...(priorityStyle[
                                step.priority
                              ] ?? {}),
                            }}
                          >
                            {
                              step.priority
                            }
                          </span>

                          <span
                            style={{
                              fontSize: 13,
                              fontWeight: 600,
                              color:
                                "#e2e8f0",
                            }}
                          >
                            {
                              step.text
                            }
                          </span>
                        </div>

                        <p
                          style={{
                            margin: 0,
                            fontSize: 12,
                            color:
                              "#64748b",
                            lineHeight: 1.6,
                          }}
                        >
                          {
                            step.detail
                          }
                        </p>
                      </div>
                    </div>
                  ),
                )}
              </div>
            </div>

            <div
              style={{
                display: "flex",
                flexDirection:
                  "column",
                gap: 12,
              }}
            >
              <div
                style={{
                  ...styles.card,
                  textAlign:
                    "center" as const,
                }}
              >
                <div
                  style={
                    styles.sectionTitle
                  }
                >
                  Session Info
                </div>

                <div
                  style={{
                    fontSize: 12,
                    color:
                      "#64748b",
                    marginBottom: 12,
                  }}
                >
                  {result.created_at
                    ? new Date(
                        result.created_at,
                      ).toLocaleString()
                    : "—"}
                </div>

                <div
                  style={{
                    fontSize: 11,
                    color:
                      "#334155",
                    marginBottom: 16,
                  }}
                >
                  Session ID:{" "}
                  {result.session_id.slice(
                    0,
                    8,
                  )}
                  …
                </div>

                <button
                  onClick={
                    onRunAnother
                  }
                  style={
                    styles.runAnotherBtn
                  }
                >
                  + Run Another
                  Diagnosis
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

const styles: Record<
  string,
  React.CSSProperties
> = {
  root: {
    minHeight: "100%",
    background: "#020c1b",
    color: "#e2e8f0",
    fontFamily:
      "'JetBrains Mono', monospace, sans-serif",
  },

  advisoryBanner: {
    padding: "10px 24px",
    background:
      "rgba(251,191,36,0.07)",
    borderBottom:
      "1px solid rgba(251,191,36,0.2)",
    fontSize: 12,
    color: "#94a3b8",
    lineHeight: 1.5,
  },

  body: {
    padding: "20px 24px",
    display: "flex",
    gap: 16,
  },

  mainCol: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    gap: 16,
  },

  card: {
    background: "#0b1628",
    border:
      "1px solid #1e293b",
    borderRadius: 10,
    padding: "18px 20px",
  },

  cardHeader: {
    display: "flex",
    justifyContent:
      "space-between",
    alignItems: "center",
    marginBottom: 16,
    paddingBottom: 12,
    borderBottom:
      "1px solid #1e293b",
  },

  dimLabel: {
    fontSize: 10,
    color: "#475569",
    letterSpacing: 1,
    textTransform:
      "uppercase" as const,
  },

  dimValue: {
    fontSize: 13,
    color: "#94a3b8",
    marginTop: 2,
  },

  readyDot: {
    width: 10,
    height: 10,
    borderRadius: "50%",
  },

  resultGrid: {
    display: "grid",
    gridTemplateColumns:
      "1fr 1fr",
    gap: 20,
    marginBottom: 20,
  },

  fieldLabel: {
    fontSize: 10,
    color: "#334155",
    letterSpacing: 1,
    textTransform:
      "uppercase" as const,
    marginBottom: 4,
  },

  fieldValue: {
    fontSize: 15,
    fontWeight: 600,
    color: "#e2e8f0",
  },

  fieldSub: {
    fontSize: 11,
    color: "#475569",
    marginTop: 2,
  },

  recurringTag: {
    fontSize: 10,
    background:
      "rgba(251,191,36,0.15)",
    color: "#fcd34d",
    border:
      "1px solid rgba(251,191,36,0.3)",
    borderRadius: 4,
    padding: "2px 7px",
    marginRight: 6,
  },

  actionCards: {
    display: "grid",
    gridTemplateColumns:
      "repeat(4,1fr)",
    gap: 10,
  },

  actionCard: {
    border: "1.5px solid",
    borderRadius: 8,
    padding: "12px 14px",
    transition:
      "opacity 0.2s",
  },

  actionCardLabel: {
    fontSize: 12,
    fontWeight: 700,
    marginBottom: 6,
    letterSpacing: 0.3,
  },

  actionCardDesc: {
    fontSize: 11,
    color: "#475569",
    lineHeight: 1.55,
  },

  twoCol: {
    display: "grid",
    gridTemplateColumns:
      "1fr 1fr",
    gap: 16,
  },

  sectionTitle: {
    fontSize: 12,
    fontWeight: 700,
    color: "#64748b",
    letterSpacing: 0.5,
    marginBottom: 12,
    paddingBottom: 8,
    borderBottom:
      "1px solid #1e293b",
  },

  explanationText: {
    fontSize: 13,
    color: "#94a3b8",
    lineHeight: 1.75,
    margin: 0,
    whiteSpace:
      "pre-line" as const,
  },

  reasonRow: {
    display: "flex",
    alignItems:
      "flex-start",
    gap: 8,
  },

  checkIcon: {
    color: "#22c55e",
    fontSize: 12,
    flexShrink: 0,
    marginTop: 2,
  },

  table: {
    width: "100%",
    borderCollapse:
      "collapse" as const,
    fontSize: 12,
  },

  th: {
    textAlign:
      "left" as const,
    padding: "8px 12px",
    fontSize: 10,
    color: "#475569",
    letterSpacing: 0.8,
    textTransform:
      "uppercase" as const,
    borderBottom:
      "1px solid #1e293b",
    fontWeight: 600,
  },

  td: {
    padding: "10px 12px",
    color: "#e2e8f0",
    fontSize: 12,
    verticalAlign: "top",
  },

  stepRow: {
    display: "flex",
    gap: 12,
    alignItems:
      "flex-start",
  },

  stepNum: {
    width: 24,
    height: 24,
    borderRadius: "50%",
    background: "#1e293b",
    color: "#64748b",
    fontSize: 11,
    fontWeight: 700,
    display: "flex",
    alignItems: "center",
    justifyContent:
      "center",
    flexShrink: 0,
  },

  priorityTag: {
    fontSize: 9,
    fontWeight: 700,
    letterSpacing: 0.8,
    padding: "2px 7px",
    borderRadius: 3,
    textTransform:
      "uppercase" as const,
  },

  runAnotherBtn: {
    width: "100%",
    padding: "10px",
    background: "#1d4ed8",
    color: "#fff",
    border: "none",
    borderRadius: 6,
    fontSize: 13,
    fontWeight: 600,
    cursor: "pointer",
  },
};