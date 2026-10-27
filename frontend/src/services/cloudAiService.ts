import { apiGet, apiPost } from '../lib/api';

export interface CloudAiAccount {
  signedIn: boolean;
  email: string | null;
  remembered?: boolean;
  reconnecting?: boolean;
}

export async function getCloudAiAccount(): Promise<CloudAiAccount> {
  return (await apiGet<CloudAiAccount>('/api/cloud-ai/account')).data;
}

export async function signOutCloudAi(): Promise<CloudAiAccount> {
  return (await apiPost<CloudAiAccount>('/api/cloud-ai/sign-out')).data;
}

export interface WebsiteConnection { state: string; code?: string | null; expiresAt?: string | null; }
export async function startWebsiteConnection(): Promise<WebsiteConnection> {
  return (await apiPost<WebsiteConnection>('/api/cloud-ai/connect')).data;
}
export async function getWebsiteConnection(): Promise<WebsiteConnection> {
  return (await apiGet<WebsiteConnection>('/api/cloud-ai/connect')).data;
}
export async function cancelWebsiteConnection(): Promise<WebsiteConnection> {
  return (await apiPost<WebsiteConnection>('/api/cloud-ai/connect/cancel')).data;
}
