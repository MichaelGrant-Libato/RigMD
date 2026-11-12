import { useCallback, useEffect, useState } from 'react';
import { apiFetch } from './api';
import { readRemediationEligibility } from './remediationEligibility';
import { getRemediationRefreshView, type RemediationRefreshState } from './remediationRefreshState';

export function useRemediationEligibility(sessionId: string | undefined, revision: string) {
  const [refreshKey, setRefreshKey] = useState(0);
  const identity = `${sessionId ?? ''}:${revision}`;
  const key = `${identity}:${refreshKey}`;
  const [result, setResult] = useState<RemediationRefreshState>({ identity: '', key: '', eligibility: null, retainedEligibility: null, pending: true, error: false });
  const refresh = useCallback(() => setRefreshKey(value => value + 1), []);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;
    setResult(current => ({ identity, key, eligibility: null,
      retainedEligibility: current.identity === identity ? current.eligibility ?? current.retainedEligibility : null,
      pending: Boolean(sessionId), error: false }));
    if (sessionId) {
      const timeout = window.setTimeout(() => controller.abort(), 15000);
      void (async () => {
        try {
          const response = await apiFetch(`/api/autonomy/${encodeURIComponent(sessionId)}/remediation-eligibility`, { signal: controller.signal, cache: 'no-store' });
          if (!response.ok) throw new Error('Could not verify action eligibility');
          const eligibility = readRemediationEligibility(await response.json(), sessionId);
          if (!active || controller.signal.aborted) return;
          setResult(current => current.key === key
            ? { identity, key, eligibility, retainedEligibility: eligibility, pending: false, error: false } : current);
        } catch {
          if (active) setResult(current => current.key === key ? { ...current, eligibility: null, pending: false, error: true } : current);
        } finally {
          window.clearTimeout(timeout);
        }
      })();
      return () => { active = false; window.clearTimeout(timeout); controller.abort(); };
    }
    return () => { active = false; controller.abort(); };
  }, [identity, key, sessionId]);

  useEffect(() => {
    window.addEventListener('focus', refresh);
    window.addEventListener('rigmd-remediation-evidence-changed', refresh);
    return () => {
      window.removeEventListener('focus', refresh);
      window.removeEventListener('rigmd-remediation-evidence-changed', refresh);
    };
  }, [refresh]);

  return { ...getRemediationRefreshView(result, identity, key), refresh };
}
