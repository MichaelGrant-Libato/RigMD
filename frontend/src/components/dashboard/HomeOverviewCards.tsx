import type { ReactNode } from 'react';
import { Cpu, MemoryStick, HardDrive, Network } from 'lucide-react';
import type { HomeOverview } from '../../lib/homeOverview';
import { formatStorageCapacity } from '../../lib/storageTelemetry';
import { UsageBar, MemoryGauge } from './LiveUsageIndicators';
import { REVIEW_THRESHOLDS } from '../../lib/reviewThresholds';

function Card({ title, icon, children }: { title: string; icon: ReactNode; children: ReactNode }) {
  return <div className="rigmd-card-surface min-h-[166px] rounded-xl border p-5"><div className="flex items-center gap-3 text-cyan-300"><span aria-hidden="true" className="flex h-12 w-12 shrink-0 items-center justify-center rounded-lg border border-cyan-400/25 bg-cyan-400/10">{icon}</span><h3 className="text-xs font-bold uppercase tracking-wider text-slate-300">{title}</h3></div><div className="mt-4 space-y-2">{children}</div></div>;
}
export default function HomeOverviewCards({ overview, error }: { overview: HomeOverview | null; error?: string | null }) {
  const empty = error ? 'Not available' : 'Loading readings…';
  const cpu = overview?.cpu;
  const memory = overview?.memory;
  const network = overview?.network;
  const color = (value: number | null | undefined, threshold: number) => value == null ? 'text-slate-400' : value >= threshold ? 'text-amber-300' : 'text-white';
  return <>
    {error && <p role="alert" className="text-sm text-amber-300">{error}</p>}
    <section className="grid gap-4 md:grid-cols-2 xl:grid-cols-4" aria-label="Essential device readings">
      <Card title="CPU activity" icon={<Cpu size={24} className="shrink-0" />}>
        <p className={`text-3xl font-bold tabular-nums ${color(cpu?.usagePercent, REVIEW_THRESHOLDS.cpu)}`}>{cpu?.usagePercent != null ? `${Math.round(cpu.usagePercent)}%` : cpu?.state === 'warming' ? 'Measuring…' : cpu ? 'Not available' : empty}</p>
        <p className="truncate text-xs text-slate-400">{cpu?.name ?? 'Processor information pending'}</p>
        <p className="text-xs text-slate-400" title={cpu?.source}>{cpu?.windowSeconds ? `Average over ${cpu.windowSeconds.toFixed(1)} s` : 'A real sampling interval is needed.'}</p>
        <UsageBar label="CPU activity" value={cpu?.usagePercent} warningAt={REVIEW_THRESHOLDS.cpu} />
        <p className="text-xs text-slate-500">Temperature is checked in device details and device checks.</p>
      </Card>
      <Card title="Memory in use" icon={<MemoryStick size={24} className="shrink-0" />}>
        <div className="flex items-center gap-4"><MemoryGauge value={memory?.usagePercent} warningAt={REVIEW_THRESHOLDS.memory} />
          <div className="min-w-0"><p className="text-sm text-slate-300">{memory?.usedGb != null && memory.totalGb != null ? `${memory.usedGb.toFixed(1)} / ${memory.totalGb.toFixed(1)} GB` : overview ? 'Not available' : empty}</p>
            <p className="mt-1 text-xs text-slate-400">{memory?.usagePercent == null ? 'Usage reading unavailable' : memory.usagePercent >= REVIEW_THRESHOLDS.memory ? 'Above the review threshold' : 'Below the review threshold'}</p></div>
        </div>
        <p className="text-xs text-slate-500">Current Windows physical-memory usage.</p>
      </Card>
      <Card title="Local file space" icon={<HardDrive size={24} className="shrink-0" />}>
        {!overview ? <p className="text-slate-400">{empty}</p> : !overview.storageQuerySucceeded || !overview.volumes.length ? <p className="text-slate-400">Not available</p> : <ul className="space-y-2">{overview.volumes.map(volume => <li key={volume.drive}><p className={`text-sm font-semibold ${color(volume.usagePercent, REVIEW_THRESHOLDS.storage)}`}>{volume.drive} {formatStorageCapacity(volume.usedGb, volume.totalGb)}</p><p className="text-xs text-slate-500">{volume.usagePercent === null ? 'Usage could not be read' : `${volume.usagePercent.toFixed(1)}% used`}</p><UsageBar label={`${volume.drive} space used`} value={volume.usagePercent} warningAt={REVIEW_THRESHOLDS.storage} /></li>)}</ul>}
        <p className="text-xs text-slate-500">Mounted fixed-volume space; not a drive-health test.</p>
      </Card>
      <Card title="Network link" icon={<Network size={24} className="shrink-0" />}>
        <p className="text-lg font-semibold text-white">{!network ? empty : !network.querySucceeded ? 'Link information unavailable' : network.hasActiveAdapter === false ? 'No active network adapter' : network.kind === 'wireless' ? 'Wi-Fi adapter detected' : network.kind === 'wired' ? 'Ethernet adapter detected' : 'Network adapter detected'}</p>
        <p className="break-all text-xs text-slate-400">{network?.ipAddress ?? (network?.hasActiveAdapter ? 'No IPv4 address reported' : 'No IPv4 address to display')}</p>
        <ul className="mt-3 flex flex-wrap gap-x-3 gap-y-2" aria-label="Network configuration indicators">
          {([{ label: 'Adapter', value: network?.hasActiveAdapter }, { label: 'IPv4', value: network?.hasIpv4Address }, { label: 'Gateway', value: network?.hasDefaultGateway }, { label: 'DNS setup', value: network?.hasDnsServers }]).map(item => <li key={item.label} className="inline-flex items-center gap-1.5 text-[11px] text-slate-400">
            <span aria-hidden="true" className={`h-2 w-2 rounded-full transition-colors ${network?.querySucceeded && item.value === true ? 'bg-cyan-400' : network?.querySucceeded && item.value === false ? 'bg-amber-400' : 'bg-slate-500'}`} />
            <span>{item.label}: {network?.querySucceeded && item.value != null ? item.value ? 'present' : 'absent' : 'unknown'}</span>
          </li>)}
        </ul>
        <p className="text-xs text-slate-500">Run a network check to measure DNS, latency and packet loss.</p>
      </Card>
    </section>
    <p className="text-xs text-slate-400">Home shows essential readings. Open My Device Info for the full hardware inventory. {overview && `Captured ${new Date(overview.capturedAt).toLocaleTimeString()}.`}</p>
  </>;
}
