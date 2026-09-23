import { motion } from 'motion/react';
import type { HardwareStats } from '../../types/rigmd';
import { Cpu, MemoryStick, HardDrive, Wifi, Cable, Network, Thermometer } from 'lucide-react';

/* ─── CPU Card ─── */
export function CpuCard({ stats }: { stats: HardwareStats | null }) {
  const usage = stats?.cpu?.usage_percent ?? null;
  const temp = stats?.cpu?.temperature_celsius ?? null;
  const name = stats?.cpu?.name ?? 'CPU';
  const shortName = name.replace(/\(R\)|\(TM\)|CPU|@.*$/gi, '').trim();

  const usageColor = usage === null ? 'text-slate-400'
    : usage >= 90 ? 'text-red-300'
    : usage >= 70 ? 'text-amber-300'
    : 'text-emerald-300';

  return (
    <motion.div
      whileHover={{ y: -2, scale: 1.01 }}
      transition={{ duration: 0.18 }}
      className="rigmd-card-surface flex flex-col justify-between rounded-xl border p-5 min-h-[166px]"
    >
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2.5">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-cyan-400/[0.08]">
            <Cpu size={16} className="text-cyan-300" />
          </div>
          <span className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-400">CPU</span>
        </div>
        {temp !== null && (
          <span className="flex items-center gap-1 text-xs text-slate-400">
            <Thermometer size={12} />
            {Math.round(temp)}°C
          </span>
        )}
      </div>
      <div className="mt-4">
        <div className="flex items-baseline gap-1.5">
          <span className={`text-3xl font-bold tabular-nums ${usageColor}`}>
            {usage !== null ? `${Math.round(usage)}%` : '—'}
          </span>
          <span className="text-xs text-slate-500">usage</span>
        </div>
        <p className="mt-1.5 truncate text-xs text-slate-400">{shortName}</p>
      </div>
      {/* Mini usage bar */}
      <div className="mt-3 h-1.5 w-full overflow-hidden rounded-full bg-white/[0.04]">
        <motion.div
          className="h-full rounded-full"
          style={{ backgroundColor: usage === null ? '#64748b' : usage >= 90 ? '#fb7185' : usage >= 70 ? '#fbbf24' : '#34d399' }}
          initial={{ width: 0 }}
          animate={{ width: `${usage ?? 0}%` }}
          transition={{ duration: 0.8, ease: 'easeOut' }}
        />
      </div>
    </motion.div>
  );
}

/* ─── Memory Card (Mini Ring) ─── */
export function MemoryCard({ stats }: { stats: HardwareStats | null }) {
  const usage = stats?.ram?.usage_percent ?? null;
  const used = stats?.ram?.used_gb ?? null;
  const total = stats?.ram?.total_gb ?? null;

  const ringSize = 64;
  const sw = 6;
  const r = (ringSize - sw) / 2;
  const circ = 2 * Math.PI * r;
  const offset = usage !== null ? circ - (usage / 100) * circ : circ;

  const color = usage === null ? '#64748b' : usage >= 90 ? '#fb7185' : usage >= 75 ? '#fbbf24' : '#34d399';
  const colorClass = usage === null ? 'text-slate-400' : usage >= 90 ? 'text-red-300' : usage >= 75 ? 'text-amber-300' : 'text-emerald-300';

  return (
    <motion.div
      whileHover={{ y: -2, scale: 1.01 }}
      transition={{ duration: 0.18 }}
      className="rigmd-card-surface flex items-center gap-5 rounded-xl border p-5 min-h-[166px]"
    >
      {/* Ring */}
      <div className="relative flex shrink-0 items-center justify-center" style={{ width: ringSize, height: ringSize }}>
        <svg width={ringSize} height={ringSize} viewBox={`0 0 ${ringSize} ${ringSize}`} className="rotate-[-90deg]">
          <circle cx={ringSize / 2} cy={ringSize / 2} r={r} fill="none" stroke="rgba(125,162,199,0.10)" strokeWidth={sw} />
          <motion.circle
            cx={ringSize / 2} cy={ringSize / 2} r={r}
            fill="none" stroke={color} strokeWidth={sw} strokeLinecap="round"
            strokeDasharray={circ}
            initial={{ strokeDashoffset: circ }}
            animate={{ strokeDashoffset: offset }}
            transition={{ duration: 0.8, ease: 'easeOut' }}
          />
        </svg>
        <span className={`absolute text-sm font-bold ${colorClass}`}>
          {usage !== null ? `${Math.round(usage)}%` : '—'}
        </span>
      </div>
      <div className="min-w-0">
        <div className="flex items-center gap-2">
          <MemoryStick size={14} className="text-cyan-300" />
          <span className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-400">Memory</span>
        </div>
        <p className="mt-2 text-lg font-semibold text-white">
          {used !== null && total !== null ? `${used.toFixed(1)} / ${total.toFixed(0)} GB` : 'Not available'}
        </p>
        <p className="mt-0.5 text-xs text-slate-400">
          {usage === null ? 'Needs a refresh' : usage >= 90 ? 'Needs attention' : usage >= 75 ? 'Keep an eye on it' : 'Working normally'}
        </p>
      </div>
    </motion.div>
  );
}

