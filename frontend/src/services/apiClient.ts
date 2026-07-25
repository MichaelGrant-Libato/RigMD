// API Client wrapper for managing client-specific data isolation
export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:8000';

export function getClientId(): string {
  let clientId = localStorage.getItem('rigmd_client_id');
  if (!clientId) {
    clientId = crypto.randomUUID();
    localStorage.setItem('rigmd_client_id', clientId);
  }
  return clientId;
}

export function getHeaders(additionalHeaders: Record<string, string> = {}): Record<string, string> {
  return {
    'Content-Type': 'application/json',
    'X-Client-ID': getClientId(),
    ...additionalHeaders,
  };
}

export async function fetchWithClient(url: string, options: RequestInit = {}): Promise<Response> {
  const headers = getHeaders(options.headers as Record<string, string>);
  return fetch(url, { ...options, headers });
}
