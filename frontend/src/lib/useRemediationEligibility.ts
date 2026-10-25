import { useCallback, useEffect, useState } from 'react';
import { apiFetch } from './api';
import { readRemediationEligibility, type RemediationEligibility } from './remediationEligibility';

type RequestState = { key: string; eligibility: RemediationEligibility | null; pending: boolean; error: boolean };

export function useRemediationEligibility(sessionId: string | undefined, revision: string) {
  const [refreshKey, setRefreshKey] = useState(0);
  const key = `${sessionId ?? ''}:${revision}:${refreshKey}`;
  const [result, setResult] = useState<RequestState>({ key: '', eligibility: null, pending: true, error: false });
  const refresh = useCallback(() => setRefreshKey(value => value + 1), []);

  useEffect(() => {
    const controller = new AbortController();
    setResult({ key, eligibility: null, pending: Boolean(sessionId), error: false });
    if (sessionId) {
      const timeout = window.setTimeout(() => controller.abort(), 15000);
      void (async () => {
        try {
          const response = await apiFetch(`/api/autonomy/${encodeURIComponent(sessionId)}/remediation-eligibility`, { signal: controller.signal, cache: 'no-store' });
          if (!response.ok) throw new Error('Could not verify action eligibility');
          const eligibility = readRemediationEligibility(await response.json(), sessionId);
          if (controller.signal.aborted) return;
          setResult({ key, eligibility, pending: false, error: false });
        } catch {
          setResult(current => current.key === key ? { key, eligibility: null, pending: false, error: true } : current);
        } finally {
          window.clearTimeout(timeout);
        }
      })();
      return () => { window.clearTimeout(timeout); controller.abort(); };
    }
    return () => controller.abort();
  }, [key, sessionId]);

  useEffect(() => {
    window.addEventListener('focus', refresh);
    window.addEventListener('rigmd-remediation-evidence-changed', refresh);
    return () => {
      window.removeEventListener('focus', refresh);
      window.removeEventListener('rigmd-remediation-evidence-changed', refresh);
    };
  }, [refresh]);

  return { ...(result.key === key ? result : { eligibility: null, pending: true, error: false }), refresh };
}
