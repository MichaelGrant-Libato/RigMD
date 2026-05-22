import React, { useState, useEffect } from 'react';
import axios from 'axios';
import { 
    LayoutDashboard, Server, Stethoscope, History, Activity, AlertTriangle, 
    FileText, Settings, HelpCircle, Bell, Search, Zap, Layers, Cpu, MemoryStick, 
    HardDrive, Monitor, Terminal, Microchip, Calendar, Save, RefreshCw, RotateCcw, Database
} from 'lucide-react';

interface HardwareStats {
    os_version: string;
    system_age: string;
    chipset_driver: string;
    storage_type: string;
    cpu: { name: string; usage_percent: number; cores: number; threads: number; frequency_mhz: number; };
    gpu: { name: string; driver: string; type: string; vram_gb: number; };
    ram: { total_gb: number; used_gb: number; usage_percent: number; };
    disk: { total_gb: number; usage_percent: number; };
}

export default function HardwareDashboard() {
    const [stats, setStats] = useState<HardwareStats | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [activeTab, setActiveTab] = useState('auto');

    useEffect(() => {
        const fetchHardware = async () => {
            try {
                const response = await axios.get('http://localhost:8000/api/hardware/live');
                setStats(response.data);
                setError(null);
            } catch (err) {
                setError("Connection lost. Ensure FastAPI is running on port 8000.");
            }
        };

        fetchHardware();
        const interval = setInterval(fetchHardware, 1500);
        return () => clearInterval(interval);
    }, []);

    const SidebarItem = ({ icon: Icon, label, active = false, badge = null }: any) => (
        <div className={`flex items-center justify-between px-4 py-2.5 mb-1 rounded-lg cursor-pointer transition-colors ${active ? 'bg-[#1f2937] text-cyan-400 border-l-2 border-cyan-400 rounded-l-none' : 'text-gray-400 hover:text-gray-200 hover:bg-[#161b22]'}`}>
            <div className="flex items-center gap-3">
                <Icon size={18} />
                <span className="text-sm font-medium">{label}</span>
            </div>
            {badge && <span className="text-xs font-bold text-cyan-500">{badge}</span>}
        </div>
    );

    const HardwareCard = ({ icon: Icon, title, value, confidence, confColor, subtitle }: any) => (
        <div className="bg-[#161b22] p-4 rounded-xl border border-[#30363d] flex items-center justify-between">
            <div className="flex items-center gap-4">
                <div className="p-3 bg-[#0d1117] rounded-lg border border-[#30363d] text-cyan-400">
                    <Icon size={24} />
                </div>
                <div>
                    <p className="text-xs text-gray-400 mb-1">{title} {subtitle && <span className="text-[#30363d] mx-1">|</span>} {subtitle && <span className="text-cyan-600">{subtitle}</span>}</p>
                    <p className="text-sm font-semibold text-gray-100">{value}</p>
                </div>
            </div>
            <span className={`text-xs font-medium ${confColor}`}>{confidence}</span>
        </div>
    );

    return (
        <div className="flex h-screen bg-[#0d1117] text-gray-200 font-sans overflow-hidden">
            {/* SIDEBAR */}
            <aside className="w-64 bg-[#0d1117] border-r border-[#30363d] flex flex-col hidden md:flex shrink-0">
                <div className="p-6 flex items-center gap-3">
                    <Activity className="text-cyan-400" size={28} />
                    <div>
                        <h1 className="font-bold text-lg leading-tight">RigMD</h1>
                        <p className="text-[10px] text-gray-500 uppercase tracking-wider">Diagnostic Support</p>
                    </div>
                </div>

                <div className="flex-1 overflow-y-auto px-3 py-2 custom-scrollbar">
                    <div className="mb-6">
                        <p className="px-4 text-[11px] font-bold text-gray-500 mb-2 tracking-wider">OVERVIEW</p>
                        <SidebarItem icon={LayoutDashboard} label="Home" />
                        <SidebarItem icon={Server} label="System Profile" active={true} />
                    </div>
                    <div className="mb-6">
                        <p className="px-4 text-[11px] font-bold text-gray-500 mb-2 tracking-wider">DIAGNOSTICS</p>
                        <SidebarItem icon={Stethoscope} label="New Diagnosis" />
                        <SidebarItem icon={History} label="Diagnostic History" badge="3" />
                        <SidebarItem icon={Activity} label="Recurring Patterns" badge="2" />
                        <SidebarItem icon={AlertTriangle} label="Warning Signs" badge="1" />
                    </div>
                </div>
            </aside>

            {/* MAIN CONTENT */}
            <main className="flex-1 flex flex-col h-full overflow-hidden">
                {/* HEADER */}
                <header className="h-20 border-b border-[#30363d] flex items-center justify-between px-8 shrink-0">
                    <div>
                        <h2 className="text-xl font-bold text-white">System Profile</h2>
                        <p className="text-sm text-gray-400">Enter or confirm your desktop PC specifications before running diagnostics</p>
                    </div>
                    <div className="flex items-center gap-4">
                        <div className="flex items-center gap-2 bg-[#161b22] border border-[#30363d] px-4 py-2 rounded-lg text-sm cursor-pointer">
                            <Microchip size={16} className="text-cyan-400" />
                            <span>RigMD Session</span>
                            <div className="w-1.5 h-1.5 rounded-full bg-emerald-500 ml-2"></div>
                        </div>
                        <div className="flex items-center gap-2 bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 px-3 py-1.5 rounded-lg text-sm font-medium">
                            <Monitor size={16} /> Live Data
                        </div>
                        <button className="p-2 text-gray-400 hover:text-gray-200"><Bell size={20} /></button>
                        <button className="p-2 text-gray-400 hover:text-gray-200"><Settings size={20} /></button>
                        <div className="w-9 h-9 rounded-full bg-cyan-600 flex items-center justify-center text-sm font-bold text-white ml-2">MG</div>
                    </div>
                </header>

                {/* SCROLLABLE BODY */}
                <div className="flex-1 overflow-y-auto p-8 custom-scrollbar">
                    
                    {/* TABS */}
                    <div className="flex gap-2 mb-6 mt-4">
                        <button 
                            onClick={() => setActiveTab('auto')}
                            className={`flex items-center gap-2 px-5 py-2.5 rounded-lg text-sm font-medium transition-colors ${activeTab === 'auto' ? 'bg-cyan-500/10 text-cyan-400 border border-cyan-500/30' : 'text-gray-400 hover:bg-[#161b22] border border-transparent'}`}
                        >
                            <Zap size={16} /> Auto-Detected Telemetry
                        </button>
                        <button 
                            onClick={() => setActiveTab('manual')}
                            className={`flex items-center gap-2 px-5 py-2.5 rounded-lg text-sm font-medium transition-colors ${activeTab === 'manual' ? 'bg-[#161b22] text-gray-200 border border-[#30363d]' : 'text-gray-400 hover:bg-[#161b22] border border-transparent'}`}
                        >
                            <Layers size={16} /> Manual Details
                        </button>
                    </div>

                    {/* AUTO-DETECTION RESULTS */}
                    <section className="bg-[#161b22] border border-[#30363d] rounded-2xl p-6 mb-8">
                        <div className="flex gap-3 mb-6">
                            <Search className="text-cyan-400 shrink-0 mt-0.5" size={18} />
                            <div>
                                <h3 className="font-semibold text-gray-200 mb-1">Live Sensor Array</h3>
                                <p className="text-sm text-gray-400">Values are queried directly from the Windows Management Instrumentation API and psutil ring buffers.</p>
                            </div>
                        </div>

                        {error ? (
                            <div className="p-4 bg-red-500/10 border border-red-500/20 text-red-400 rounded-lg text-sm font-medium">{error}</div>
                        ) : !stats ? (
                            <div className="p-8 text-center text-gray-500 font-mono tracking-widest animate-pulse border border-dashed border-[#30363d] rounded-xl">ESTABLISHING WMI DATALINK...</div>
                        ) : (
                            <div className="grid grid-cols-1 lg:grid-cols-2 xl:grid-cols-3 gap-4 mb-6">
                                <HardwareCard icon={Cpu} title="CPU" subtitle="Real-Time" value={`${stats.cpu.name} (${Math.round(stats.cpu.usage_percent)}% Load)`} confidence="High" confColor="text-emerald-400" />
                                <HardwareCard icon={MemoryStick} title="RAM" subtitle="Real-Time" value={`${stats.ram.total_gb} GB (${stats.ram.usage_percent}% Allocated)`} confidence="High" confColor="text-emerald-400" />
                                
                                <HardwareCard icon={Monitor} title="GPU Model" subtitle={stats.gpu.type} value={stats.gpu.name} confidence="High" confColor="text-emerald-400" />
                                <HardwareCard icon={Microchip} title="GPU Driver" value={stats.gpu.driver} confidence="High" confColor="text-emerald-400" />
                                
                                <HardwareCard icon={HardDrive} title="Storage Size (C:\)" subtitle="Real-Time" value={`${stats.disk.total_gb} GB Total (${stats.disk.usage_percent}% Full)`} confidence="High" confColor="text-emerald-400" />
                                <HardwareCard icon={Database} title="Storage Type" value={stats.storage_type} confidence="Medium" confColor="text-cyan-400" />
                                
                                <HardwareCard icon={Terminal} title="OS Version" value={stats.os_version} confidence="High" confColor="text-emerald-400" />
                                <HardwareCard icon={Layers} title="Chipset Proxy" value={stats.chipset_driver} confidence="Medium" confColor="text-cyan-400" />
                                <HardwareCard icon={Calendar} title="System Age" subtitle="Since OS Install" value={stats.system_age} confidence="Medium" confColor="text-cyan-400" />
                            </div>
                        )}

                        <button className="flex items-center gap-2 px-5 py-2.5 bg-cyan-500/10 text-cyan-400 border border-cyan-500/30 rounded-lg text-sm font-semibold hover:bg-cyan-500/20 transition-colors mt-4">
                            <Save size={16} /> Save Hardware Profile
                        </button>
                    </section>
                </div>
            </main>
        </div>
    );
}