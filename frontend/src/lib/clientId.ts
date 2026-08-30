/**
 * Persistent RigMD client identity utility.
 *
 * If a RigMD Agent ID is configured, use it as the installation identity so
 * Agent scans, diagnosis persistence, history, reports, recurring patterns,
 * and dashboard data are scoped to the same device.
 *
 * If no Agent ID is configured, fall back to a persistent browser UUID.
 */

const STORAGE_KEY = 'rigmd_client_id';

const CONFIGURED_AGENT_ID =
  import.meta.env.VITE_AGENT_ID?.trim();

function generateUUID(): string {
  if (
    typeof crypto !== 'undefined' &&
    crypto.randomUUID
  ) {
    return crypto.randomUUID();
  }

  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(
    /[xy]/g,
    (char) => {
      const random =
        (Math.random() * 16) | 0;

      const value =
        char === 'x'
          ? random
          : (random & 0x3) | 0x8;

      return value.toString(16);
    },
  );
}

const _sessionFallbackId =
  generateUUID();

/**
 * Returns the stable identity used for API scoping.
 *
 * Installed RigMD:
 *   VITE_AGENT_ID -> device/installation identity
 *
 * Browser-only development:
 *   persistent localStorage UUID
 */
export function getClientId(): string {
  if (CONFIGURED_AGENT_ID) {
    return CONFIGURED_AGENT_ID;
  }

  try {
    const stored =
      localStorage.getItem(
        STORAGE_KEY,
      );

    if (
      stored &&
      stored.trim().length > 0
    ) {
      return stored;
    }

    const newId =
      generateUUID();

    localStorage.setItem(
      STORAGE_KEY,
      newId,
    );

    return newId;
  } catch {
    return _sessionFallbackId;
  }
}