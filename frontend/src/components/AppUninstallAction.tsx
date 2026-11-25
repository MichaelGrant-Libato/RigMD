import { useState } from 'react';
import { Trash2 } from 'lucide-react';
import { apiPost } from '../lib/api';

interface UninstallReview {
  approvalId?: string | null;
  message: string;
  name?: string | null;
  version?: string | null;
  productCode?: string | null;
}

export interface AppUninstallResult {
  status: string;
  message: string;
  name?: string | null;
  productCode?: string | null;
  exitCode?: string | null;
}

const secondaryButton = 'rounded-md border border-slate-700 px-3 py-1.5 text-xs text-slate-200 hover:bg-slate-800 disabled:opacity-50';

export default function AppUninstallAction({
  appId, availability, reason, busy, onUninstall,
}: {
  appId: string;
  availability?: string;
  reason?: string | null;
  busy: boolean;
  onUninstall: (approvalId: string) => Promise<void>;
}) {
  const [review, setReview] = useState<UninstallReview | null>(null);
  const [reviewing, setReviewing] = useState(false);

  if (availability !== 'review') {
    return <p className="text-slate-400">{reason ?? 'Use Windows Settings for this app.'}</p>;
  }

  async function prepare() {
    setReviewing(true);
    setReview(null);
    try {
      const { data } = await apiPost<UninstallReview>('/api/software-inventory/apps/uninstall-review', { appId });
      setReview(data);
    } catch {
      setReview({ message: 'RigMD could not verify this app. Try again or use Manage in Windows.' });
    } finally {
      setReviewing(false);
    }
  }

  return (
    <div className="mt-3 text-xs">
      {review?.approvalId ? (
        <div role="group" aria-label="Confirm app uninstall" className="space-y-2 rounded-lg border border-amber-500/25 bg-amber-400/5 p-3">
          <p className="font-semibold text-white">Uninstall {review.name}{review.version ? ' ' + review.version : ''}?</p>
          <p className="text-slate-300">{review.message}</p>
          {review.productCode && (
            <details className="text-slate-400">
              <summary className="cursor-pointer">Package details</summary>
              <p className="mt-1 break-all">Windows Installer product code · {review.productCode}</p>
            </details>
          )}
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              disabled={busy}
              className="rounded-md bg-rose-500/15 px-3 py-1.5 font-semibold text-rose-200 hover:bg-rose-500/25 disabled:opacity-50"
              onClick={() => {
                const approval = review.approvalId;
                setReview(null);
                if (approval) void onUninstall(approval);
              }}
            >
              Uninstall app
            </button>
            <button type="button" className={secondaryButton} onClick={() => setReview(null)}>Cancel</button>
          </div>
        </div>
      ) : (
        <>
          <button type="button" disabled={busy || reviewing} className={secondaryButton + ' inline-flex items-center gap-2'}
            onClick={() => void prepare()}>
            <Trash2 size={13} aria-hidden="true" /> {reviewing ? 'Checking app…' : 'Uninstall…'}
          </button>
          {review && <p role="status" className="mt-2 text-slate-300">{review.message}</p>}
        </>
      )}
    </div>
  );
}
