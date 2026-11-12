import { motion, useReducedMotion } from 'motion/react';

export default function SplashScreen() {
  const reducedMotion = useReducedMotion();
  return (
    <motion.div
      initial={{ opacity: 1 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
      transition={{ duration: reducedMotion ? 0 : 0.18 }}
      className="rigmd-startup"
      role="status" aria-label="Getting RigMD ready" aria-live="polite"
    >
      <div className="startup-shell">
        <div className="startup-hardware" aria-hidden="true">
          <svg className="startup-circuits" viewBox="0 0 320 156" focusable="false">
            <path d="M18 54 H62 L82 74 H116 M18 102 H62 L82 82 H116 M204 74 H238 L258 54 H302 M204 82 H238 L258 102 H302 M142 12 V34 M160 12 V34 M178 12 V34 M142 122 V144 M160 122 V144 M178 122 V144" />
            <rect x="116" y="34" width="88" height="88" rx="12" />
            <path d="M106 54 H116 M106 66 H116 M106 90 H116 M106 102 H116 M204 54 H214 M204 66 H214 M204 90 H214 M204 102 H214" />
          </svg>
          <img className="startup-logo" src="/rigmd-logo.png" alt="" width={64} height={64} />
        </div>
        <h1>RigMD</h1>
        <p className="startup-subtitle">Windows diagnostic support</p>
        <div className="startup-track" aria-hidden="true"><span className="startup-activity" /></div>
        <p className="startup-status">Starting RigMD…</p>
      </div>
    </motion.div>
  );
}
