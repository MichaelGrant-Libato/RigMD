declare global {
  interface Window {
    readonly __rigmdLocalApiToken?: string;
  }
}

export function getLocalApiToken(): string {
  return typeof window === 'undefined' ? '' : window.__rigmdLocalApiToken ?? '';
}

export function localApiAuthHeaders(): Record<string, string> {
  const token = getLocalApiToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}
