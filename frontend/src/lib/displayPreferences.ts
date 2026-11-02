export type ReadingSize = 'standard' | 'larger' | 'largest';
export type MotionPreference = 'system' | 'reduced';
export interface DisplayPreferences { readingSize: ReadingSize; motion: MotionPreference }
export const displayPreferencesKey = 'rigmd.display-preferences.v1';
export const defaultDisplayPreferences: Readonly<DisplayPreferences> = { readingSize: 'standard', motion: 'system' };
type PreferenceStorage = Pick<Storage, 'getItem' | 'setItem'>;

export function normalizeDisplayPreferences(value: unknown): DisplayPreferences {
  const input = value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown> : {};
  return {
    readingSize: input.readingSize === 'larger' || input.readingSize === 'largest' ? input.readingSize : 'standard',
    motion: input.motion === 'reduced' ? 'reduced' : 'system',
  };
}

export function readDisplayPreferences(storage: PreferenceStorage | null): DisplayPreferences {
  try { return normalizeDisplayPreferences(JSON.parse(storage?.getItem(displayPreferencesKey) ?? 'null')); }
  catch { return { ...defaultDisplayPreferences }; }
}

export function saveDisplayPreferences(storage: PreferenceStorage | null, preferences: DisplayPreferences): boolean {
  if (!storage) return false;
  try { storage.setItem(displayPreferencesKey, JSON.stringify(normalizeDisplayPreferences(preferences))); return true; }
  catch { return false; }
}

export function shouldReduceMotion(preferences: DisplayPreferences, systemReducesMotion: boolean): boolean {
  return preferences.motion === 'reduced' || systemReducesMotion;
}
