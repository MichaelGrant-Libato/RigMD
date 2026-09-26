import { useEffect, useState } from 'react';
import {
  Bot,
  CheckCircle2,
  KeyRound,
  RefreshCw,
  Save,
  ShieldCheck,
  Trash2,
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
  const [autoExecuteSafeTier1, setAutoExecuteSafeTier1] = useState(false);
  const [apiKeyInput, setApiKeyInput] = useState('');
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
      setAutoExecuteSafeTier1(Boolean(settingsData.autoExecuteSafeTier1));
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

  const handleSaveSettings = async (clearKey = false) => {
    setIsSaving(true);
    setStatusMessage(null);
    setErrorMessage(null);

    try {
      const updated = await updateAgentSettings({
        preferredMode,
        autoExecuteSafeTier1,
        geminiApiKey: clearKey ? undefined : apiKeyInput.trim() || undefined,
        clearGeminiApiKey: clearKey,
      });

      setSettings(updated);
      setPreferredMode(
        updated.preferredMode === 'local-only' ? 'local-only' : 'auto',
      );
      setAutoExecuteSafeTier1(Boolean(updated.autoExecuteSafeTier1));
      if (clearKey || apiKeyInput.trim()) {
        setApiKeyInput('');
      }
      setStatusMessage(
        clearKey
          ? 'Local Gemini API key cleared. Using offline deterministic ReAct engine.'
          : 'AI Agent settings saved to %LocalAppData%\\RigMD\\agent-settings.json.',
      );
    } catch (err) {
      setErrorMessage(getBackendErrorMessage(err));
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="flex flex-col gap-6 pb-10">
      <TopHeader
        title="Settings & AI Agent Configuration"
        subtitle="Configure the ReAct Diagnostic Engine, Safe Auto-Pilot behavior, and local API credentials"
      />

      {isLoading ? (
        <div className="rigmd-card-surface flex items-center gap-3 border border-white/10 p-6 text-sm text-slate-300">
          <RefreshCw size={18} className="animate-spin text-cyan-300" />
          Loading AI Agent runtime configuration...
        </div>
      ) : (
        <>
          <div className="rigmd-card-surface border border-cyan-400/25 p-6">
            <div className="flex flex-wrap items-center justify-between gap-4 border-b border-white/10 pb-4">
              <div className="flex items-center gap-3">
                <div className="flex h-10 w-10 items-center justify-center border border-cyan-400/35 bg-cyan-400/10 text-cyan-200">
                  <Bot size={22} />
                </div>
                <div>
                  <h2 className="text-base font-bold text-white">
                    Active AI Reasoning Engine
                  </h2>
                  <p className="text-xs text-cyan-300">
                    {settings?.activeEngine ||
                      'Local Deterministic ReAct Engine (100% Offline)'}
                  </p>
                </div>
              </div>

              <div className="flex flex-wrap items-center gap-2 text-xs">
                <span className="border border-emerald-400/30 bg-emerald-400/10 px-2.5 py-1 font-semibold text-emerald-200">
                  {settings?.registeredToolCount ?? tools.length} Registered OS Tools
                </span>
                {settings?.settingsFilePath && (
                  <span className="border border-white/10 bg-black/30 px-2.5 py-1 font-mono text-[11px] text-slate-400">
                    {settings.settingsFilePath}
                  </span>
                )}
              </div>
            </div>

            {/* Engine Mode Selection */}
            <div className="mt-5 grid grid-cols-1 gap-4 md:grid-cols-2">
              <button
                type="button"
                onClick={() => setPreferredMode('auto')}
                className={`border p-4 text-left transition ${
                  preferredMode === 'auto'
                    ? 'border-cyan-400 bg-cyan-400/10 text-white'
                    : 'border-white/10 bg-black/25 text-slate-300 hover:border-white/25'
                }`}
              >
                <div className="flex items-center justify-between">
                  <span className="text-sm font-bold">
                    Auto (Gemini 3.5 Flash Cascade + Local ReAct Fallback)
                  </span>
                  {preferredMode === 'auto' && (
                    <CheckCircle2 size={16} className="text-cyan-300" />
                  )}
                </div>
                <p className="mt-1.5 text-xs text-slate-300">
                  Uses Gemini function-calling when a local API key is configured, and automatically falls back to the 100% offline deterministic C# ReAct engine if offline or rate-limited.
                </p>
              </button>

              <button
                type="button"
                onClick={() => setPreferredMode('local-only')}
                className={`border p-4 text-left transition ${
                  preferredMode === 'local-only'
                    ? 'border-cyan-400 bg-cyan-400/10 text-white'
                    : 'border-white/10 bg-black/25 text-slate-300 hover:border-white/25'
                }`}
              >
                <div className="flex items-center justify-between">
                  <span className="text-sm font-bold">
                    100% Offline Local Deterministic ReAct Engine
                  </span>
                  {preferredMode === 'local-only' && (
                    <CheckCircle2 size={16} className="text-cyan-300" />
                  )}
                </div>
                <p className="mt-1.5 text-xs text-slate-300">
                  Strictly offline execution. Never sends telemetry to any cloud LLM; executes all 18 diagnostic, historical memory, and remediation tools locally on this PC.
                </p>
              </button>
            </div>

            {/* Safe Auto-Pilot Toggle */}
            <div className="mt-5 border border-white/10 bg-black/25 p-4">
              <label className="flex cursor-pointer items-start gap-3">
                <input
                  type="checkbox"
                  checked={autoExecuteSafeTier1}
                  onChange={(e) => setAutoExecuteSafeTier1(e.target.checked)}
                  className="mt-1 h-4 w-4 accent-cyan-400"
                />
                <div>
                  <span className="text-sm font-bold text-white">
                    Enable Safe Tier-1 Auto-Pilot by Default
                  </span>
                  <p className="mt-1 text-xs text-slate-300">
                    When enabled, the Autonomous Doctor will automatically run non-destructive Tier-1 maintenance tools (<code className="text-cyan-200">clear_temp_files</code>, <code className="text-cyan-200">clear_browser_cache</code>, <code className="text-cyan-200">flush_dns_cache</code>) and verify recovery immediately. Tier-2 actions always require explicit user confirmation.
                  </p>
                </div>
              </label>
            </div>

            {/* Optional Local Gemini API Key */}
            <div className="mt-5 border border-white/10 bg-black/25 p-4">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2">
                  <KeyRound size={16} className="text-cyan-300" />
                  <span className="text-sm font-bold text-white">
                    Optional Local Gemini API Key (Stored Only on This PC)
                  </span>
                </div>
                {settings?.hasGeminiApiKey && (
                  <span className="border border-emerald-400/30 bg-emerald-400/10 px-2 py-0.5 font-mono text-xs text-emerald-200">
                    Configured: {settings.maskedGeminiApiKey}
                  </span>
                )}
              </div>

              <p className="mt-1 text-xs text-slate-400">
                Saved strictly to <code className="text-slate-300">%LocalAppData%\RigMD\agent-settings.json</code>. Never bundled in installers or committed to Git.
              </p>

              <div className="mt-3 flex flex-col gap-3 sm:flex-row">
                <input
                  type="password"
                  value={apiKeyInput}
                  onChange={(e) => setApiKeyInput(e.target.value)}
                  placeholder={
                    settings?.hasGeminiApiKey
                      ? 'Enter a new Gemini API key to replace existing key...'
                      : 'AIzaSy... (Leave blank to use 100% free offline ReAct engine)'
                  }
                  className="flex-1 border border-white/15 bg-black/40 px-3.5 py-2 text-sm text-white placeholder-slate-500 focus:border-cyan-400 focus:outline-none"
                />

                {settings?.hasGeminiApiKey && (
                  <button
                    type="button"
                    onClick={() => handleSaveSettings(true)}
                    disabled={isSaving}
                    className="inline-flex items-center justify-center gap-1.5 border border-red-400/40 bg-red-950/30 px-3.5 py-2 text-xs font-semibold text-red-200 transition hover:bg-red-950/50 disabled:opacity-50"
                  >
                    <Trash2 size={14} />
                    Clear Key
                  </button>
                )}
              </div>
            </div>

            <div className="mt-5 flex items-center justify-end gap-3">
              <button
                type="button"
                onClick={() => handleSaveSettings(false)}
                disabled={isSaving}
                className="inline-flex items-center gap-2 border border-cyan-400/50 bg-cyan-500/20 px-5 py-2.5 text-xs font-bold text-cyan-100 transition hover:bg-cyan-500/30 disabled:opacity-50"
              >
                <Save size={15} />
                {isSaving ? 'Saving...' : 'Save Agent Settings'}
              </button>
            </div>

            {statusMessage && (
              <div className="mt-4 flex items-center gap-2 border border-emerald-400/35 bg-emerald-950/25 p-3 text-xs text-emerald-200">
                <ShieldCheck size={16} className="shrink-0 text-emerald-400" />
                <span>{statusMessage}</span>
              </div>
            )}

            {errorMessage && (
              <div className="mt-4 border border-red-400/35 bg-red-950/25 p-3 text-xs text-red-200">
                {errorMessage}
              </div>
            )}
          </div>

          {/* Registered Agent Tool Registry Inventory */}
          <div className="rigmd-card-surface border border-white/10 p-6">
            <div className="mb-4 flex items-center gap-2 border-b border-white/10 pb-3">
              <Wrench size={18} className="text-cyan-300" />
              <h3 className="text-sm font-bold uppercase tracking-wider text-white">
                Registered Agent Tools ({tools.length})
              </h3>
            </div>

            <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
              {tools.map((tool) => (
                <div
                  key={tool.name}
                  className="border border-white/10 bg-black/25 p-3.5"
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-mono text-xs font-bold text-cyan-200">
                      {tool.name}
                    </span>
                    <span className="border border-white/15 bg-white/5 px-2 py-0.5 text-[10px] font-semibold text-slate-300">
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
  );
}
