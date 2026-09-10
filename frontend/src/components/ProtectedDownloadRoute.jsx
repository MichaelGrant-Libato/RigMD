import { useEffect, useState } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { AlertCircle, Loader2 } from 'lucide-react';

import {
  isEmailVerified,
  supabase,
  supabaseConfigError,
} from '../lib/supabase';

function AuthLoadingScreen() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-[var(--rigmd-bg)] px-6 text-[var(--rigmd-text-main)]">
      <div className="rigmd-glass flex w-full max-w-md items-center gap-4 rounded-lg border p-6">
        <Loader2
          size={24}
          className="animate-spin text-[var(--rigmd-accent)]"
        />
        <div>
          <p className="font-semibold text-white">
            Checking account access
          </p>
          <p className="mt-1 text-sm text-[var(--rigmd-text-muted)]">
            RigMD is confirming your verified session.
          </p>
        </div>
      </div>
    </div>
  );
}

function AuthConfigurationScreen() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-[var(--rigmd-bg)] px-6 text-[var(--rigmd-text-main)]">
      <div className="rigmd-glass w-full max-w-lg rounded-lg border p-7">
        <div className="mb-5 flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-lg border border-[var(--rigmd-danger)]/45 bg-[var(--rigmd-danger-soft)]">
            <AlertCircle
              size={21}
              className="text-[var(--rigmd-danger)]"
            />
          </div>
          <div>
            <p className="font-semibold text-white">
              Download access is not ready
            </p>
            <p className="text-sm text-[var(--rigmd-text-muted)]">
              {supabaseConfigError}
            </p>
          </div>
        </div>

        <div className="rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-main-surface)]/65 p-4 text-sm leading-6 text-[var(--rigmd-text-soft)]">
          Use the public Supabase project URL and anon key in the
          frontend environment. Keep database passwords and service role
          keys out of Vite variables.
        </div>
      </div>
    </div>
  );
}

function ProtectedDownloadRoute({ children }) {
  const location = useLocation();
  const [state, setState] = useState(() => ({
    loading: Boolean(supabase),
    user: null,
  }));

  useEffect(() => {
    if (!supabase) {
      return undefined;
    }

    let mounted = true;

    const loadUser = async () => {
      const { data, error } = await supabase.auth.getUser();

      if (!mounted) {
        return;
      }

      setState({
        loading: false,
        user: error ? null : data.user,
      });
    };

    loadUser();

    const { data } = supabase.auth.onAuthStateChange(() => {
      loadUser();
    });

    return () => {
      mounted = false;
      data.subscription.unsubscribe();
    };
  }, []);

  if (!supabase) {
    return <AuthConfigurationScreen />;
  }

  if (state.loading) {
    return <AuthLoadingScreen />;
  }

  if (!state.user) {
    return (
      <Navigate
        to="/login"
        replace
        state={{ from: location }}
      />
    );
  }

  if (!isEmailVerified(state.user)) {
    return (
      <Navigate
        to="/verify-email"
        replace
        state={{
          email: state.user.email,
          from: location,
        }}
      />
    );
  }

  return children;
}

export default ProtectedDownloadRoute;
