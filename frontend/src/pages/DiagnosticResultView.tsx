//DiagnosticResultView.tsx

import { type DiagnosisResult, ACTION_COLORS, CONFIDENCE_COLORS } from "../types/rigmd";
import { useState, useEffect } from "react";
import { fetchWithClient } from "../services/apiClient";


// ─── Props ───────────────────────────────────────────────────────────────────

interface Props {
  result: DiagnosisResult;
  onRunAnother: () => void;
}

interface RemediationAction {
  id: string;
  label: string;
  description: string;
  risk: string;
}
 
interface ActionResult {
  success: boolean;
  summary: string;
  cleared?: string;        // only on clear_temp_files
  output?: string;         // only on chkdsk / sfc
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

function normalizeAction(action: string): string {
  const a = action.toLowerCase();
  if (a.includes("escalate"))    return "Escalate";
  if (a.includes("troubleshoot")) return "Troubleshoot";
  if (a.includes("maintain"))    return "Maintain";
  return "Monitor";
}

function ActionBadge({ action }: { action: string }) {
  const key    = normalizeAction(action);
  const colors = ACTION_COLORS[key] ?? ACTION_COLORS["Monitor"];
  return (
    <span
      style={{
        display: "inline-block",
        padding: "3px 10px",
        borderRadius: 4,
        fontSize: 11,
        fontWeight: 700,
        letterSpacing: 0.8,
        textTransform: "uppercase" as const,
        border: `1px solid`,
      }}
      className={`${colors.bg} ${colors.text} ${colors.border}`}
    >
      {key}
    </span>
  );
}

function ConfidenceBadge({ label }: { label: string }) {
  const color = CONFIDENCE_COLORS[label] ?? CONFIDENCE_COLORS["Low"];
  return (
    <span style={{ fontSize: 13, fontWeight: 600 }} className={color}>
      {label} Confidence
    </span>
  );
}

// Recommended next-steps derived from action category
function getNextSteps(action: string, category: string): Array<{ priority: string; text: string; detail: string }> {
  const a = normalizeAction(action);

  const base: Array<{ priority: string; text: string; detail: string }> = [];

  if (category === "Storage health behavior") {
    base.push({ priority: "DO FIRST", text: "Back up your important files immediately", detail: "A SMART warning was detected. Back up documents, photos, and project files to an external drive or cloud storage before doing anything else." });
    base.push({ priority: "SOON",     text: "Run Windows Disk Check (chkdsk)",          detail: "Open Command Prompt as Administrator and run: chkdsk C: /f /r. This checks for file system errors on your storage drive." });
    base.push({ priority: "SOON",     text: "Check drive health with CrystalDiskInfo",   detail: "Download CrystalDiskInfo (free tool) and look for Caution or Bad status on your drive. Share this with a technician if needed." });
  } else if (category === "Driver conflict") {
    base.push({ priority: "DO FIRST", text: "Open Device Manager and check for warnings", detail: "Press Windows + X and select Device Manager. Look for any device with a yellow exclamation mark. Right-click it and choose Update Driver." });
    base.push({ priority: "SOON",     text: "Roll back the recently installed driver",    detail: "If symptoms started after a driver update, go to Device Manager → right-click the device → Properties → Driver tab → Roll Back Driver." });
  } else if (category === "Boot and startup failure") {
    base.push({ priority: "DO FIRST", text: "Run Windows Startup Repair",                detail: "Boot from a Windows installation media or Advanced Startup Options. Select Troubleshoot → Advanced Options → Startup Repair." });
    base.push({ priority: "SOON",     text: "Check for recently installed updates",       detail: "Go to Settings → Windows Update → Update History. If a recent update coincides with the problem, consider uninstalling it." });
  } else if (category === "OS performance degradation") {
    base.push({ priority: "DO FIRST", text: "Disable unnecessary startup programs",      detail: "Open Task Manager (Ctrl+Shift+Esc) → Startup tab. Disable any programs you do not need at login to reduce boot time." });
    base.push({ priority: "SOON",     text: "Run Windows Disk Cleanup",                  detail: "Search for Disk Cleanup in the Start menu. Run it on your C: drive and also run the Clean up system files option. This removes old update files that can slow boot." });
    base.push({ priority: "SOON",     text: "Check Windows Update history",               detail: "Go to Settings → Windows Update → Update History. If a major update was installed within the last 7 days, check if the issue worsened after that specific update." });
  } else if (category === "Display driver behavior") {
    base.push({ priority: "DO FIRST", text: "Boot into Safe Mode and roll back GPU driver", detail: "Hold Shift and click Restart. Go to Troubleshoot → Advanced Options → Startup Settings → Enable Safe Mode. Then open Device Manager and roll back the GPU driver." });
    base.push({ priority: "SOON",     text: "Reinstall the GPU driver cleanly",             detail: "Download DDU (Display Driver Uninstaller) and use it in Safe Mode to fully remove the current GPU driver before installing the correct version from the manufacturer's site." });
  } else if (category === "Thermal condition") {
    base.push({ priority: "DO FIRST", text: "Check CPU and GPU temperatures under load",  detail: "Download HWMonitor (free tool). Run it while using your PC normally. If CPU temps exceed 90°C or GPU temps exceed 95°C, thermal throttling is likely causing your symptoms." });
    base.push({ priority: "SOON",     text: "Clean your PC's cooling components",         detail: "Shut down and unplug your PC. Use compressed air to blow out dust from the CPU heatsink, GPU heatsink, case fans, and vents. Dust is the most common thermal cause." });
  } else {
    base.push({ priority: "SOON", text: "Review Event Viewer for recent errors", detail: "Press Windows + R, type eventvwr, and press Enter. Look in Windows Logs → System and Application for any Critical or Error entries that match the time your symptoms started." });
  }

  if (a === "Escalate") {
    base.push({ priority: "WHEN READY", text: "Bring this report to a qualified technician", detail: "Use the Reports section to export this diagnostic session as a PDF. Share it with a hardware-knowledgeable technician for physical inspection." });
  } else {
    base.push({ priority: "WHEN READY", text: "Re-run diagnosis in 7 days", detail: "After completing the above steps, run a new diagnostic session to check if the symptom has improved. This will update your recurring pattern tracking." });
  }

  return base;
}

// ─── Component ───────────────────────────────────────────────────────────────

export default function DiagnosticResultView({ result, onRunAnother }: Props) {
  const normalizedAction = normalizeAction(result.action_category);
  const nextSteps        = getNextSteps(result.action_category, result.diagnosed_category);

  const [remediationActions, setRemediationActions] = useState<RemediationAction[]>([]);
const [runningAction, setRunningAction]           = useState<string | null>(null);
const [actionResults, setActionResults]           = useState<Record<string, ActionResult>>({});
 
useEffect(() => {
  if (!result.diagnosed_category) return;
  fetchWithClient(`/api/remediation/actions?category=${encodeURIComponent(result.diagnosed_category)}`)
    .then(r => r.json())
    .then(data => setRemediationActions(data.actions ?? []))
    .catch(() => setRemediationActions([]));
}, [result.diagnosed_category]);
 
async function handleFixNow(actionId: string) {
  setRunningAction(actionId);
  try {
    const res = await fetchWithClient("/api/remediation/execute", {
      method:  "POST",
      headers: { "Content-Type": "application/json" },
      body:    JSON.stringify({ action_id: actionId }),
    });
    const data = await res.json();
    setActionResults(prev => ({ ...prev, [actionId]: data }));
  } catch {
    setActionResults(prev => ({
      ...prev,
      [actionId]: { success: false, summary: "Could not connect to the backend." },
    }));
  } finally {
    setRunningAction(null);
  }
}

  const actionStyle: Record<string, React.CSSProperties> = {
    Monitor:     { borderColor: "#3b82f6", color: "#3b82f6" },
    Maintain:    { borderColor: "#22c55e", color: "#22c55e" },
    Troubleshoot:{ borderColor: "#f59e0b", color: "#f59e0b" },
    Escalate:    { borderColor: "#ef4444", color: "#ef4444" },
  };

  const priorityStyle: Record<string, React.CSSProperties> = {
    "DO FIRST":   { background: "rgba(239,68,68,0.15)",   color: "#fca5a5",  border: "1px solid rgba(239,68,68,0.3)" },
    "SOON":       { background: "rgba(251,191,36,0.12)",  color: "#fcd34d",  border: "1px solid rgba(251,191,36,0.3)" },
    "WHEN READY": { background: "rgba(148,163,184,0.10)", color: "#94a3b8",  border: "1px solid rgba(148,163,184,0.2)" },
  };

  return (
    <div style={styles.root}>

      {/* ── Advisory notice ── */}
      <div style={styles.advisoryBanner}>
        <span style={{ color: "#f59e0b", marginRight: 8 }}>⚠</span>
        <strong>Probable Finding:</strong>&nbsp;This result is based on your reported symptoms and system profile. It is advisory only and does not replace physical hardware inspection by a qualified technician.
      </div>

      <div style={styles.body}>
        {/* ── Left: main result ── */}
        <div style={styles.mainCol}>

          {/* Result header card */}
          <div style={styles.card}>
            <div style={styles.cardHeader}>
              <div>
                <div style={styles.dimLabel}>DIAGNOSTIC RESULT</div>
                <div style={styles.dimValue}>
                  {result.is_recurring && <span style={styles.recurringTag}>↺ Recurring</span>}
                  &nbsp;Result Ready
                </div>
              </div>
              <div style={{ ...styles.readyDot, background: "#22c55e" }} />
            </div>

            <div style={styles.resultGrid}>
              <div>
                <div style={styles.fieldLabel}>REPORTED SYMPTOM</div>
                <div style={styles.fieldValue}>{result.symptom_type}</div>
                <div style={styles.fieldSub}>
                  {[result.affected_activity, result.frequency, result.severity].filter(Boolean).join(" · ")}
                </div>
              </div>
              <div>
                <div style={styles.fieldLabel}>PROBABLE INTERNAL CAUSE</div>
                <div style={{ ...styles.fieldValue, color: "#60a5fa", fontSize: 18 }}>
                  {result.diagnosed_category}
                </div>
              </div>
              <div>
                <div style={styles.fieldLabel}>DIAGNOSTIC CONFIDENCE</div>
                <div style={styles.fieldValue}>
                  <ConfidenceBadge label={result.confidence_label} />
                </div>
                <div style={styles.fieldSub}>
                  {result.confidence_label === "High" ? "3 of 3" : result.confidence_label === "Moderate" ? "2 of 3" : "1 of 3"} key indicators matched
                </div>
              </div>
              <div>
                <div style={styles.fieldLabel}>RECOMMENDED ACTION</div>
                <div style={{ display: "flex", alignItems: "center", gap: 8, marginTop: 4 }}>
                  <ActionBadge action={result.action_category} />
                </div>
              </div>
            </div>

            {/* Action cards */}
            <div style={styles.actionCards}>
              {(["Monitor","Maintain","Troubleshoot","Escalate"] as const).map(a => (
                <div
                  key={a}
                  style={{
                    ...styles.actionCard,
                    borderColor: a === normalizedAction ? (actionStyle[a]?.borderColor ?? "#475569") : "#1e293b",
                    opacity: a === normalizedAction ? 1 : 0.4,
                  }}
                >
                  <div style={{ ...styles.actionCardLabel, color: a === normalizedAction ? (actionStyle[a]?.color ?? "#94a3b8") : "#475569" }}>
                    {a === normalizedAction && <span>● </span>}{a}
                  </div>
                  <div style={styles.actionCardDesc}>
                    {a === "Monitor"      && "No immediate action needed. Keep an eye on the system and check again if it worsens."}
                    {a === "Maintain"     && "Run standard maintenance steps. The issue is likely caused by software or driver-level buildup that maintenance can address."}
                    {a === "Troubleshoot" && "Investigate further using Windows diagnostic tools. A specific driver or configuration is likely causing the problem."}
                    {a === "Escalate"     && "Seek professional hardware inspection. The probable cause may involve physical component failure."}
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Plain-language explanation */}
          <div style={styles.twoCol}>
            <div style={styles.card}>
              <div style={styles.sectionTitle}>□ Plain-Language Explanation</div>
              <p style={styles.explanationText}>
                {result.ai_explanation ?? "Explanation unavailable. Please follow the recommended next steps below."}
              </p>
            </div>
            <div style={styles.card}>
              <div style={styles.sectionTitle}>↗ Why This Result Was Given</div>
              <p style={{ fontSize: 12, color: "#64748b", lineHeight: 1.7 }}>
                RigMD mapped your answers against your system profile using the following factors. Each factor contributes to the probable cause and action category.
              </p>
              <div style={{ marginTop: 10, display: "flex", flexDirection: "column", gap: 6 }}>
                {[
                  { label: "Symptom", value: result.symptom_type, tag: "PRIMARY" },
                  result.affected_activity && { label: "Affected Activity", value: result.affected_activity, tag: "PRIMARY" },
                  result.warning_signs && { label: "Warning Signs", value: result.warning_signs, tag: "SECONDARY" },
                  result.recent_changes && { label: "Recent Changes", value: result.recent_changes, tag: "CONTRIBUTING" },
                ].filter(Boolean).map((f: any, i: number) => (
                  <div key={i} style={styles.reasonRow}>
                    <span style={styles.checkIcon}>✓</span>
                    <div>
                      <span style={{ color: "#94a3b8", fontSize: 12 }}>{f.label}: </span>
                      <span style={{ color: "#e2e8f0", fontSize: 12 }}>{f.value}</span>
                      &nbsp;
                      <span style={{
                        fontSize: 9,
                        fontWeight: 700,
                        letterSpacing: 0.6,
                        padding: "1px 5px",
                        borderRadius: 3,
                        background: f.tag === "PRIMARY" ? "rgba(59,130,246,0.2)" : "rgba(148,163,184,0.1)",
                        color: f.tag === "PRIMARY" ? "#60a5fa" : "#64748b",
                      }}>
                        {f.tag}
                      </span>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>

          {/* Warning signs */}
          {result.recommendations.length > 0 && (
            <div style={styles.card}>
              <div style={styles.sectionTitle}>⚠ Warning Signs Reference</div>
              <div style={{ overflowX: "auto" as const }}>
                <table style={styles.table}>
                  <thead>
                    <tr>
                      {["Warning Sign","Threshold","Recommended Action"].map(h => (
                        <th key={h} style={styles.th}>{h}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {result.recommendations.map(r => (
                      <tr key={r.id} style={{ borderBottom: "1px solid #1e293b" }}>
                        <td style={{ ...styles.td, color: "#f59e0b" }}>{r.warning_sign}</td>
                        <td style={styles.td}>{r.threshold}</td>
                        <td style={{ ...styles.td, color: "#94a3b8" }}>{r.recommended_action}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {remediationActions.length > 0 && (
  <div style={styles.card}>
    <div style={styles.sectionTitle}>
      ⚡ Automated Safe Fixes&nbsp;
      <span style={{ color: "#475569", fontSize: 11 }}>
        {remediationActions.length} action{remediationActions.length !== 1 ? "s" : ""} available
      </span>
    </div>
 
    <p style={{ fontSize: 12, color: "#475569", marginBottom: 14, marginTop: 0, lineHeight: 1.6 }}>
      These actions are safe, non-destructive, and reversible. RigMD will run them
      automatically — no manual steps required.
    </p>
 
    <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
      {remediationActions.map(action => {
        const result_  = actionResults[action.id];
        const isRunning = runningAction === action.id;
        const isDone    = !!result_;
 
        return (
          <div
            key={action.id}
            style={{
              border: `1px solid ${isDone && result_.success ? "rgba(34,197,94,0.35)" : isDone ? "rgba(239,68,68,0.35)" : "#1e293b"}`,
              borderRadius: 8,
              padding: "14px 16px",
              background: isDone && result_.success
                ? "rgba(34,197,94,0.05)"
                : isDone
                ? "rgba(239,68,68,0.05)"
                : "#0b1628",
              transition: "border-color 0.3s, background 0.3s",
            }}
          >
            {/* Top row: label + button */}
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 12 }}>
              <div style={{ flex: 1 }}>
                <div style={{ fontSize: 13, fontWeight: 600, color: "#e2e8f0", marginBottom: 4 }}>
                  {action.label}
                </div>
                <div style={{ fontSize: 12, color: "#64748b", lineHeight: 1.55 }}>
                  {action.description}
                </div>
                <div style={{ fontSize: 11, color: "#334155", marginTop: 5 }}>
                  🛡 {action.risk}
                </div>
              </div>
 
              {!isDone ? (
                <button
                  onClick={() => handleFixNow(action.id)}
                  disabled={isRunning || runningAction !== null}
                  style={{
                    padding: "8px 18px",
                    background: isRunning ? "#1e3a5f" : "#1d4ed8",
                    color: "#fff",
                    border: "none",
                    borderRadius: 6,
                    fontSize: 12,
                    fontWeight: 700,
                    cursor: isRunning || runningAction !== null ? "not-allowed" : "pointer",
                    whiteSpace: "nowrap",
                    opacity: runningAction !== null && !isRunning ? 0.5 : 1,
                    transition: "background 0.2s, opacity 0.2s",
                    minWidth: 90,
                  }}
                >
                  {isRunning ? "Running…" : "Fix Now"}
                </button>
              ) : (
                <div
                  style={{
                    padding: "6px 12px",
                    borderRadius: 6,
                    fontSize: 11,
                    fontWeight: 700,
                    background: result_.success ? "rgba(34,197,94,0.15)" : "rgba(239,68,68,0.15)",
                    color: result_.success ? "#4ade80" : "#f87171",
                    border: `1px solid ${result_.success ? "rgba(34,197,94,0.3)" : "rgba(239,68,68,0.3)"}`,
                    whiteSpace: "nowrap",
                  }}
                >
                  {result_.success ? "✓ Done" : "✗ Failed"}
                </div>
              )}
            </div>
 
            {/* Result feedback */}
            {isDone && (
              <div
                style={{
                  marginTop: 10,
                  padding: "10px 12px",
                  borderRadius: 6,
                  background: result_.success ? "rgba(34,197,94,0.07)" : "rgba(239,68,68,0.07)",
                  border: `1px solid ${result_.success ? "rgba(34,197,94,0.2)" : "rgba(239,68,68,0.2)"}`,
                  fontSize: 12,
                  color: result_.success ? "#86efac" : "#fca5a5",
                  lineHeight: 1.6,
                }}
              >
                <strong>Result: </strong>{result_.summary}
                {result_.cleared && (
                  <span style={{ color: "#4ade80", marginLeft: 8, fontWeight: 700 }}>
                    ({result_.cleared} freed)
                  </span>
                )}
              </div>
            )}
          </div>
        );
      })}
    </div>
  </div>
)}

          {/* Next steps */}
          <div style={styles.twoCol}>
            <div style={styles.card}>
              <div style={styles.sectionTitle}>≡ Recommended Next Steps&nbsp;
                <span style={{ color: "#475569", fontSize: 11 }}>{nextSteps.length} actions</span>
              </div>
              <div style={{ display: "flex", flexDirection: "column", gap: 12, marginTop: 8 }}>
                {nextSteps.map((step, i) => (
                  <div key={i} style={styles.stepRow}>
                    <div style={styles.stepNum}>{i + 1}</div>
                    <div style={{ flex: 1 }}>
                      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4 }}>
                        <span style={{ ...styles.priorityTag, ...(priorityStyle[step.priority] ?? {}) }}>
                          {step.priority}
                        </span>
                        <span style={{ fontSize: 13, fontWeight: 600, color: "#e2e8f0" }}>{step.text}</span>
                      </div>
                      <p style={{ margin: 0, fontSize: 12, color: "#64748b", lineHeight: 1.6 }}>{step.detail}</p>
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* Run another */}
            <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
              <div style={{ ...styles.card, textAlign: "center" as const }}>
                <div style={styles.sectionTitle}>Session Info</div>
                <div style={{ fontSize: 12, color: "#64748b", marginBottom: 12 }}>
                  {result.created_at ? new Date(result.created_at).toLocaleString() : "—"}
                </div>
                <div style={{ fontSize: 11, color: "#334155", marginBottom: 16 }}>
                  Session ID: {result.session_id.slice(0, 8)}…
                </div>
                <button onClick={onRunAnother} style={styles.runAnotherBtn}>
                  + Run Another Diagnosis
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

// ─── Styles ──────────────────────────────────────────────────────────────────

const styles: Record<string, React.CSSProperties> = {
  root: {
    minHeight: "100%",
    background: "#020c1b",
    color: "#e2e8f0",
    fontFamily: "'JetBrains Mono', monospace, sans-serif",
  },
  advisoryBanner: {
    padding: "10px 24px",
    background: "rgba(251,191,36,0.07)",
    borderBottom: "1px solid rgba(251,191,36,0.2)",
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
    border: "1px solid #1e293b",
    borderRadius: 10,
    padding: "18px 20px",
  },
  cardHeader: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    marginBottom: 16,
    paddingBottom: 12,
    borderBottom: "1px solid #1e293b",
  },
  dimLabel: { fontSize: 10, color: "#475569", letterSpacing: 1, textTransform: "uppercase" as const },
  dimValue: { fontSize: 13, color: "#94a3b8", marginTop: 2 },
  readyDot: { width: 10, height: 10, borderRadius: "50%" },
  resultGrid: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: 20,
    marginBottom: 20,
  },
  fieldLabel: { fontSize: 10, color: "#334155", letterSpacing: 1, textTransform: "uppercase" as const, marginBottom: 4 },
  fieldValue: { fontSize: 15, fontWeight: 600, color: "#e2e8f0" },
  fieldSub:   { fontSize: 11, color: "#475569", marginTop: 2 },
  recurringTag: {
    fontSize: 10,
    background: "rgba(251,191,36,0.15)",
    color: "#fcd34d",
    border: "1px solid rgba(251,191,36,0.3)",
    borderRadius: 4,
    padding: "2px 7px",
    marginRight: 6,
  },
  actionCards: {
    display: "grid",
    gridTemplateColumns: "repeat(4,1fr)",
    gap: 10,
  },
  actionCard: {
    border: "1.5px solid",
    borderRadius: 8,
    padding: "12px 14px",
    transition: "opacity 0.2s",
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
    gridTemplateColumns: "1fr 1fr",
    gap: 16,
  },
  sectionTitle: {
    fontSize: 12,
    fontWeight: 700,
    color: "#64748b",
    letterSpacing: 0.5,
    marginBottom: 12,
    paddingBottom: 8,
    borderBottom: "1px solid #1e293b",
  },
  explanationText: {
    fontSize: 13,
    color: "#94a3b8",
    lineHeight: 1.75,
    margin: 0,
    whiteSpace: "pre-line" as const,
  },
  reasonRow: {
    display: "flex",
    alignItems: "flex-start",
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
    borderCollapse: "collapse" as const,
    fontSize: 12,
  },
  th: {
    textAlign: "left" as const,
    padding: "8px 12px",
    fontSize: 10,
    color: "#475569",
    letterSpacing: 0.8,
    textTransform: "uppercase" as const,
    borderBottom: "1px solid #1e293b",
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
    alignItems: "flex-start",
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
    justifyContent: "center",
    flexShrink: 0,
  },
  priorityTag: {
    fontSize: 9,
    fontWeight: 700,
    letterSpacing: 0.8,
    padding: "2px 7px",
    borderRadius: 3,
    textTransform: "uppercase" as const,
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