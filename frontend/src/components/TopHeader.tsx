import { Bell, Microchip, Monitor, Settings } from 'lucide-react';

interface TopHeaderProps {
  title: string;
  subtitle: string;
}

export default function TopHeader({ title, subtitle }: TopHeaderProps) {
  return (
    <header className="flex h-20 shrink-0 items-center justify-between border-b border-[#253041] bg-[#111827] px-8">
      <div>
        <h2 className="text-xl font-bold text-white">{title}</h2>
        <p className="text-sm text-slate-400">{subtitle}</p>
      </div>

      <div className="flex items-center gap-4">
        <div className="hidden items-center gap-2 rounded-lg border border-[#2c3a4f] bg-[#172232] px-4 py-2 text-sm sm:flex">
          <Microchip size={16} className="text-cyan-400" />
          <span className="text-white">RigMD Session</span>
          <div className="ml-2 h-1.5 w-1.5 rounded-full bg-emerald-500" />
        </div>

        <div className="hidden items-center gap-2 rounded-lg border border-emerald-500/20 bg-emerald-500/10 px-3 py-1.5 text-sm font-medium text-emerald-400 sm:flex">
          <Monitor size={16} />
          Live Data
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