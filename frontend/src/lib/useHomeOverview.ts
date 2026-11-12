import { useEffect, useState } from 'react';
import { apiGet } from './api';
import { essentialHomeReady, parseHomeOverview, type HomeOverview } from './homeOverview';

export function useHomeOverview(active: boolean) {
  const [data, setData] = useState<HomeOverview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [state, setState] = useState<'pending' | 'completed' | 'failed'>('pending');
  useEffect(() => {
    if (!active) return;
    const abort = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    const startedAt = performance.now();
    const load = async () => {
      try {
        const response = await apiGet<unknown>('/api/home/overview', { signal: abort.signal, timeout: 5000 });
        const next = parseHomeOverview(response.data);
        if (abort.signal.aborted) return;
        setData(next); setError(null);
        if (essentialHomeReady(next)) setState('completed');
        else if (performance.now() - startedAt > 10000) throw new Error('CPU sample not ready');
      } catch {
        if (abort.signal.aborted) return;
        setData(null); setState('failed'); setError('Essential readings could not be loaded. RigMD will retry.');
      }
      if (!abort.signal.aborted) timer = setTimeout(() => { void load(); }, 1000);
    };
    void load();
    return () => { abort.abort(); if (timer) clearTimeout(timer); };
  }, [active]);
  return { data, error, state };
}
