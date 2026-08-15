    import { useState } from "react";
import {
  SYMPTOM_TYPES,
  FREQUENCY_OPTIONS,
  SEVERITY_OPTIONS,
  DURATION_OPTIONS,
  AFFECTED_ACTIVITY_OPTIONS,
  RECENT_CHANGES_OPTIONS,
  SYSTEM_STATE_OPTIONS,
  WARNING_SIGNS_OPTIONS,
  type SymptomIntakePayload, 
  type DiagnosisResult,      
} from "../types/rigmd";

const API_BASE =
  import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5273";

    const STEPS = [
    { key: "symptom_type",      label: "Symptom Type",      hint: "What is your PC doing that brought you here?" },
    { key: "affected_activity", label: "Affected Activity",  hint: "Which activity is most affected?" },
    { key: "frequency",         label: "Frequency",          hint: "How often does this happen?" },
    { key: "severity",          label: "Severity",           hint: "How badly does this affect your work?" },
    { key: "duration",          label: "Duration",           hint: "How long has this been happening?" },
    { key: "recent_changes",    label: "Recent Changes",     hint: "Did you change anything before this started?" },
    { key: "system_state",      label: "System State",       hint: "What best describes your PC right now?" },
    { key: "warning_signs",     label: "Warning Signs",      hint: "Have you noticed any of these on your PC?" },
    ] as const;

    type StepKey = typeof STEPS[number]["key"];

    const STEP_OPTIONS: Record<StepKey, readonly string[]> = {
    symptom_type:      SYMPTOM_TYPES,
    affected_activity: AFFECTED_ACTIVITY_OPTIONS,
    frequency:         FREQUENCY_OPTIONS,
    severity:          SEVERITY_OPTIONS,
    duration:          DURATION_OPTIONS,
    recent_changes:    RECENT_CHANGES_OPTIONS,
    system_state:      SYSTEM_STATE_OPTIONS,
    warning_signs:     WARNING_SIGNS_OPTIONS,
    };

    // ─── Props ───────────────────────────────────────────────────────────────────

    interface Props {
    profileId: string;
    onResult: (result: DiagnosisResult) => void;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    function actionColor(action: string): string {
    const a = action.toLowerCase();
    if (a.includes("escalate"))   return "#ef4444";
    if (a.includes("troubleshoot")) return "#f59e0b";
    if (a.includes("maintain"))   return "#22c55e";
    return "#3b82f6";
    }

    function confidenceDot(label: string): string {
    if (label === "High")     return "#22c55e";
    if (label === "Moderate") return "#f59e0b";
    return "#64748b";
    }

    // ─── Component ───────────────────────────────────────────────────────────────

    export default function DiagnosticIntakeView({ profileId, onResult }: Props) {
    const [currentStep, setCurrentStep]   = useState(0);
    const [answers, setAnswers]           = useState<Partial<Record<StepKey, string>>>({});
    const [submitting, setSubmitting]     = useState(false);
    const [error, setError]               = useState<string | null>(null);

    const step     = STEPS[currentStep];
    const options  = STEP_OPTIONS[step.key];
    const selected = answers[step.key];
    const progress = Math.round(((currentStep) / STEPS.length) * 100);

    function select(value: string) {
        setAnswers(prev => ({ ...prev, [step.key]: value }));
    }

    function goBack() {
        if (currentStep > 0) setCurrentStep(s => s - 1);
    }

    function goNext() {
        if (!selected) return;
        if (currentStep < STEPS.length - 1) {
        setCurrentStep(s => s + 1);
        }
    }

    async function submit() {
        setSubmitting(true);
        setError(null);

        const payload: SymptomIntakePayload = {
        profile_id:        profileId,
        symptom_type:      answers.symptom_type      ?? "",
        affected_activity: answers.affected_activity,
        frequency:         answers.frequency         ?? "",
        severity:          answers.severity          ?? "",
        duration:          answers.duration,
        recent_changes:    answers.recent_changes,
        system_state:      answers.system_state,
        warning_signs:     answers.warning_signs,
        };

        try {
        const res = await fetch(`${API_BASE}/api/diagnosis/submit`, {
            method:  "POST",
            headers: { "Content-Type": "application/json" },
            body:    JSON.stringify(payload),
        });

        if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.detail ?? `Server error ${res.status}`);
        }

        const data: DiagnosisResult = await res.json();
        onResult(data);
        } catch (e: unknown) {
        setError(e instanceof Error ? e.message : "Submission failed. Please try again.");
        } finally {
        setSubmitting(false);
        }
    }

    const isLastStep = currentStep === STEPS.length - 1;

    // ── Render ────────────────────────────────────────────────────────────────

    return (
        <div style={styles.root}>

        {/* Header */}
        <div style={styles.header}>
            <div style={styles.headerTop}>
            <div>
                <div style={styles.pageLabel}>Guided Diagnostic Session</div>
                <div style={styles.pageSubLabel}>Step-by-step symptom collection — answer each question carefully</div>
            </div>
            <div style={styles.stepCounter}>
                Step {currentStep + 1} of {STEPS.length}
                <span style={{ ...styles.progressPill, width: `${progress}%` }} />
            </div>
            </div>

            {/* Progress bar */}
            <div style={styles.progressTrack}>
            <div style={{ ...styles.progressFill, width: `${progress}%` }} />
            </div>

            {/* Step indicators */}
            <div style={styles.stepDots}>
            {STEPS.map((s, i) => (
                <div
                key={s.key}
                title={s.label}
                style={{
                    ...styles.stepDot,
                    background: i < currentStep
                    ? "#3b82f6"
                    : i === currentStep
                        ? "#60a5fa"
                        : "#1e293b",
                    border: i === currentStep ? "2px solid #60a5fa" : "2px solid transparent",
                }}
                />
            ))}
            </div>
        </div>

        {/* Body */}
        <div style={styles.body}>

            {/* Left — step list */}
            <div style={styles.sidebar}>
            <div style={styles.sidebarTitle}>Diagnostic Steps</div>
            {STEPS.map((s, i) => (
                <div
                key={s.key}
                style={{
                    ...styles.sidebarItem,
                    opacity: i > currentStep ? 0.4 : 1,
                    color: i === currentStep ? "#e2e8f0" : "#94a3b8",
                    fontWeight: i === currentStep ? 600 : 400,
                }}
                >
                <div style={{
                    ...styles.sidebarDot,
                    background: i < currentStep ? "#3b82f6" : i === currentStep ? "#60a5fa" : "#334155",
                }} />
                {s.label}
                </div>
            ))}
            </div>

            {/* Center — question */}
            <div style={styles.questionArea}>
            <div style={styles.stepLabel}>Step {currentStep + 1} of {STEPS.length}</div>
            <h2 style={styles.question}>{step.hint}</h2>
            <div style={styles.hint}>
                <span style={{ color: "#60a5fa", marginRight: 6 }}>ⓘ</span>
                {step.key === "symptom_type" && "The symptom type is the starting point of your diagnosis. It determines which internal system components RigMD checks against your system profile."}
                {step.key === "affected_activity" && "Knowing which task triggers the problem helps narrow down which subsystem is under stress."}
                {step.key === "frequency" && "Frequency helps distinguish intermittent issues from consistent failures."}
                {step.key === "severity" && "Severity tells us how much this is affecting your ability to use your PC."}
                {step.key === "duration" && "Duration helps detect whether this is a new issue or a developing pattern."}
                {step.key === "recent_changes" && "Changes made before symptoms began are often the direct cause."}
                {step.key === "system_state" && "Knowing your PC's current state helps determine the urgency of the recommendation."}
                {step.key === "warning_signs" && "Observable warning signs are strong indicators of specific hardware or software conditions."}
            </div>

            <div style={styles.optionList}>
                {options.map(opt => (
                <button
                    key={opt}
                    onClick={() => select(opt)}
                    style={{
                    ...styles.optionBtn,
                    borderColor:    opt === selected ? "#3b82f6" : "#1e293b",
                    background:     opt === selected ? "rgba(59,130,246,0.12)" : "#0f172a",
                    color:          opt === selected ? "#e2e8f0" : "#94a3b8",
                    }}
                >
                    <span style={{
                    ...styles.radio,
                    borderColor:   opt === selected ? "#3b82f6" : "#334155",
                    background:    opt === selected ? "#3b82f6" : "transparent",
                    }} />
                    {opt}
                </button>
                ))}
            </div>

            {error && (
                <div style={styles.errorBanner}>{error}</div>
            )}

            {/* Nav controls */}
            <div style={styles.navRow}>
                <button onClick={goBack} disabled={currentStep === 0} style={styles.btnSecondary}>
                ← Back
                </button>
                <div style={{ color: "#475569", fontSize: 13 }}>{step.label}</div>
                {isLastStep ? (
                <button
                    onClick={submit}
                    disabled={!selected || submitting}
                    style={{ ...styles.btnPrimary, opacity: (!selected || submitting) ? 0.5 : 1 }}
                >
                    {submitting ? "Analyzing…" : "Submit Diagnosis →"}
                </button>
                ) : (
                <button
                    onClick={goNext}
                    disabled={!selected}
                    style={{ ...styles.btnPrimary, opacity: !selected ? 0.5 : 1 }}
                >
                    Next →
                </button>
                )}
            </div>
            </div>

            {/* Right — answer summary */}
            <div style={styles.summary}>
            <div style={styles.summaryTitle}>Your Answers</div>
            {STEPS.map(s => (
                <div key={s.key} style={styles.summaryRow}>
                <div style={styles.summaryKey}>{s.label.toUpperCase()}</div>
                <div style={styles.summaryVal}>{answers[s.key] ?? "—"}</div>
                </div>
            ))}
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
        display: "flex",
        flexDirection: "column",
    },
    header: {
        padding: "20px 28px 12px",
        borderBottom: "1px solid #1e293b",
    },
    headerTop: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "flex-start",
        marginBottom: 12,
    },
    pageLabel: {
        fontSize: 18,
        fontWeight: 700,
        color: "#f1f5f9",
        letterSpacing: "-0.5px",
    },
    pageSubLabel: {
        fontSize: 12,
        color: "#475569",
        marginTop: 2,
    },
    stepCounter: {
        fontSize: 12,
        color: "#64748b",
        position: "relative",
    },
    progressPill: {
        display: "block",
        height: 2,
        background: "#3b82f6",
        borderRadius: 1,
        marginTop: 4,
        transition: "width 0.3s ease",
    },
    progressTrack: {
        height: 3,
        background: "#1e293b",
        borderRadius: 2,
        overflow: "hidden",
        marginBottom: 10,
    },
    progressFill: {
        height: "100%",
        background: "linear-gradient(90deg, #1d4ed8, #3b82f6)",
        borderRadius: 2,
        transition: "width 0.4s ease",
    },
    stepDots: {
        display: "flex",
        gap: 6,
    },
    stepDot: {
        width: 10,
        height: 10,
        borderRadius: "50%",
        transition: "all 0.2s",
    },
    body: {
        flex: 1,
        display: "flex",
        gap: 0,
        overflow: "hidden",
    },
    sidebar: {
        width: 200,
        borderRight: "1px solid #1e293b",
        padding: "20px 16px",
        flexShrink: 0,
        overflowY: "auto",
    },
    sidebarTitle: {
        fontSize: 11,
        fontWeight: 700,
        color: "#475569",
        letterSpacing: 1,
        textTransform: "uppercase" as const,
        marginBottom: 12,
    },
    sidebarItem: {
        display: "flex",
        alignItems: "center",
        gap: 8,
        fontSize: 12,
        padding: "6px 0",
        transition: "all 0.2s",
    },
    sidebarDot: {
        width: 7,
        height: 7,
        borderRadius: "50%",
        flexShrink: 0,
    },
    questionArea: {
        flex: 1,
        padding: "28px 32px",
        overflowY: "auto",
    },
    stepLabel: {
        fontSize: 11,
        color: "#3b82f6",
        fontWeight: 600,
        letterSpacing: 1,
        textTransform: "uppercase" as const,
        marginBottom: 8,
    },
    question: {
        fontSize: 20,
        fontWeight: 700,
        color: "#f1f5f9",
        margin: "0 0 10px",
        letterSpacing: "-0.4px",
        lineHeight: 1.35,
    },
    hint: {
        fontSize: 12,
        color: "#64748b",
        lineHeight: 1.6,
        marginBottom: 24,
        padding: "10px 14px",
        background: "#0f172a",
        borderRadius: 6,
        border: "1px solid #1e293b",
    },
    optionList: {
        display: "flex",
        flexDirection: "column" as const,
        gap: 8,
        marginBottom: 32,
    },
    optionBtn: {
        display: "flex",
        alignItems: "center",
        gap: 12,
        padding: "12px 16px",
        border: "1.5px solid",
        borderRadius: 8,
        cursor: "pointer",
        fontSize: 13,
        textAlign: "left" as const,
        transition: "all 0.15s",
        lineHeight: 1.4,
    },
    radio: {
        width: 14,
        height: 14,
        borderRadius: "50%",
        border: "2px solid",
        flexShrink: 0,
        transition: "all 0.15s",
    },
    navRow: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        borderTop: "1px solid #1e293b",
        paddingTop: 20,
    },
    btnSecondary: {
        padding: "9px 18px",
        borderRadius: 6,
        border: "1px solid #1e293b",
        background: "transparent",
        color: "#94a3b8",
        fontSize: 13,
        cursor: "pointer",
    },
    btnPrimary: {
        padding: "9px 20px",
        borderRadius: 6,
        border: "none",
        background: "#1d4ed8",
        color: "#fff",
        fontSize: 13,
        fontWeight: 600,
        cursor: "pointer",
        transition: "opacity 0.2s",
    },
    errorBanner: {
        padding: "10px 14px",
        background: "rgba(239,68,68,0.12)",
        border: "1px solid rgba(239,68,68,0.3)",
        borderRadius: 6,
        color: "#fca5a5",
        fontSize: 13,
        marginBottom: 16,
    },
    summary: {
        width: 220,
        borderLeft: "1px solid #1e293b",
        padding: "20px 16px",
        flexShrink: 0,
        overflowY: "auto",
    },
    summaryTitle: {
        fontSize: 11,
        fontWeight: 700,
        color: "#475569",
        letterSpacing: 1,
        textTransform: "uppercase" as const,
        marginBottom: 14,
    },
    summaryRow: {
        marginBottom: 12,
    },
    summaryKey: {
        fontSize: 10,
        color: "#334155",
        letterSpacing: 0.8,
        fontWeight: 700,
        marginBottom: 2,
    },
    summaryVal: {
        fontSize: 12,
        color: "#94a3b8",
        wordBreak: "break-word" as const,
    },
    };