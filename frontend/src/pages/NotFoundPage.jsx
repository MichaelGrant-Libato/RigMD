import { Activity, ArrowLeft, LogIn } from 'lucide-react';
import { Link } from 'react-router-dom';

export default function NotFoundPage() {
  return (
    <main className="min-h-screen bg-[var(--rigmd-bg)] px-6 py-10 text-[var(--rigmd-text-main)]">
      <div className="mx-auto max-w-3xl">
        <Link to="/" className="inline-flex items-center gap-3 text-xl font-semibold">
          <span className="flex h-10 w-10 items-center justify-center rounded-lg border border-[var(--rigmd-border-active)] bg-[var(--rigmd-accent-soft)] text-[var(--rigmd-accent)]">
            <Activity size={22} aria-hidden="true" />
          </span>
          RigMD
        </Link>
        <div className="py-16 sm:py-24">
          <p className="text-sm font-semibold text-[var(--rigmd-accent)]">404</p>
          <h1 className="mt-3 text-3xl font-semibold">Page not found</h1>
          <p className="mt-4 max-w-xl text-sm leading-6 text-[var(--rigmd-text-muted)]">
            This link may be out of date, or the address may be incorrect.
          </p>
          <div className="mt-8 flex flex-wrap gap-4">
            <Link to="/" className="inline-flex items-center gap-2 rounded-lg bg-[var(--rigmd-accent)] px-4 py-3 text-sm font-semibold text-slate-950">
              <ArrowLeft size={18} /> Back to RigMD
            </Link>
            <Link to="/login" className="inline-flex items-center gap-2 rounded-lg border border-[var(--rigmd-border)] px-4 py-3 text-sm font-semibold">
              <LogIn size={18} /> Log in
            </Link>
          </div>
        </div>
      </div>
    </main>
  );
}
