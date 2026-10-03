import { useEffect, useState } from 'react';
import {
  Bot,
  CheckCircle2,
  RefreshCw,
  Save,
  ShieldCheck,
  Wrench,
} from 'lucide-react';
import TopHeader from '../components/TopHeader';
import {
  getAgentSettings,
  getBackendErrorMessage,
  getRegisteredAgentTools,
  updateAgentSettings,
  type AgentSettingsResponse,
  type RegisteredAgentTool,
} from '../services/autonomyService';

export default function SettingsView() {
  const [settings, setSettings] = useState<AgentSettingsResponse | null>(null);
  const [tools, setTools] = useState<RegisteredAgentTool[]>([]);
  const [preferredMode, setPreferredMode] = useState<'auto' | 'local-only'>('auto');
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const loadData = async () => {
    setIsLoading(true);
    setErrorMessage(null);
    try {
      const [settingsData, toolsData] = await Promise.all([
        getAgentSettings(),
        getRegisteredAgentTools(),
      ]);
      setSettings(settingsData);
      setPreferredMode(
        settingsData.preferredMode === 'local-only' ? 'local-only' : 'auto',
      );
      setTools(toolsData);
    } catch (err) {
      setErrorMessage(getBackendErrorMessage(err));
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    void loadData();
  }, []);

  const handleSaveSettings = async () => {
    setIsSaving(true);
    setStatusMessage(null);
    setErrorMessage(null);

    try {
      const updated = await updateAgentSettings({
        preferredMode,
      });

      setSettings(updated);
      setPreferredMode(
        updated.preferredMode === 'local-only' ? 'local-only' : 'auto',
      );
      setStatusMessage('Settings saved on this PC.');
    } catch (err) {
      setErrorMessage(getBackendErrorMessage(err));
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <>
      <TopHeader
        title="Settings"
        subtitle="Choose whether RigMD may use Gemini for explanations and guided-action reasoning"
      />

      <div className="custom-scrollbar flex-1 overflow-y-auto px-6 py-6 lg:px-8">
        <div className="mx-auto flex w-full max-w-[1360px] flex-col gap-6 pb-10">
          {isLoading ? (
            <div className="flex items-center gap-3 rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-6 text-sm text-slate-300">
              <RefreshCw size={18} className="animate-spin text-cyan-300" />
              Loading local settings...
            </div>
          ) : (
            <>
              <div className="rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-6">
                <div className="flex flex-wrap items-center justify-between gap-4 border-b border-white/10 pb-4">
                  <div className="flex items-center gap-3">
                    <div className="flex h-10 w-10 items-center justify-center rounded-xl border border-cyan-400/35 bg-cyan-400/10 text-cyan-200">
                      <Bot size={22} />
                    </div>
                    <div>
                      <h2 className="text-base font-bold text-white">
                        AI Explanation &amp; Guided Action Mode
                      </h2>
                      <p className="text-xs text-cyan-300">
                        {settings?.activeEngine ||
                          'Local Deterministic Engine (100% Offline)'}
                      </p>
                    </div>
                  </div>

                  <div className="flex flex-wrap items-center gap-2 text-xs">
                    <span className="rounded-lg border border-emerald-400/30 bg-emerald-400/10 px-2.5 py-1 font-semibold text-emerald-200">
                      {settings?.registeredToolCount ?? tools.length} Registered Diagnostic &amp; Maintenance Tools
                    </span>
                  </div>
                </div>

                <p className="mt-4 text-sm leading-relaxed text-slate-300">
                  RigMD still reads device data from Windows on this PC. This setting only controls whether Gemini may help explain the result and plan a safe guided action when an issue needs review.
                </p>

                {/* Engine Mode Selection */}
                <div className="mt-5 grid grid-cols-1 gap-4 md:grid-cols-2">
                  <button
                    type="button"
                    onClick={() => setPreferredMode('auto')}
                    className={`rounded-xl border p-4 text-left transition ${
                      preferredMode === 'auto'
                        ? 'border-cyan-400 bg-cyan-400/10 text-white'
                        : 'border-white/10 bg-black/25 text-slate-300 hover:border-white/25'
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <span className="text-sm font-bold">
                        Use Gemini when available
                      </span>
                      {preferredMode === 'auto' && (
                        <CheckCircle2 size={16} className="text-cyan-300" />
                      )}
                    </div>
                    <p className="mt-1.5 text-xs leading-relaxed text-slate-300">
                      Uses RigMD's server-managed Gemini key for clearer explanations and guided-action planning. If Gemini is unavailable, offline, or rate-limited, RigMD falls back to the local engine.
                    </p>
                  </button>

                  <button
                    type="button"
                    onClick={() => setPreferredMode('local-only')}
                    className={`rounded-xl border p-4 text-left transition ${
                      preferredMode === 'local-only'
                        ? 'border-cyan-400 bg-cyan-400/10 text-white'
                        : 'border-white/10 bg-black/25 text-slate-300 hover:border-white/25'
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <span className="text-sm font-bold">
                        Local-only mode
                      </span>
                      {preferredMode === 'local-only' && (
                        <CheckCircle2 size={16} className="text-cyan-300" />
                      )}
                    </div>
                    <p className="mt-1.5 text-xs leading-relaxed text-slate-300">
                      Keeps explanations, diagnosis decisions, history checks, and controlled maintenance planning on this PC only.
                    </p>
                  </button>
                </div>

                <div className="mt-5 rounded-xl border border-white/10 bg-black/25 p-4">
                  <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                    <div className="flex items-center gap-2">
                      <ShieldCheck size={16} className="text-cyan-300" />
                      <span className="text-sm font-bold text-white">
                        Gemini Access
                      </span>
                    </div>

                    <span
                      className={`w-fit rounded-md border px-2 py-0.5 text-xs font-bold uppercase tracking-wider ${
                        settings?.hasGeminiApiKey
                          ? 'border-emerald-400/30 bg-emerald-400/10 text-emerald-200'
                          : 'border-slate-500/30 bg-slate-500/10 text-slate-300'
                      }`}
                    >
                      {settings?.hasGeminiApiKey
                        ? 'Configured by app'
                        : 'Offline fallback only'}
                    </span>
                  </div>

                  <p className="mt-1 text-xs text-slate-400">
                    Users do not enter API keys here. Gemini credentials are configured by the deployment/server environment, and RigMD automatically falls back to local mode when Gemini is unavailable.
                  </p>
                </div>

                <div className="mt-5 flex items-center justify-end gap-3">
                  <button
                    type="button"
                    onClick={() => handleSaveSettings()}
                    disabled={isSaving}
                    className="inline-flex items-center gap-2 rounded-lg bg-cyan-400 px-5 py-2.5 text-xs font-bold text-[#041014] transition hover:bg-cyan-300 disabled:opacity-50"
                  >
                    <Save size={15} />
                    {isSaving ? 'Saving...' : 'Save Settings'}
                  </button>
                </div>

                {statusMessage && (
                  <div className="mt-4 flex items-center gap-2 rounded-xl border border-emerald-400/35 bg-emerald-950/25 p-3 text-xs text-emerald-200">
                    <ShieldCheck size={16} className="shrink-0 text-emerald-400" />
                    <span>{statusMessage}</span>
                  </div>
                )}

                {errorMessage && (
                  <div className="mt-4 rounded-xl border border-red-400/35 bg-red-950/25 p-3 text-xs text-red-200">
                    {errorMessage}
                  </div>
                )}
              </div>

              {/* Registered Agent Tool Registry Inventory */}
              <div className="rounded-2xl border border-[var(--rigmd-border)] bg-[#101821] p-6">
                <div className="mb-4 flex items-center gap-2 border-b border-white/10 pb-3">
                  <Wrench size={18} className="text-cyan-300" />
                  <h3 className="text-sm font-bold uppercase tracking-wider text-white">
                    Registered Diagnostic &amp; Maintenance Tools ({tools.length})
                  </h3>
                </div>

                <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                  {tools.map((tool) => (
                    <div
                      key={tool.name}
                      className="rounded-xl border border-white/10 bg-black/25 p-3.5"
                    >
                      <div className="flex items-center justify-between gap-2">
                        <span className="font-mono text-xs font-bold text-cyan-200">
                          {tool.name}
                        </span>
                        <span className="rounded-md border border-white/15 bg-white/5 px-2 py-0.5 text-[10px] font-semibold text-slate-300">
                          {tool.safetyTier}
                        </span>
                      </div>
                      <p className="mt-1 text-xs font-semibold text-white">
                        {tool.displayName}
                      </p>
                      <p className="mt-1 text-xs text-slate-400">
                        {tool.description}
                      </p>
                    </div>
                  ))}
                </div>
              </div>
            </>
          )}
        </div>
      </div>
    </>
  );
}
