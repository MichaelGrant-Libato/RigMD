import { useEffect, useRef } from 'react';
import { ArrowLeft } from 'lucide-react';
import TopHeader from '../components/TopHeader';
import PerformanceRecords from '../components/PerformanceRecords';

export default function AppSpeedView({ onBack }: { onBack: () => void }) {
  const backButton = useRef<HTMLButtonElement>(null);
  useEffect(() => { backButton.current?.focus(); }, []);
  return <>
    <TopHeader title="App speed" subtitle="See how quickly RigMD responds on this PC" />
    <div className="custom-scrollbar min-h-0 flex-1 overflow-y-auto px-4 py-6 sm:px-6 lg:px-8">
      <div className="mx-auto w-full max-w-[1360px] pb-10">
        <button ref={backButton} type="button" onClick={onBack} className="mb-6 inline-flex min-h-11 items-center gap-2 rounded-lg border border-[var(--rigmd-border)] bg-[#15222e] px-4 text-sm font-semibold text-cyan-200"><ArrowLeft size={18} aria-hidden="true" />Back to Settings</button>
        <PerformanceRecords standalone />
      </div>
    </div>
  </>;
}
