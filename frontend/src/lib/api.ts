/**
 * API wrappers authenticated by the desktop's per-launch local credential.
 * X-Client-ID remains a compatibility hint, not an authorization boundary.
 */

import axios, {
  type AxiosRequestConfig,
  type AxiosResponse,
} from 'axios';
import { getClientId } from './clientId';
import { getLocalApiToken, localApiAuthHeaders } from './localApiSession';

export const API_BASE_URL =
  getLocalApiToken()
    ? window.location.origin
    : import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5273';

export async function apiFetch(
  path: string,
  init: RequestInit = {},
): Promise<Response> {
  const clientId = getClientId();

  const headers = new Headers();
  headers.set('X-Client-ID', clientId);

  if (init.headers) {
    const suppliedHeaders = new Headers(init.headers);

    suppliedHeaders.forEach((value, key) => {
      headers.set(key, value);
    });
  }

  if (
    !headers.has('Content-Type') &&
    init.method &&
    init.method !== 'GET'
  ) {
    headers.set('Content-Type', 'application/json');
  }

  for (const [key, value] of Object.entries(localApiAuthHeaders())) headers.set(key, value);

  return fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers,
  });
}

function clientHeaders(): Record<string, string> {
  return {
    'X-Client-ID': getClientId(),
  };
}

export async function apiGet<T = unknown>(
  path: string,
  config: AxiosRequestConfig = {},
): Promise<AxiosResponse<T>> {
  return axios.get<T>(`${API_BASE_URL}${path}`, {
    ...config,
    headers: {
      ...clientHeaders(),
      ...config.headers,
      ...localApiAuthHeaders(),
    },
  });
}

export async function apiPost<T = unknown>(
  path: string,
  data?: unknown,
  config: AxiosRequestConfig = {},
): Promise<AxiosResponse<T>> {
  return axios.post<T>(`${API_BASE_URL}${path}`, data, {
    ...config,
    headers: {
      'Content-Type': 'application/json',
      ...clientHeaders(),
      ...config.headers,
      ...localApiAuthHeaders(),
    },
  });
}

export async function apiDelete<T = unknown>(
  path: string,
  config: AxiosRequestConfig = {},
): Promise<AxiosResponse<T>> {
  return axios.delete<T>(`${API_BASE_URL}${path}`, {
    ...config,
    headers: {
      ...clientHeaders(),
      ...config.headers,
      ...localApiAuthHeaders(),
    },
  });
}
