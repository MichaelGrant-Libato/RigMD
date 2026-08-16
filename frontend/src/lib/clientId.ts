/**
 * Persistent anonymous client ID utility.
 *
 * On first call, generates a stable UUID v4 and stores it in localStorage.
 * All subsequent calls — including after a page refresh — return the same ID.
 *
 * This ID is sent with every API request as the `X-Client-ID` header so the
 * backend can scope diagnostic data exclusively to this installation.
 */

const STORAGE_KEY = 'rigmd_client_id';

function generateUUID(): string {
  // Use the browser's native crypto API for a true UUID v4
  if (typeof crypto !== 'undefined' && crypto.randomUUID) {
    return crypto.randomUUID();
  }

  // Fallback for older environments
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (char) => {
    const random = (Math.random() * 16) | 0;
    const value = char === 'x' ? random : (random & 0x3) | 0x8;
    return value.toString(16);
  });
}

/**
 * Returns the stable client ID for this browser installation.
 * Creates and persists one if it does not already exist.
 */
export function getClientId(): string {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);

    if (stored && stored.trim().length > 0) {
      return stored;
    }

    const newId = generateUUID();
    localStorage.setItem(STORAGE_KEY, newId);
    return newId;
  } catch {
    // localStorage may be unavailable in some environments (e.g. private browsing restrictions).
    // Fall back to a session-scoped in-memory ID so the app still works.
    return _sessionFallbackId;
  }
}

// In-memory fallback used only if localStorage is unavailable
const _sessionFallbackId = generateUUID();
