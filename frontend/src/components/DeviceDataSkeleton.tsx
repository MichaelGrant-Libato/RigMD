import TopHeader from './TopHeader';

function Block({ className = '' }: { className?: string }) {
  return <div className={`rounded-lg bg-slate-600/25 motion-safe:animate-pulse ${className}`} />;
}

export function ReportPreviewSkeleton() {
  return <div role="status" aria-label="Loading report preview" aria-busy="true" className="p-5 sm:p-8">
    <p className="mb-5 text-sm text-slate-300">Loading report preview…</p>
    <div aria-hidden="true" className="space-y-5 rounded-xl border border-[var(--rigmd-border)] p-5">
      <Block className="h-7 w-2/3" /><Block className="h-4 w-1/3" />
      {[0, 1, 2].map(section => <div key={section} className="space-y-3 border-t border-[var(--rigmd-border)] pt-5">
        <Block className="h-5 w-2/5" /><Block className="h-3 w-full" /><Block className="h-3 w-5/6" /><Block className="h-3 w-3/4" />
      </div>)}
    </div>
  </div>;
}

export default function DeviceDataSkeleton({ report = false }: { report?: boolean }) {
  return <>
    <TopHeader title={report ? 'Share Report' : 'My Device Info'} subtitle={report ? 'Preparing device details for your report' : 'Reading your hardware and Windows information'} />
    <div role="status" aria-label={report ? 'Loading Share Report' : 'Loading My Device Info'} aria-busy="true"
      className="custom-scrollbar min-h-0 min-w-0 flex-1 overflow-y-auto px-4 py-6 sm:px-6 lg:px-8">
      <div className="mx-auto w-full max-w-[1500px] space-y-6">
        <section className="rigmd-card-surface rounded-2xl border p-6">
          <h2 className="text-xl font-semibold text-white">{report ? 'Preparing your report…' : 'Loading full device details…'}</h2>
          <p className="mt-2 text-sm text-slate-400">Reading hardware inventory and sensors. Home essentials are available separately.</p>
          <div aria-hidden="true" className="mt-6 flex flex-wrap justify-between gap-4"><Block className="h-4 w-52" /><Block className="h-10 w-48" /></div>
        </section>
        {report ? <div aria-hidden="true" className="grid min-w-0 gap-5 xl:grid-cols-[minmax(0,1fr)_340px]">
          <section className="rigmd-card-surface space-y-5 rounded-2xl border p-6"><Block className="h-7 w-1/2" /><Block className="h-10 w-full" />
            <div className="grid gap-4 sm:grid-cols-3">{[0, 1, 2].map(i => <Block key={i} className="h-24" />)}</div>
            <Block className="h-64 w-full" />
          </section>
          <section className="rigmd-card-surface space-y-5 self-start rounded-2xl border p-6"><Block className="h-6 w-1/2" />{[0, 1, 2, 3].map(i => <Block key={i} className="h-11 w-full" />)}</section>
        </div> : <div aria-hidden="true" className="space-y-4">
          <Block className="h-6 w-56" />
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">{[0, 1, 2, 3, 4, 5].map(i => <section key={i} className="rigmd-card-surface space-y-5 rounded-xl border p-5">
            <div className="flex items-center gap-4"><Block className="h-14 w-14 shrink-0" /><div className="flex-1 space-y-3"><Block className="h-4 w-1/2" /><Block className="h-6 w-full" /></div></div>
            <Block className="h-4 w-3/4" /><Block className="h-3 w-1/2" />
          </section>)}</div>
        </div>}
      </div>
    </div>
  </>;
}
