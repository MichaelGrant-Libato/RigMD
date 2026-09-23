interface Props {
  label: string;
  variant: 'ok' | 'warning' | 'critical' | 'neutral';
}

const styles: Record<Props['variant'], string> = {
  ok: 'border-emerald-400/30 bg-emerald-400/[0.06] text-emerald-300',
  warning: 'border-amber-400/30 bg-amber-400/[0.06] text-amber-300',
  critical: 'border-red-400/30 bg-red-400/[0.06] text-red-300',
  neutral: 'border-slate-500/30 bg-slate-500/[0.06] text-slate-300',
};

const dots: Record<Props['variant'], string> = {
  ok: 'bg-emerald-400',
  warning: 'bg-amber-400',
  critical: 'bg-red-400',
  neutral: 'bg-slate-400',
};

export default function StatusPill({ label, variant }: Props) {
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] font-semibold tracking-wider ${styles[variant]}`}>
      <span className={`h-1.5 w-1.5 rounded-full ${dots[variant]}`} />
      {label}
    </span>
  );
}
