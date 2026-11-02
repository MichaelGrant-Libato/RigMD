import { createContext, useContext } from 'react';
import type { DisplayPreferences } from './displayPreferences';

export const DisplayPreferencesContext = createContext<{
  preferences: DisplayPreferences;
  systemReducesMotion: boolean;
  saveStatus: 'idle' | 'saved' | 'session-only';
  update: (change: Partial<DisplayPreferences>) => void;
  reset: () => void;
} | null>(null);

export function useDisplayPreferences() {
  const context = useContext(DisplayPreferencesContext);
  if (!context) throw new Error('Reading settings require DisplayPreferencesProvider.');
  return context;
}
