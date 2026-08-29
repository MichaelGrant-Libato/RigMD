/**
 * Thin API wrappers that automatically inject the X-Client-ID header
 * into every request so the backend scopes data to this client.
 */

import axios, {
  type AxiosRequestConfig,
  type AxiosResponse,
} from 'axios';
import { getClientId } from './clientId';

const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5273';

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
    },
  });
}