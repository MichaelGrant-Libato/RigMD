import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { RefreshCw } from 'lucide-react';
import { apiGet } from '../lib/api';
import { getApiErrorMessage } from '../lib/apiErrorMessage';
import { formatMeasurement, measurement } from '../lib/deviceTelemetry';

function DiagnosticPanel<T>({ title, path, button, description, children }: {
  title: string;
  path: string;
  button: string;
  description: string;
  children: (reading: T) => ReactNode;
}) {
  const label = useId();
  const request = useRef<AbortController | null>(null);
  const [reading, setReading] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => () => request.current?.abort(), []);

  async function collect() {
    if (request.current && !request.current.signal.aborted) return;
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    setReading(null);
    setError(null);
    try {
      const response = await apiGet<T>('/api/device-diagnostics/' + path, { signal: controller.signal, timeout: 18000 });
      if (!controller.signal.aborted) setReading(response.data);
    } catch (error) {
      if (!controller.signal.aborted) setError(getApiErrorMessage(error, 'The reading is unavailable. Try again shortly.'));
    } finally {
      if (request.current === controller) request.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  return (
    <section aria-labelledby={label} className="space-y-3 rounded-lg border border-[var(--rigmd-border)] bg-[#101821] p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 id={label} className="font-semibold text-white">{title}</h3>
          <p className="mt-1 text-sm text-slate-400">{description}</p>
        </div>
        <button type="button" className="inline-flex min-h-10 items-center gap-2 rounded-md border border-cyan-500/30 px-3 py-2 text-sm font-semibold text-cyan-200 disabled:opacity-50"
          disabled={busy} onClick={() => void collect()}>
          <RefreshCw aria-hidden="true" size={15} className={busy ? 'animate-spin' : ''} />
          {busy ? 'Collecting…' : reading ? 'Refresh reading' : button}
        </button>
      </div>
      {busy && <p className="sr-only" role="status">Collecting {title.toLowerCase()}.</p>}
      {error && <p role="alert" className="text-sm text-amber-200">{error}</p>}
      {reading && <div className="space-y-3">{children(reading)}</div>}
    </section>
  );
}

function observed(at?: string | null) {
  const time = Date.parse(at ?? '');
  return Number.isFinite(time) ? new Date(time).toLocaleString() : 'Not available';
}

interface AppRow { name: string; processes: number; cpuPercent?: number | null; workingSetBytes?: number | null }
interface AppActivity { capturedAt: string; seconds: number; partial: boolean; byCpu: AppRow[]; byMemory: AppRow[] }

export function AppActivityPanel({ metric, onMemoryCheck }: { metric: 'cpu' | 'memory'; onMemoryCheck?: () => void }) {
  return <DiagnosticPanel<AppActivity> title="App Activity" path="app-activity" button="Take a sample"
    description="See which running apps are using the most resources during a short sample.">
    {reading => {
      const rows = metric === 'cpu' ? reading.byCpu : reading.byMemory;
      return <>
        <p className="text-xs text-slate-400">{'Sampled ' + observed(reading.capturedAt) + ' · ' + reading.seconds.toFixed(1) + ' seconds'}</p>
        {reading.partial && <p className="text-sm text-slate-300">Some processes could only be measured in part.</p>}
        {rows.length ? <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-xs text-slate-400"><tr>
              <th scope="col" className="py-2">App or process</th>
              <th scope="col" className="py-2 text-right">CPU</th>
              <th scope="col" className="py-2 text-right">Memory in RAM</th>
            </tr></thead>
            <tbody>{rows.map(row => <tr key={row.name} className="border-t border-white/10 text-slate-200">
              <th scope="row" className="py-2 font-medium">{row.name}
                {row.processes > 1 && <span className="ml-2 text-xs text-slate-400">{row.processes} processes</span>}
              </th>
              <td className="py-2 text-right">{formatMeasurement(row.cpuPercent, '%', 1, 'Idle')}</td>
              <td className="py-2 text-right">{row.workingSetBytes == null ? 'Not available' : formatMeasurement(row.workingSetBytes / 1048576, ' MB', 1)}</td>
            </tr>)}</tbody>
          </table>
        </div> : <p className="text-sm text-slate-300">No activity could be ranked for this sample.</p>}
        {metric === 'memory' && onMemoryCheck && <button type="button" className="min-h-10 text-sm font-semibold text-cyan-200 hover:underline"
          onClick={onMemoryCheck}>Run a memory check to review suggested actions</button>}
      </>;
    }}
  </DiagnosticPanel>;
}

interface Connection { capturedAt: string; adapter?: string | null; steps: Array<{
  name: string; state: string; detail: string; milliseconds?: number | null;
}> }

export function ConnectionTestPanel() {
  return <DiagnosticPanel<Connection> title="Connection Test" path="connection" button="Run connection test"
    description="Check the Windows route, gateway, DNS, and a direct HTTPS request to Microsoft.">
    {reading => <>
      <p className="text-xs text-slate-400">{observed(reading.capturedAt)}{reading.adapter && ' · ' + reading.adapter}</p>
      <ul className="divide-y divide-white/10">
        {reading.steps.map(step => <li key={step.name} className="py-2 text-sm">
          <div className="flex flex-wrap justify-between gap-2">
            <span className="font-semibold text-white">{step.name}</span>
            <span className={step.state === 'passed' ? 'text-emerald-300' : 'capitalize text-slate-300'}>
              {step.state}{step.milliseconds != null && ' · ' + formatMeasurement(step.milliseconds, ' ms', 0, '<1 ms')}
            </span>
          </div>
          <p className="mt-1 text-slate-400">{step.detail}</p>
        </li>)}
      </ul>
    </>}
  </DiagnosticPanel>;
}

interface DriveHealth {
  state: string; capturedAt: string; model?: string | null; temperature?: number | null;
  wearPercent?: number | null; powerOnHours?: number | null; uncorrectedReads?: number | null; uncorrectedWrites?: number | null;
}

function ReadingFields({ fields }: { fields: [string, string][] }) {
  return <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3">{fields.map(([name, value]) =>
    <div key={name}><dt className="text-xs text-slate-400">{name}</dt>
      <dd className="text-sm font-medium text-white">{value}</dd></div>)}</dl>;
}

