import { useEffect, useLayoutEffect, useState, type ReactNode } from 'react';
import { MotionConfig } from 'motion/react';
import { defaultDisplayPreferences, normalizeDisplayPreferences, readDisplayPreferences, saveDisplayPreferences, shouldReduceMotion, displayPreferencesKey, type DisplayPreferences } from '../lib/displayPreferences';
import { DisplayPreferencesContext } from '../lib/displayPreferencesContext';

function localStorageIfAvailable() {
  try { return window.localStorage; } catch { return null; }
}

export default function DisplayPreferencesProvider({ children }: { children: ReactNode }) {
  const [preferences, setPreferences] = useState(() => readDisplayPreferences(localStorageIfAvailable()));
  const [systemReducesMotion, setSystemReducesMotion] = useState(() => window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  const [saveStatus, setSaveStatus] = useState<'idle' | 'saved' | 'session-only'>('idle');
  const reduceMotion = shouldReduceMotion(preferences, systemReducesMotion);

  useEffect(() => {
    const query = window.matchMedia('(prefers-reduced-motion: reduce)');
    const updateSystem = () => setSystemReducesMotion(query.matches);
    query.addEventListener('change', updateSystem);
    const sync = (event: StorageEvent) => {
      if ((event.key === displayPreferencesKey || event.key === null) && event.storageArea === localStorageIfAvailable()) {
        setPreferences(readDisplayPreferences(localStorageIfAvailable()));
        setSaveStatus('idle');
      }
    };
    window.addEventListener('storage', sync);
    return () => { query.removeEventListener('change', updateSystem); window.removeEventListener('storage', sync); };
  }, []);

  useLayoutEffect(() => {
    const root = document.documentElement;
    const previousSize = root.getAttribute('data-rigmd-text-size');
    const previousMotion = root.getAttribute('data-rigmd-motion');
    root.setAttribute('data-rigmd-text-size', preferences.readingSize);
    root.setAttribute('data-rigmd-motion', reduceMotion ? 'reduced' : 'system');
    return () => {
      if (previousSize === null) root.removeAttribute('data-rigmd-text-size'); else root.setAttribute('data-rigmd-text-size', previousSize);
      if (previousMotion === null) root.removeAttribute('data-rigmd-motion'); else root.setAttribute('data-rigmd-motion', previousMotion);
    };
  }, [preferences.readingSize, reduceMotion]);

  const apply = (next: DisplayPreferences) => {
    setPreferences(next);
    setSaveStatus(saveDisplayPreferences(localStorageIfAvailable(), next) ? 'saved' : 'session-only');
  };

  return <DisplayPreferencesContext.Provider value={{ preferences, systemReducesMotion, saveStatus,
    update: change => apply(normalizeDisplayPreferences({ ...preferences, ...change })),
    reset: () => apply({ ...defaultDisplayPreferences }),
  }}>
    <MotionConfig reducedMotion={reduceMotion ? 'always' : 'never'} skipAnimations={reduceMotion}>{children}</MotionConfig>
  </DisplayPreferencesContext.Provider>;
}
