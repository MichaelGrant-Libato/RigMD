/// <reference types="vite/client" />

import { apiFetch } from '../lib/api';

export interface StorageDetail {
  model: string;
  type: string;
  size_gb: number;
  interface: string;
  media_type?: string | null;
  bus_type?: string | null;
  detection_source?: string | null;
  disk_index?: number | null;
  used_gb?: number | null;
  usage_percent?: number | null;
  volumes?: Array<{
    drive: string;
    mountpoint: string;
    fstype: string;
    disk_index?: number | null;
    total_gb: number;
    used_gb: number;
    usage_percent: number;
  }>;
}

export interface SaveProfilePayload {
  cpu_model: string;
  ram_capacity: string;
  storage_type: string;
  storage_capacity: string;
  storage_details?: StorageDetail[] | null;
  os_version: string;
  gpu_driver?: string | null;
  chipset_driver?: string | null;
  system_age?: string | null;
}

export interface HardwareProfile {
  id: string;
  cpu_model: string;
  ram_capacity: string;
  storage_type: string;
  storage_capacity: string;
  storage_details: StorageDetail[] | null;
  os_version: string;
  gpu_driver: string | null;
  chipset_driver: string | null;
  system_age: string | null;
  created_at: string;
}

export type SaveProfileResponse = HardwareProfile;

export async function saveHardwareProfile(
  payload: SaveProfilePayload
): Promise<SaveProfileResponse> {
  try {
    const response = await apiFetch(
      '/api/profiles/save',
      {
        method: 'POST',
        body: JSON.stringify(payload),
      }
    );

    if (!response.ok) {
      const errorData =
        await response.json().catch(() => ({}));

      throw new Error(
        errorData.detail ||
          `HTTP ${response.status}: Failed to save profile`
      );
    }

    return await response.json();
  } catch (error) {
    const message =
      error instanceof Error
        ? error.message
        : 'Unknown error';

    throw new Error(
      `Failed to save profile: ${message}`
    );
  }
}