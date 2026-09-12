import { useEffect, useRef } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  BrainCircuit,
  ShieldAlert,
  Zap,
  Microscope,
  RotateCcw,
  CheckCircle2,
  AlertCircle
} from 'lucide-react';

export interface TimelineEvent {
  id: string;
  timestamp: Date;
  type: 'PLANNER' | 'SAFETY' | 'EXECUTOR' | 'VERIFICATION' | 'ROLLBACK' | 'PERSISTENCE' | 'ORCHESTRATOR' | 'PIVOT' | 'INFO';
  message: string;
}

interface AgentTimelineProps {
  events: TimelineEvent[];
  isComplete: boolean;
}

const getEventIcon = (type: string) => {
  switch (type) {
    case 'PLANNER':
      return <BrainCircuit size={16} className="text-purple-400" />;
    case 'SAFETY':
      return <ShieldAlert size={16} className="text-blue-400" />;
    case 'EXECUTOR':
      return <Zap size={16} className="text-amber-400" />;
    case 'VERIFICATION':
      return <Microscope size={16} className="text-cyan-400" />;
    case 'ROLLBACK':
      return <RotateCcw size={16} className="text-red-400" />;
    case 'PIVOT':
      return <RotateCcw size={16} className="text-fuchsia-400" />;
    case 'ORCHESTRATOR':
      return <AlertCircle size={16} className="text-slate-300" />;
    case 'PERSISTENCE':
      return <CheckCircle2 size={16} className="text-emerald-400" />;
    default:
      return <AlertCircle size={16} className="text-slate-400" />;
  }
};

const getEventColor = (type: string) => {
  switch (type) {
    case 'PLANNER': return 'border-purple-500/30 bg-purple-500/10 text-purple-200';
    case 'SAFETY': return 'border-blue-500/30 bg-blue-500/10 text-blue-200';
    case 'EXECUTOR': return 'border-amber-500/30 bg-amber-500/10 text-amber-200';
    case 'VERIFICATION': return 'border-cyan-500/30 bg-cyan-500/10 text-cyan-200';
    case 'ROLLBACK': return 'border-red-500/30 bg-red-500/10 text-red-200';
    case 'PIVOT': return 'border-fuchsia-500/30 bg-fuchsia-500/10 text-fuchsia-200';
    case 'PERSISTENCE': return 'border-emerald-500/30 bg-emerald-500/10 text-emerald-200';
    default: return 'border-slate-600/30 bg-slate-600/10 text-slate-300';
  }
};

export default function AgentTimeline({ events, isComplete }: AgentTimelineProps) {
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (containerRef.current) {
      containerRef.current.scrollTop = containerRef.current.scrollHeight;
    }
  }, [events]);

  return (
    <div className="flex flex-col h-full bg-[#0a0f14] rounded-xl border border-[var(--rigmd-border)] overflow-hidden">
      <div className="bg-[#121a22] p-3 border-b border-[var(--rigmd-border)] flex items-center justify-between">
        <div className="flex items-center gap-2 text-cyan-300">
          <BrainCircuit size={18} />
          <h3 className="text-sm font-bold uppercase tracking-wider">Live Agent Feed</h3>
        </div>
        <div className="flex items-center gap-2">
          {!isComplete && (
            <div className="flex items-center gap-1.5">
              <span className="relative flex h-2 w-2">
                <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-cyan-400 opacity-75"></span>
                <span className="relative inline-flex rounded-full h-2 w-2 bg-cyan-500"></span>
              </span>
              <span className="text-[10px] uppercase font-bold text-cyan-400">Thinking</span>
            </div>
          )}
          {isComplete && (
            <div className="flex items-center gap-1.5 text-emerald-400">
              <CheckCircle2 size={12} />
              <span className="text-[10px] uppercase font-bold">Complete</span>
            </div>
          )}
        </div>
      </div>
      
      <div 
        ref={containerRef}
        className="flex-1 overflow-y-auto p-4 space-y-3 custom-scrollbar"
        style={{ minHeight: '300px', maxHeight: '500px' }}
      >
        <AnimatePresence initial={false}>
          {events.length === 0 && !isComplete && (
            <motion.div 
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              className="h-full flex items-center justify-center text-slate-500 text-sm italic"
            >
              Waiting for agent activity...
            </motion.div>
          )}
          
          {events.map((event) => (
            <motion.div
              key={event.id}
              initial={{ opacity: 0, y: 10, scale: 0.98 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              className={`flex gap-3 p-3 rounded-lg border ${getEventColor(event.type)}`}
            >
              <div className="shrink-0 mt-0.5">
                {getEventIcon(event.type)}
              </div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 mb-1">
                  <span className="text-[10px] font-bold uppercase tracking-wider opacity-70">
                    {event.type}
                  </span>
                  <span className="text-[10px] opacity-50">
                    {event.timestamp.toLocaleTimeString([], { hour12: false, hour: '2-digit', minute: '2-digit', second:'2-digit' })}
                  </span>
                </div>
                <p className="text-xs break-words whitespace-pre-wrap">{event.message}</p>
              </div>
            </motion.div>
          ))}
        </AnimatePresence>
      </div>
    </div>
  );
}
