import { useEffect, useState } from 'react';
import { motion } from 'motion/react';

interface Props {
  score: number; // 0–100
  size?: number;
}

function scoreColor(score: number) {
  if (score >= 75) return { stroke: '#2dd4bf', glow: 'rgba(45,212,191,0.25)', label: 'Good', labelClass: 'text-emerald-300' };
  if (score >= 50) return { stroke: '#fbbf24', glow: 'rgba(251,191,36,0.20)', label: 'Fair', labelClass: 'text-amber-300' };
  return { stroke: '#fb7185', glow: 'rgba(251,113,133,0.20)', label: 'Poor', labelClass: 'text-red-300' };
}

export default function HealthScoreRing({ score, size = 140 }: Props) {
  const strokeWidth = 10;
  const radius = (size - strokeWidth) / 2;
  const circumference = 2 * Math.PI * radius;
  const progress = Math.max(0, Math.min(100, score));
  const offset = circumference - (progress / 100) * circumference;
  const { stroke, glow, label, labelClass } = scoreColor(score);

  // Animated counter
  const [displayed, setDisplayed] = useState(0);
  useEffect(() => {
    const duration = 800;
    const start = performance.now();
    const from = displayed;
    let raf: number;
    const tick = (now: number) => {
      const elapsed = Math.min(now - start, duration);
      const t = elapsed / duration;
      const eased = 1 - Math.pow(1 - t, 3); // ease-out cubic
      setDisplayed(Math.round(from + (progress - from) * eased));
      if (elapsed < duration) raf = requestAnimationFrame(tick);
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [progress]);

  return (
    <div className="relative flex flex-col items-center justify-center" style={{ width: size, height: size }}>
      <svg
        width={size}
        height={size}
        viewBox={`0 0 ${size} ${size}`}
        className="rotate-[-90deg]"
        aria-hidden="true"
      >
        {/* Background track */}
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          stroke="rgba(125,162,199,0.10)"
          strokeWidth={strokeWidth}
        />
        {/* Glow filter */}
        <defs>
          <filter id="ring-glow" x="-40%" y="-40%" width="180%" height="180%">
            <feGaussianBlur in="SourceGraphic" stdDeviation="4" />
          </filter>
        </defs>
        {/* Glow layer */}
        <motion.circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          stroke={stroke}
          strokeWidth={strokeWidth + 4}
          strokeLinecap="round"
          strokeDasharray={circumference}
          initial={{ strokeDashoffset: circumference }}
          animate={{ strokeDashoffset: offset }}
          transition={{ duration: 1, ease: 'easeOut' }}
          filter="url(#ring-glow)"
          opacity={0.4}
        />
        {/* Main progress arc */}
        <motion.circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          stroke={stroke}
          strokeWidth={strokeWidth}
          strokeLinecap="round"
          strokeDasharray={circumference}
          initial={{ strokeDashoffset: circumference }}
          animate={{ strokeDashoffset: offset }}
          transition={{ duration: 1, ease: 'easeOut' }}
        />
      </svg>

      {/* Center content */}
      <div className="absolute inset-0 flex flex-col items-center justify-center">
        <span className="text-3xl font-bold tabular-nums text-white" style={{ filter: `drop-shadow(0 0 8px ${glow})` }}>
          {displayed}
        </span>
        <span className={`mt-0.5 text-xs font-semibold uppercase tracking-[0.18em] ${labelClass}`}>{label}</span>
      </div>
    </div>
  );
}
