import { createContext, useContext, type ReactNode } from 'react';
import { Bell, Menu, Microchip, Monitor } from 'lucide-react';
import { motion } from 'motion/react';

export type LiveDataStatus = 'live' | 'syncing' | 'offline' | 'stale';

interface HeaderStatusContextValue {
  deviceName: string;
  liveStatus: LiveDataStatus;
  onMenuClick?: () => void;
}

const HeaderStatusContext = createContext<HeaderStatusContextValue>({
  deviceName: 'Detecting PC',
  liveStatus: 'syncing',
});

export function HeaderStatusProvider({
  children,
  deviceName,
  liveStatus,
  onMenuClick,
}: HeaderStatusContextValue & { children: ReactNode }) {
  return (
    <HeaderStatusContext.Provider value={{ deviceName, liveStatus, onMenuClick }}>
      {children}
    </HeaderStatusContext.Provider>
  );
}

interface TopHeaderProps {
  title: string;
  subtitle: string;
}

function getLiveStatusView(status: LiveDataStatus) {
  if (status === 'syncing') {
    return {
      label: 'Syncing',
      className: 'border-cyan-400/20 bg-cyan-400/10 text-cyan-300',
      iconClassName: 'text-cyan-300',
      dotClassName: 'bg-cyan-400 animate-pulse',
    };
  }

  if (status === 'offline') {
    return {
      label: 'Offline',
      className: 'border-red-400/20 bg-red-400/10 text-red-300',
      iconClassName: 'text-red-300',
      dotClassName: 'bg-red-400',
    };
  }

  if (status === 'stale') {
    return {
      label: 'Stale Data',
      className: 'border-amber-400/20 bg-amber-400/10 text-amber-300',
      iconClassName: 'text-amber-300',
      dotClassName: 'bg-amber-400',
    };
  }

  return {
      label: 'Live Data',
      className: 'border-emerald-400/20 bg-emerald-400/10 text-emerald-300',
      iconClassName: 'text-emerald-300',
      dotClassName: 'bg-emerald-400',
  };
}

export default function TopHeader({ title, subtitle }: TopHeaderProps) {
  const { deviceName, liveStatus, onMenuClick } = useContext(HeaderStatusContext);
  const statusView = getLiveStatusView(liveStatus);

  return (
    <header className="rigmd-app-header flex min-h-[72px] shrink-0 items-center justify-between border-b px-4 sm:px-6 lg:px-8">
      <div className="flex min-w-0 items-center gap-3">
        {onMenuClick && (
          <motion.button
            type="button"
            onClick={onMenuClick}
            whileTap={{ scale: 0.98 }}
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card-soft)] text-slate-400 transition hover:border-cyan-400/35 hover:text-white md:hidden"
            aria-label="Open navigation menu"
          >
            <Menu size={20} />
          </motion.button>
        )}

        <div className="min-w-0">
          <h2 className="truncate text-lg font-bold text-[var(--rigmd-text-main)] sm:text-xl">{title}</h2>
          <p className="truncate text-xs text-[var(--rigmd-text-muted)] sm:text-sm">{subtitle}</p>
        </div>
      </div>

      <div className="flex shrink-0 items-center gap-2 sm:gap-4">
        <div className="hidden items-center gap-2 rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-card-soft)] px-3.5 py-2 text-sm shadow-[inset_0_1px_0_rgba(255,255,255,0.035)] sm:flex">
          <Microchip size={16} className="text-cyan-300" />
          <span className="max-w-[180px] truncate text-[var(--rigmd-text-main)]">{deviceName}</span>
          <div className={`ml-2 h-1.5 w-1.5 rounded-full ${statusView.dotClassName}`} />
        </div>

        <div className={`hidden items-center gap-2 rounded-lg border px-3 py-1.5 text-sm font-medium sm:flex ${statusView.className}`}>
          <Monitor size={16} className={statusView.iconClassName} />
          {statusView.label}
        </div>

        <motion.button
          type="button"
          whileTap={{ scale: 0.98 }}
          className="flex h-9 w-9 items-center justify-center rounded-lg text-slate-400 transition hover:bg-[var(--rigmd-card-soft)] hover:text-white"
        >
          <Bell size={20} />
        </motion.button>

        <motion.button
          type="button"
          whileTap={{ scale: 0.98 }}
          className="ml-1 flex h-9 w-9 items-center justify-center rounded-full border border-cyan-400/22 bg-cyan-400/12 text-sm font-bold text-cyan-100 transition hover:bg-cyan-400/20"
        >
          MG
        </motion.button>
      </div>
    </header>
  );
}