export function DriveHealthPanel({ number, model, sessionId, volumes }: {
  number: number; model: string; sessionId?: string; volumes?: string[];
}) {
  const path = (sessionId ? 'checks/' + encodeURIComponent(sessionId) + '/' : '') + 'drives/' + number + '/health';
  return <DiagnosticPanel<DriveHealth> title={sessionId ? 'Drive Health · ' + model : 'Drive Health'} path={path} button="Read drive health"
    description={sessionId && volumes?.length
      ? 'Read current reliability counters for the physical drive that held ' + volumes.join(', ') + ' at the saved check.'
      : 'Read the reliability counters Windows exposes for this physical drive.'}>
    {reading => reading.state === 'permission denied'
      ? <p className="text-sm text-amber-200">Windows denied access to this drive’s reliability counters.</p>
      : reading.state === 'drive changed'
        ? <p className="text-sm text-amber-200">This drive no longer matches the one in the saved check. Open Storage in My Device Info to view the current drives.</p>
      : reading.state !== 'measured'
        ? <p className="text-sm text-slate-300">Windows did not expose reliability counters for this drive.</p>
        : <>
          <p className="text-xs text-slate-400">{(reading.model || model) + ' · ' + observed(reading.capturedAt)}</p>
          <ReadingFields fields={[
            ['Temperature', measurement(reading.temperature) === null ? 'Not available' : reading.temperature + '°C'],
            ['Wear used', formatMeasurement(reading.wearPercent, '%', 0, 'None reported')],
            ['Power-on hours', formatMeasurement(reading.powerOnHours, ' h')],
            ['Uncorrected read errors', formatMeasurement(reading.uncorrectedReads)],
            ['Uncorrected write errors', formatMeasurement(reading.uncorrectedWrites)],
          ]} />
        </>}
  </DiagnosticPanel>;
}

interface BootHistory {
  state: string;
  records: Array<{ recordedAt: string; durationMs: number; mainPathMs?: number | null; postBootMs?: number | null }>;
}

export function BootHistoryPanel() {
  return <DiagnosticPanel<BootHistory> title="Boot History" path="boot-history" button="Read boot history"
    description="Review recent startup durations recorded by Windows on this PC.">
    {reading => <>
      {!reading.records.length && <p className="text-sm text-slate-300">{reading.state === 'permission denied'
        ? 'Windows denied access to the boot history.'
        : reading.state === 'no records'
          ? 'Windows returned no usable boot records.'
          : 'Windows boot history is unavailable.'}</p>}
      {reading.state === 'partial' && <p className="text-sm text-amber-200">Windows returned part of the boot history.</p>}
      {reading.records.length > 0 && <ul className="divide-y divide-white/10">
        {reading.records.map((row, index) => <li key={row.recordedAt + index} className="space-y-2 py-2">
          <p className="text-xs text-slate-400">{observed(row.recordedAt)}</p>
          <ReadingFields fields={[
            ['Boot duration', formatMeasurement(row.durationMs / 1000, ' s', 1)],
            ['Main startup', formatMeasurement(row.mainPathMs != null ? row.mainPathMs / 1000 : null, ' s', 1)],
            ['Post-boot', formatMeasurement(row.postBootMs != null ? row.postBootMs / 1000 : null, ' s', 1)],
          ]} />
        </li>)}
      </ul>}
      <p className="text-xs text-slate-400">Use Task Manager → Startup apps to review individual app impact.</p>
    </>}
  </DiagnosticPanel>;
}

interface BatteryHealth { state: string; capturedAt: string;
  batteries: Array<{ name: string; designMwh?: number | null; fullChargeMwh?: number | null; cycleCount?: number | null }>;
  recent: Array<{ at: string; remainingMwh?: number | null; drainedMwh?: number | null }>;
}

