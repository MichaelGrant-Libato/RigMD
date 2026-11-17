import { motion } from 'motion/react';
import { Gpu } from 'lucide-react';
import type { GpuAdapterReading, HardwareStats } from '../../types/rigmd';
import { gpuUsagePercent } from '../../lib/liveReadings';

export default function GpuCard({ stats }: { stats: HardwareStats | null }) {
  const adapters: GpuAdapterReading[] = stats?.gpu?.adapters?.length
    ? stats.gpu.adapters
    : [{
      id: 'unavailable',
      name: stats?.gpu?.name || 'Waiting for GPU readings',
      type: 'Unknown',
      usagePercent: null,
      capturedAtUtc: null,
    }];

  return (
    <motion.div
      whileHover={{ y: -2, scale: 1.01 }}
      transition={{ duration: 0.18 }}
      className="rigmd-card-surface flex min-w-0 flex-col justify-between rounded-xl border p-5 min-h-[166px]"
      data-testid="gpu-card"
    >
      <div className="flex items-center gap-2.5">
        <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-cyan-400/[0.08]">
          <Gpu size={16} className="text-cyan-300" />
        </div>
        <span className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-400">GPU</span>
      </div>

      <div className="mt-4 space-y-4">
        {adapters.map(adapter => {
          const usage = gpuUsagePercent(adapter);
          return (
            <div key={adapter.id} data-testid="gpu-adapter">
              <div className="flex items-baseline gap-1.5">
                <span className={`${adapters.length === 1 ? 'text-3xl' : 'text-2xl'} font-bold tabular-nums ${usage === null ? 'text-slate-400' : 'text-emerald-300'}`} data-testid="gpu-usage">
                  {usage === null ? '—' : `${Math.round(usage)}%`}
                </span>
                <span className="text-xs text-slate-500">activity</span>
              </div>
              <p className="mt-1.5 break-words text-xs text-slate-400" title={adapter.name}>{adapter.name}</p>
              <p className="mt-1 text-[11px] text-slate-400">
                {usage === null ? 'Usage unavailable' : adapter.type === 'Unknown' ? 'Windows GPU engine reading' : `${adapter.type} graphics`}
              </p>
              <div
                className="mt-3 h-1.5 w-full overflow-hidden rounded-full bg-white/[0.04]"
                role="meter"
                aria-label={`${adapter.name} activity`}
                aria-valuemin={0}
                aria-valuemax={100}
                aria-valuenow={usage ?? undefined}
                aria-valuetext={usage === null ? 'Usage unavailable' : `${Math.round(usage)} percent`}
              >
                <motion.div
                  className={`h-full rounded-full ${usage === null ? 'bg-slate-500' : 'bg-emerald-300'}`}
                  initial={{ width: 0 }}
                  animate={{ width: `${usage ?? 0}%` }}
                  transition={{ duration: 0.8, ease: 'easeOut' }}
                />
              </div>
            </div>
          );
        })}
      </div>
    </motion.div>
  );
}