/* ─── Storage Card (Per-drive bars) ─── */
export function StorageCard({ stats }: { stats: HardwareStats | null }) {
  const drives = stats?.storage_drives ?? [];
  const overallPercent = stats?.disk?.usage_percent ?? null;

  function barColor(percent: number) {
    if (percent >= 90) return '#fb7185';
    if (percent >= 75) return '#fbbf24';
    return '#34d399';
  }

  return (
    <motion.div
      whileHover={{ y: -2, scale: 1.01 }}
      transition={{ duration: 0.18 }}
      className="rigmd-card-surface flex flex-col justify-between rounded-xl border p-5 min-h-[166px]"
    >
      <div className="flex items-center gap-2.5">
        <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-cyan-400/[0.08]">
          <HardDrive size={16} className="text-cyan-300" />
        </div>
        <span className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-400">Storage</span>
      </div>

      {drives.length > 0 ? (
        <div className="mt-3 space-y-2.5">
          {drives.slice(0, 3).map((d, i) => {
            const pct = d.usage_percent ?? 0;
            return (
              <div key={i}>
                <div className="flex items-center justify-between">
                  <span className="max-w-[70%] truncate text-[11px] text-slate-300">{d.model || `Drive ${i + 1}`}</span>
                  <span className="text-[11px] font-semibold tabular-nums text-slate-300">{Math.round(pct)}%</span>
                </div>
                <div className="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-white/[0.04]">
                  <motion.div
                    className="h-full rounded-full"
                    style={{ backgroundColor: barColor(pct) }}
                    initial={{ width: 0 }}
                    animate={{ width: `${pct}%` }}
                    transition={{ duration: 0.8, delay: i * 0.1, ease: 'easeOut' }}
                  />
                </div>
              </div>
            );
          })}
        </div>
      ) : (
        <div className="mt-4">
          <span className="text-2xl font-bold tabular-nums text-white">
            {overallPercent !== null ? `${Math.round(overallPercent)}%` : '—'}
          </span>
          <span className="ml-1 text-xs text-slate-500">used</span>
          <div className="mt-2 h-1.5 w-full overflow-hidden rounded-full bg-white/[0.04]">
            <motion.div
              className="h-full rounded-full"
              style={{ backgroundColor: barColor(overallPercent ?? 0) }}
              initial={{ width: 0 }}
              animate={{ width: `${overallPercent ?? 0}%` }}
              transition={{ duration: 0.8, ease: 'easeOut' }}
            />
          </div>
        </div>
      )}
    </motion.div>
  );
}

