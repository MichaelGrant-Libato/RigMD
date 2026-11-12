import { motion, useReducedMotion } from 'motion/react';

function measured(value: number | null | undefined): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 100;
}
export function UsageBar({ value, label, warningAt }: { value: number | null | undefined; label: string; warningAt: number }) {
  const reduced = useReducedMotion();
  const available = measured(value);
  const color = !available ? '#64748b' : value >= warningAt ? '#fbbf24' : '#34d399';
  return <div role={available ? 'meter' : 'img'} aria-label={available ? label : `${label}: not available`}
    aria-valuemin={available ? 0 : undefined} aria-valuemax={available ? 100 : undefined} aria-valuenow={available ? value : undefined}
    aria-valuetext={available ? `${value.toFixed(1)}%` : undefined} className="mt-3 h-2 overflow-hidden rounded-full bg-slate-500/15">
    <motion.div aria-hidden="true" initial={false} animate={{ width: available ? `${value}%` : '0%' }} transition={{ duration: reduced ? 0 : 0.45, ease: 'easeOut' }} className="h-full rounded-full" style={{ backgroundColor: color }} />
  </div>;
}
export function MemoryGauge({ value, warningAt }: { value: number | null | undefined; warningAt: number }) {
  const reduced = useReducedMotion();
  const available = measured(value), size = 76, radius = 32, circumference = 2 * Math.PI * radius;
  const color = !available ? '#64748b' : value >= warningAt ? '#fbbf24' : '#34d399';
  return <div role={available ? 'meter' : 'img'} aria-label={available ? 'Memory usage' : 'Memory usage: not available'}
    aria-valuemin={available ? 0 : undefined} aria-valuemax={available ? 100 : undefined} aria-valuenow={available ? value : undefined}
    aria-valuetext={available ? `${value.toFixed(1)}%` : undefined} className="relative flex shrink-0 items-center justify-center" style={{ width: size, height: size }}>
    <svg aria-hidden="true" width={size} height={size} viewBox={`0 0 ${size} ${size}`} className="-rotate-90">
      <circle cx={size / 2} cy={size / 2} r={radius} fill="none" stroke="rgba(125,162,199,0.15)" strokeWidth={6} />
      <motion.circle initial={false} animate={{ strokeDashoffset: available ? circumference * (1 - value / 100) : circumference }} transition={{ duration: reduced ? 0 : 0.45, ease: 'easeOut' }}
        cx={size / 2} cy={size / 2} r={radius} fill="none" stroke={color} strokeWidth={6} strokeLinecap="round" strokeDasharray={circumference} />
    </svg>
    <span aria-hidden="true" className="absolute text-base font-bold tabular-nums" style={{ color }}>{available ? `${Math.round(value)}%` : '—'}</span>
  </div>;
}
