import { apiGet, apiPost } from '../lib/api';

export interface CloudAiAccount {
  signedIn: boolean;
  email: string | null;
}

export async function getCloudAiAccount(): Promise<CloudAiAccount> {
  return (await apiGet<CloudAiAccount>('/api/cloud-ai/account')).data;
}

export async function signInCloudAi(email: string, password: string): Promise<CloudAiAccount> {
  return (await apiPost<CloudAiAccount>('/api/cloud-ai/sign-in', { email, password })).data;
}

export async function signOutCloudAi(): Promise<CloudAiAccount> {
  return (await apiPost<CloudAiAccount>('/api/cloud-ai/sign-out')).data;
}