/* ─── Network Card ─── */
export function NetworkCard({ stats }: { stats: HardwareStats | null }) {
  const net = stats?.network ?? null;
  const isWifi = net?.is_wifi ?? false;
  const latency = net?.ping_latency_ms ?? null;
  const signal = net?.wifi_signal_strength ?? null;
  const packetLoss = net?.packet_loss_percent ?? null;
  const ip = net?.ip_address ?? null;

  return (
    <motion.div
      whileHover={{ y: -2, scale: 1.01 }}
      transition={{ duration: 0.18 }}
      className="rigmd-card-surface flex flex-col justify-between rounded-xl border p-5 min-h-[166px]"
    >
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2.5">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-cyan-400/[0.08]">
            {net ? (
              isWifi ? (
                <Wifi size={16} className="text-cyan-300" />
              ) : (
                <Cable size={16} className="text-cyan-300" />
              )
            ) : (
              <Network size={16} className="text-slate-500" />
            )}
          </div>
          <span className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-400">Network</span>
        </div>

        {net ? (
          <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-400/20 bg-emerald-500/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-emerald-300">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-400 animate-pulse" />
            {isWifi ? 'Wireless' : 'Wired'}
          </span>
        ) : (
          <span className="inline-flex items-center rounded-full border border-slate-700 bg-slate-800/60 px-2 py-0.5 text-[10px] font-medium text-slate-400">
            No Link
          </span>
        )}
      </div>

      <div className="mt-4">
        <p className="text-lg font-semibold text-white">
          {net ? (isWifi ? 'Wi-Fi' : 'Ethernet') : 'Not available'}
        </p>

        <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1">
          {latency !== null && (
            <span className="flex items-center gap-1 text-xs">
              <span className="text-slate-400">Latency</span>
              <span className="font-semibold tabular-nums text-cyan-300">{Math.round(latency)}ms</span>
            </span>
          )}
          {isWifi && signal !== null && (
            <span className="flex items-center gap-1 text-xs">
              <span className="text-slate-400">Signal</span>
              <span className="font-semibold tabular-nums text-cyan-300">{signal}%</span>
            </span>
          )}
          {packetLoss !== null && (
            <span className="flex items-center gap-1 text-xs">
              <span className="text-slate-400">Packet Loss</span>
              <span className={`font-semibold tabular-nums ${packetLoss > 2 ? 'text-amber-300' : 'text-emerald-300'}`}>
                {Math.round(packetLoss)}%
              </span>
            </span>
          )}
          {!isWifi && !packetLoss && ip && (
            <span className="flex items-center gap-1 text-xs">
              <span className="text-slate-400">IP</span>
              <span className="font-mono text-[11px] text-slate-300">{ip}</span>
            </span>
          )}
        </div>

        {/* Link indicator */}
        {net ? (
          isWifi ? (
            /* Wi-Fi Signal indicator bars */
            <div className="mt-3 flex items-end gap-[3px]">
              {[25, 45, 65, 85].map((threshold, i) => {
                const strength = signal ?? 50;
                const active = strength >= threshold;
                return (
                  <motion.div
                    key={i}
                    className="w-[6px] rounded-sm"
                    style={{
                      height: `${8 + i * 4}px`,
                      backgroundColor: active ? '#2dd4bf' : 'rgba(125,162,199,0.12)',
                    }}
                    initial={{ scaleY: 0 }}
                    animate={{ scaleY: 1 }}
                    transition={{ duration: 0.4, delay: i * 0.08 }}
                  />
                );
              })}
            </div>
          ) : (
            /* Ethernet link bar */
            <div className="mt-3 flex items-center gap-2">
              <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-800">
                <motion.div
                  className="h-full rounded-full bg-gradient-to-r from-emerald-500 to-cyan-400"
                  initial={{ width: 0 }}
                  animate={{ width: '100%' }}
                  transition={{ duration: 0.6 }}
                />
              </div>
              <span className="text-[10px] font-medium text-emerald-400/90">Wired Link Active</span>
            </div>
          )
        ) : (
          <p className="mt-3 text-xs text-slate-500">No active network adapter detected</p>
        )}
      </div>
    </motion.div>
  );
}
