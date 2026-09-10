import { createClient } from '@supabase/supabase-js';

const supabaseUrl = import.meta.env.VITE_SUPABASE_URL?.trim() ?? '';
const supabaseAnonKey =
  import.meta.env.VITE_SUPABASE_ANON_KEY?.trim() ?? '';

const isDatabaseConnectionString =
  /^postgres(?:ql)?:\/\//i.test(supabaseUrl);

export const supabaseConfigError = (() => {
  if (!supabaseUrl || !supabaseAnonKey) {
    return 'Supabase authentication is not configured yet.';
  }

  if (isDatabaseConnectionString) {
    return 'VITE_SUPABASE_URL must use the public project URL, not a database connection string.';
  }

  if (!/^https:\/\/.+\.supabase\.co$/i.test(supabaseUrl)) {
    return 'VITE_SUPABASE_URL must look like https://your-project-ref.supabase.co.';
  }

  return '';
})();

export const hasSupabaseConfig = !supabaseConfigError;

export const supabase = hasSupabaseConfig
  ? createClient(supabaseUrl, supabaseAnonKey)
  : null;

export function isEmailVerified(user) {
  return Boolean(user?.email_confirmed_at || user?.confirmed_at);
}

export function getAuthRedirectUrl(path) {
  if (typeof window === 'undefined') {
    return path;
  }

  return `${window.location.origin}${path}`;
}
