import { createContext, useContext, type ReactNode } from 'react';
import { Bell, Microchip, Monitor, Settings } from 'lucide-react';

export type LiveDataStatus = 'live' | 'syncing' | 'offline' | 'stale';

interface HeaderStatusContextValue {
  deviceName: string;
  liveStatus: LiveDataStatus;
}

const HeaderStatusContext = createContext<HeaderStatusContextValue>({
  deviceName: 'Detecting PC',
  liveStatus: 'syncing',
});

export function HeaderStatusProvider({
  children,
  deviceName,
  liveStatus,
}: HeaderStatusContextValue & { children: ReactNode }) {
  return (
    <HeaderStatusContext.Provider value={{ deviceName, liveStatus }}>
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
      className: 'border-cyan-500/20 bg-cyan-500/10 text-cyan-400',
      iconClassName: 'text-cyan-400',
      dotClassName: 'bg-cyan-400 animate-pulse',
    };
  }

  if (status === 'offline') {
    return {
      label: 'Offline',
      className: 'border-red-500/20 bg-red-500/10 text-red-400',
      iconClassName: 'text-red-400',
      dotClassName: 'bg-red-400',
    };
  }

  if (status === 'stale') {
    return {
      label: 'Stale Data',
      className: 'border-orange-500/20 bg-orange-500/10 text-orange-400',
      iconClassName: 'text-orange-400',
      dotClassName: 'bg-orange-400',
    };
  }

  return {
    label: 'Live Data',
    className: 'border-emerald-500/20 bg-emerald-500/10 text-emerald-400',
    iconClassName: 'text-emerald-400',
    dotClassName: 'bg-emerald-500',
  };
}

export default function TopHeader({ title, subtitle }: TopHeaderProps) {
  const { deviceName, liveStatus } = useContext(HeaderStatusContext);
  const statusView = getLiveStatusView(liveStatus);

  return (
    <header className="flex h-20 shrink-0 items-center justify-between border-b border-[#253041] bg-[#111827] px-8">
      <div>
        <h2 className="text-xl font-bold text-white">{title}</h2>
        <p className="text-sm text-slate-400">{subtitle}</p>
      </div>

      <div className="flex items-center gap-4">
        <div className="hidden items-center gap-2 rounded-lg border border-[#2c3a4f] bg-[#172232] px-4 py-2 text-sm sm:flex">
          <Microchip size={16} className="text-cyan-400" />
          <span className="max-w-[180px] truncate text-white">{deviceName}</span>
          <div className={`ml-2 h-1.5 w-1.5 rounded-full ${statusView.dotClassName}`} />
        </div>

        <div className={`hidden items-center gap-2 rounded-lg border px-3 py-1.5 text-sm font-medium sm:flex ${statusView.className}`}>
          <Monitor size={16} className={statusView.iconClassName} />
          {statusView.label}
        </div>

        <button type="button" className="p-2 text-slate-400 transition hover:text-white">
          <Bell size={20} />
        </button>

        <button type="button" className="p-2 text-slate-400 transition hover:text-white">
          <Settings size={20} />
        </button>

        <button
          type="button"
          className="ml-1 flex h-9 w-9 items-center justify-center rounded-full bg-cyan-600 text-sm font-bold text-white transition hover:bg-cyan-500"
        >
          MG
        </button>
      </div>
    </header>
  );
}
