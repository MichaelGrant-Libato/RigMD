/**
 * Thin API wrappers that automatically inject the X-Client-ID header
 * into every request so the backend scopes data to this client.
 *
 * Usage:
 *   import { apiFetch, apiGet, apiPost } from '../lib/api';
 *
 *   // Drop-in replacement for fetch():
 *   const res = await apiFetch('/api/diagnosis/sessions');
 *
 *   // Axios-style helpers:
 *   const res = await apiGet('/api/recurring/patterns');
 *   const res = await apiPost('/api/diagnosis/submit', payload);
 */

import axios, { type AxiosRequestConfig, type AxiosResponse } from 'axios';
import { getClientId } from './clientId';

const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5273';

// ---------------------------------------------------------------------------
// fetch() wrapper
// ---------------------------------------------------------------------------

/**
 * Drop-in replacement for fetch() that prepends the API base URL and injects
 * the X-Client-ID header automatically.
 */
export async function apiFetch(
  path: string,
  init: RequestInit = {},
): Promise<Response> {
  const clientId = getClientId();

  const headers = new Headers(init.headers);
  headers.set('X-Client-ID', clientId);

  if (!headers.has('Content-Type') && init.method && init.method !== 'GET') {
    headers.set('Content-Type', 'application/json');
  }

  return fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers,
  });
}

// ---------------------------------------------------------------------------
// Axios wrappers
// ---------------------------------------------------------------------------

function clientHeaders(): Record<string, string> {
  return { 'X-Client-ID': getClientId() };
}

/**
 * Axios GET with automatic X-Client-ID injection.
 */
export async function apiGet<T = unknown>(
  path: string,
  config: AxiosRequestConfig = {},
): Promise<AxiosResponse<T>> {
  return axios.get<T>(`${API_BASE_URL}${path}`, {
    ...config,
    headers: {
      ...config.headers,
      ...clientHeaders(),
    },
  });
}

/**
 * Axios POST with automatic X-Client-ID injection.
 */
export async function apiPost<T = unknown>(
  path: string,
  data?: unknown,
  config: AxiosRequestConfig = {},
): Promise<AxiosResponse<T>> {
  return axios.post<T>(`${API_BASE_URL}${path}`, data, {
    ...config,
    headers: {
      'Content-Type': 'application/json',
      ...config.headers,
      ...clientHeaders(),
    },
  });
}
