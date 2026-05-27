import { useState, useEffect } from "react";
import type { SessionSummary } from "../types/rigmd";
import { ACTION_COLORS, CONFIDENCE_COLORS } from "../types/rigmd";

const API_BASE = "http://localhost:8000";

function normalizeAction(action: string): string {
  const a = action.toLowerCase();
  if (a.includes("escalate"))    return "Escalate";
  if (a.includes("troubleshoot")) return "Troubleshoot";
  if (a.includes("maintain"))    return "Maintain";
  return "Monitor";
}

interface Props {
  onViewSession?: (sessionId: string) => void;
}

export default function DiagnosticHistoryView({ onViewSession }: Props) {
  const [sessions, setSessions]     = useState<SessionSummary[]>([]);
  const [loading, setLoading]       = useState(true);
  const [error, setError]           = useState<string | null>(null);
  const [filter, setFilter]         = useState<string>("All Sessions");
  const [search, setSearch]         = useState("");
  const [recurringOnly, setRecurring] = useState(false);

  const filters = ["All Sessions", "Monitor", "Maintain", "Troubleshoot", "Escalate", "Recurring Only"];

  useEffect(() => {
    fetch(`${API_BASE}/api/diagnosis/sessions`)
      .then(r => {
        if (!r.ok) throw new Error(`Server returned status ${r.status}`);
        return r.json();
      })
      .then((data) => {
        // FIX: Extracting array from object-wrapper structure safely
        if (data && Array.isArray(data.sessions)) {
          setSessions(data.sessions);
        } else {
          console.error("Expected object wrapper array layout but got:", data);
          setError("Received unexpected data signature format.");
        }
        setLoading(false);
      })
      .catch(e => {
        console.error(e);
        setError("Could not load session history.");
        setLoading(false);
      });
  }, []);

  const filtered = sessions.filter(s => {
    const norm = normalizeAction(s.action_category || "");
    if (filter !== "All Sessions" && filter !== "Recurring Only" && norm !== filter) return false;
    if (filter === "Recurring Only" && !s.is_recurring) return false;
    if (recurringOnly && !s.is_recurring) return false;
    if (search && !s.symptom_type?.toLowerCase().includes(search.toLowerCase())
               && !s.diagnosed_category?.toLowerCase().includes(search.toLowerCase())) return false;
    return true;
  });

  const totalSessions  = sessions.length;
  const recurringCount = sessions.filter(s => s.is_recurring).length;
  const escalatedCount = sessions.filter(s => normalizeAction(s.action_category || "") === "Escalate").length;
  const flagCount      = sessions.filter(s => ["Troubleshoot","Escalate"].includes(normalizeAction(s.action_category || ""))).length;

  return (
    <div style={styles.root}>

      {/* Header */}
      <div style={styles.header}>
        <div>
          <div style={styles.pageTitle}>Diagnostic History</div>
          <div style={styles.pageSubTitle}>All past diagnostic sessions and their outcomes</div>
        </div>
      </div>

      {/* Summary metrics */}
      <div style={styles.metricsRow}>
        {[
          { label: "Total Sessions",    value: totalSessions  },
          { label: "Recurring Issues",  value: recurringCount },
          { label: "Escalated",         value: escalatedCount },
          { label: "Flagged Actions",   value: flagCount      },
        ].map(m => (
          <div key={m.label} style={styles.metricCard}>
            <div style={styles.metricVal}>{m.value}</div>
            <div style={styles.metricLabel}>{m.label}</div>
          </div>
        ))}
      </div>

      {/* Filters */}
      <div style={styles.filterRow}>
        <div style={styles.pills}>
          {filters.map(f => (
            <button
              key={f}
              onClick={() => setFilter(f)}
              style={{
                ...styles.pill,
                background:   f === filter ? "#1e3a5f" : "transparent",
                borderColor:  f === filter ? "#3b82f6" : "#1e293b",
                color:        f === filter ? "#60a5fa"  : "#64748b",
              }}
            >
              {f}
            </button>
          ))}
        </div>
        <input
          placeholder="Search symptoms or causes…"
          value={search}
          onChange={e => setSearch(e.target.value)}
          style={styles.searchInput}
        />
      </div>

      {/* Session count */}
      <div style={styles.countRow}>
        <span style={{ fontSize: 13, color: "#475569" }}>
          Sessions ({filtered.length})
        </span>
        <label style={{ display: "flex", alignItems: "center", gap: 6, fontSize: 12, color: "#64748b", cursor: "pointer" }}>
          <input
            type="checkbox"
            checked={recurringOnly}
            onChange={e => setRecurring(e.target.checked)}
            style={{ accentColor: "#3b82f6" }}
          />
          Newest first
        </label>
      </div>

      {/* Session list */}
      <div style={styles.list}>
        {loading && (
          <div style={styles.emptyState}>Loading session history…</div>
        )}
        {error && (
          <div style={{ ...styles.emptyState, color: "#fca5a5" }}>{error}</div>
        )}
        {!loading && !error && filtered.length === 0 && (
          <div style={styles.emptyState}>
            No sessions match this filter. Start a new diagnosis to build your history.
          </div>
        )}
        {!loading && !error && filtered.map(s => {
          const norm   = normalizeAction(s.action_category || "");
          const colors = ACTION_COLORS[norm] ?? ACTION_COLORS["Monitor"];
          const confColor = CONFIDENCE_COLORS[s.confidence_label || ""] ?? CONFIDENCE_COLORS["Low"];

          return (
            <div key={s.session_id} style={styles.sessionRow}>
              {/* Date */}
              <div style={styles.sessionDate}>
                <div style={{ fontSize: 13, color: "#94a3b8", fontWeight: 600 }}>
                  {s.display_date ?? "—"}
                </div>
                <div style={{ fontSize: 10, color: "#334155" }}>
                  {s.created_at ? new Date(s.created_at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : ""}
                </div>
              </div>

              {/* Content */}
              <div style={styles.sessionContent}>
                <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                  <span style={{ fontSize: 14, fontWeight: 600, color: "#e2e8f0" }}>{s.symptom_type}</span>
                  {s.is_recurring && (
                    <span style={styles.recurringTag}>↺ Recurring</span>
                  )}
                </div>
                <div style={{ fontSize: 12, color: "#64748b", marginTop: 3 }}>
                  {s.diagnosed_category}
                </div>
              </div>

              {/* Badges */}
              <div style={styles.sessionBadges}>
                <span
                  style={{
                    fontSize: 10,
                    fontWeight: 700,
                    letterSpacing: 0.6,
                    padding: "3px 9px",
                    borderRadius: 4,
                    border: "1px solid",
                    textTransform: "uppercase" as const,
                  }}
                  className={`${colors.bg} ${colors.text} ${colors.border}`}
                >
                  {norm}
                </span>
                <span style={{ fontSize: 11, fontWeight: 600 }} className={confColor}>
                  {(s.confidence_label || "LOW").toUpperCase()} CONFIDENCE
                </span>
                {onViewSession && s.session_id && (
                  <button
                    onClick={() => onViewSession(s.session_id)}
                    style={styles.detailBtn}
                  >
                    ⓘ Details
                  </button>
                )}
              </div>
            </div>
          );
        })}
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
    padding: "20px 24px",
  },
  header: {
    marginBottom: 20,
  },
  pageTitle: {
    fontSize: 20,
    fontWeight: 700,
    color: "#f1f5f9",
    letterSpacing: "-0.5px",
  },
  pageSubTitle: {
    fontSize: 12,
    color: "#475569",
    marginTop: 3,
  },
  metricsRow: {
    display: "grid",
    gridTemplateColumns: "repeat(4, 1fr)",
    gap: 12,
    marginBottom: 20,
  },
  metricCard: {
    background: "#0b1628",
    border: "1px solid #1e293b",
    borderRadius: 8,
    padding: "14px 16px",
  },
  metricVal: {
    fontSize: 26,
    fontWeight: 700,
    color: "#f1f5f9",
    letterSpacing: "-1px",
  },
  metricLabel: {
    fontSize: 11,
    color: "#475569",
    marginTop: 3,
    letterSpacing: 0.5,
  },
  filterRow: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
    marginBottom: 14,
  },
  pills: {
    display: "flex",
    gap: 6,
    flexWrap: "wrap" as const,
  },
  pill: {
    padding: "5px 13px",
    borderRadius: 20,
    border: "1px solid",
    fontSize: 12,
    cursor: "pointer",
    transition: "all 0.15s",
    background: "transparent",
  },
  searchInput: {
    padding: "7px 14px",
    borderRadius: 6,
    border: "1px solid #1e293b",
    background: "#0b1628",
    color: "#e2e8f0",
    fontSize: 12,
    outline: "none",
    width: 240,
  },
  countRow: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    marginBottom: 10,
  },
  list: {
    display: "flex",
    flexDirection: "column",
    gap: 2,
  },
  sessionRow: {
    display: "flex",
    alignItems: "center",
    gap: 16,
    padding: "14px 16px",
    background: "#0b1628",
    border: "1px solid #1e293b",
    borderRadius: 8,
    transition: "border-color 0.15s",
  },
  sessionDate: {
    width: 90,
    flexShrink: 0,
  },
  sessionContent: {
    flex: 1,
  },
  sessionBadges: {
    display: "flex",
    alignItems: "center",
    gap: 10,
    flexShrink: 0,
  },
  recurringTag: {
    fontSize: 10,
    background: "rgba(251,191,36,0.15)",
    color: "#fcd34d",
    border: "1px solid rgba(251,191,36,0.3)",
    borderRadius: 4,
    padding: "2px 7px",
    fontWeight: 600,
  },
  emptyState: {
    textAlign: "center" as const,
    padding: "40px 20px",
    color: "#475569",
    fontSize: 13,
  },
  detailBtn: {
    padding: "4px 12px",
    borderRadius: 5,
    border: "1px solid #1e293b",
    background: "transparent",
    color: "#64748b",
    fontSize: 11,
    cursor: "pointer",
  },
};