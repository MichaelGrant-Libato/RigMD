export interface DesktopPdfBridge {
  postMessage(message: { type: string; requestId: string; filename: string }): void;
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
  removeEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
}
declare global {
  interface Window { readonly __rigmdDesktopPdfExport?: boolean; }
}
export function getDesktopPdfBridge(): DesktopPdfBridge | null {
  if (typeof window === 'undefined' || window.__rigmdDesktopPdfExport !== true) return null;
  return (window as Window & { chrome?: { webview?: DesktopPdfBridge } }).chrome?.webview ?? null;
}

export function saveDesktopReportPdf(filename: string, bridge: DesktopPdfBridge, signal: AbortSignal, requestId = crypto.randomUUID()) {
  return new Promise<'saved' | 'canceled'>((resolve, reject) => {
    signal.throwIfAborted();
    const cleanup = () => {
      bridge.removeEventListener('message', receive);
      signal.removeEventListener('abort', abort);
    };
    const abort = () => { cleanup(); reject(new DOMException('PDF request canceled.', 'AbortError')); };
    const receive = (event: { data: unknown }) => {
      const data = event.data;
      if (!data || typeof data !== 'object') return;
      const result = data as Record<string, unknown>;
      if (result.type !== 'rigmd:report-pdf-result' || result.requestId !== requestId ||
          !['saved', 'canceled', 'busy', 'error'].includes(String(result.status))) return;
      cleanup();
      if (result.status === 'saved' || result.status === 'canceled') resolve(result.status);
      else reject(new Error(result.status === 'busy' ? 'Another PDF is being saved.' : 'PDF could not be saved.'));
    };
    bridge.addEventListener('message', receive);
    signal.addEventListener('abort', abort, { once: true });
    try { bridge.postMessage({ type: 'rigmd:save-report-pdf', requestId, filename }); }
    catch (error) { cleanup(); reject(error); }
  });
}