export function BatteryHealthPanel() {
  return <DiagnosticPanel<BatteryHealth> title="Battery Health" path="battery-health" button="Read battery report"
    description="Compare the battery’s original capacity with its current full-charge capacity and recent energy use on this PC.">
    {reading => reading.state !== 'measured' ? <p className="text-sm text-slate-300">Windows could not provide a battery report for this PC.</p> : <>
      <p className="text-xs text-slate-400">{observed(reading.capturedAt)}</p>
      <p className="text-xs text-slate-400">Charge is the energy available now. Full-charge capacity shows how much the battery can currently hold. Recent use shows recorded energy levels or discharge over time.</p>
      {reading.batteries.map((battery, index) => <div key={index} className="space-y-2">
        <h4 className="text-sm font-semibold text-white">{battery.name}</h4>
        <ReadingFields fields={[
          ['Design capacity', formatMeasurement(battery.designMwh, ' mWh')],
          ['Full-charge capacity', formatMeasurement(battery.fullChargeMwh, ' mWh')],
          ['Capacity retained', battery.designMwh && battery.fullChargeMwh
            ? formatMeasurement(battery.fullChargeMwh / battery.designMwh * 100, '%', 1) : 'Not available'],
          ['Cycles', formatMeasurement(battery.cycleCount)],
        ]} />
      </div>)}
      <details className="text-sm text-slate-300">
        <summary className="cursor-pointer py-2 font-medium">Recent use</summary>
        {reading.recent.length ? <ul className="space-y-2">{reading.recent.map((row, index) => <li key={index}>
          {observed(row.at)}{row.remainingMwh != null && ' · ' + formatMeasurement(row.remainingMwh, ' mWh remaining')}
          {row.drainedMwh != null && ' · ' + formatMeasurement(row.drainedMwh, ' mWh drained')}
        </li>)}</ul> : <p>Recent battery use was unavailable in the report.</p>}
      </details>
    </>}
  </DiagnosticPanel>;
}

interface Changes {
  since: string; checkedAt: string; limitedWindow: boolean;
  changes: Array<{ at: string; kind: string; detail: string }>;
  coverage: Array<{ source: string; state: string }>;
}

type WindowsEvent = Changes['changes'][number];

function WindowsEvents({ events }: { events: WindowsEvent[] }) {
  return <ul className="max-h-64 divide-y divide-white/10 overflow-y-auto">
    {events.map((event, index) => <li key={index} className="py-2 text-sm text-slate-200">
      <span className="font-medium">{event.kind}</span>{' · ' + observed(event.at)}
      <p className="mt-1 break-words text-slate-400">{event.detail}</p>
    </li>)}
  </ul>;
}

export function RecordedCrashEventsPanel({ sessionId }: { sessionId: string }) {
  return <DiagnosticPanel<{ since: string; until: string; coverage: Changes['coverage'][number]; events: WindowsEvent[] }>
    title="Crash Records" path={'checks/' + encodeURIComponent(sessionId) + '/crash-events'}
    button="Read crash records" description="Review the Windows events still available for the time window of this saved check.">
    {reading => <>
      <p className="text-xs text-slate-400">{observed(reading.since) + ' to ' + observed(reading.until)}</p>
      <p className="text-xs text-slate-400">{reading.coverage.source + ' log · ' + reading.coverage.state}</p>
      {reading.events.length > 0 ? <WindowsEvents events={reading.events} /> : <p className="text-sm text-slate-300">
        {reading.coverage.state === 'unavailable' ? 'Windows could not read the event log.' : 'No matching records were returned for this window.'}
      </p>}
    </>}
  </DiagnosticPanel>;
}

export function RecentChangesPanel({ sessionId }: { sessionId: string }) {
  return <DiagnosticPanel<Changes> key={sessionId} title="What Changed?" path={'changes/' + encodeURIComponent(sessionId)}
    button="Review changes" description="Look for updates, driver configuration, and crashes recorded since this check on this PC.">
    {reading => <>
      <p className="text-xs text-slate-400">{'Since ' + observed(reading.since) + ' · Checked ' + observed(reading.checkedAt)}
        {reading.limitedWindow && ' · Limited to 30 days'}</p>
      <p className="text-sm text-slate-300">Events near a problem give you a place to investigate. They do not establish its cause.</p>
      <details className="text-sm text-slate-300">
        <summary className="cursor-pointer py-1">Sources checked</summary>
        <ul>{reading.coverage.map(row => <li key={row.source}>{row.source} · {row.state}</li>)}</ul>
      </details>
      {reading.changes.length ? <WindowsEvents events={reading.changes} /> : <p className="text-sm text-slate-300">{reading.coverage.every(row => row.state === 'unavailable')
        ? 'The Windows activity sources were unavailable.' : 'The sources that responded returned no matching events.'}</p>}
    </>}
  </DiagnosticPanel>;
}
