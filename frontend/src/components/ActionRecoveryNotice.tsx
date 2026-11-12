import { useEffect, useState } from 'react';
import { apiGet } from '../lib/api';

export default function ActionRecoveryNotice() {
  const [message, setMessage] = useState('');
  useEffect(() => {
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    let running = false;
    const refresh = () => {
      if (running || controller.signal.aborted) return;
      running = true;
      clearTimeout(timer);
      void apiGet<{ blocked: boolean; message: string }>('/api/desktop/recovery', { signal: controller.signal, timeout: 10000 })
        .then(({ data }) => {
          if (controller.signal.aborted) return;
          if (typeof data.blocked !== 'boolean' || typeof data.message !== 'string') throw new Error('Invalid recovery status');
          setMessage(data.blocked ? data.message : '');
          if (data.blocked) timer = setTimeout(refresh, 2000);
        })
        .catch(() => {
          if (!controller.signal.aborted) {
            setMessage('RigMD could not check whether system changes are available. It will retry.');
            timer = setTimeout(refresh, 5000);
          }
        })
        .finally(() => { running = false; });
    };
    refresh(); window.addEventListener('focus', refresh);
    return () => { controller.abort(); clearTimeout(timer); window.removeEventListener('focus', refresh); };
  }, []);
  return message ? <div role="alert" className="mx-4 my-3 shrink-0 rounded-xl border border-amber-400/40 bg-amber-400/10 p-4 text-sm leading-relaxed text-amber-100"><strong className="block mb-1">System changes are temporarily unavailable</strong>{message}</div> : null;
}
