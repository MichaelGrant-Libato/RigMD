import { useEffect, useMemo, useState } from 'react';
import {
  Link,
  useLocation,
  useNavigate,
} from 'react-router-dom';
import { AlertCircle, Loader2 } from 'lucide-react';

import AuthFormShell from '../components/AuthFormShell';
import {
  isEmailVerified,
  supabase,
  supabaseConfigError,
} from '../lib/supabase';

const authRoutes = new Set([
  '/login',
  '/register',
  '/verify-email',
]);

function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const [email, setEmail] = useState(
    location.state?.email ?? ''
  );
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  const redirectTo = useMemo(() => {
    const fromPath = location.state?.from?.pathname;
    return fromPath && !authRoutes.has(fromPath)
      ? fromPath
      : '/download';
  }, [location.state]);

  useEffect(() => {
    if (!supabase) {
      return undefined;
    }

    let mounted = true;

    const redirectSignedInUser = async () => {
      const { data } = await supabase.auth.getUser();

      if (!mounted || !data.user) {
        return;
      }

      navigate(
        isEmailVerified(data.user) ? redirectTo : '/verify-email',
        {
          replace: true,
          state: { email: data.user.email },
        }
      );
    };

    redirectSignedInUser();

    return () => {
      mounted = false;
    };
  }, [navigate, redirectTo]);

  const handleSubmit = async (event) => {
    event.preventDefault();
    setError('');

    if (!supabase) {
      setError(supabaseConfigError);
      return;
    }

    const cleanEmail = email.trim();
    setIsSubmitting(true);

    try {
      const { data, error: signInError } =
        await supabase.auth.signInWithPassword({
          email: cleanEmail,
          password,
        });

      if (signInError) {
        if (
          signInError.message
            .toLowerCase()
            .includes('email not confirmed')
        ) {
          navigate(
            `/verify-email?email=${encodeURIComponent(cleanEmail)}`,
            {
              replace: true,
              state: {
                email: cleanEmail,
                reason: 'email_not_confirmed',
              },
            }
          );
          return;
        }

        setError(signInError.message);
        return;
      }

      if (!isEmailVerified(data.user)) {
        navigate(
          `/verify-email?email=${encodeURIComponent(cleanEmail)}`,
          {
            replace: true,
            state: {
              email: cleanEmail,
              reason: 'email_not_confirmed',
            },
          }
        );
        return;
      }

      navigate(redirectTo, { replace: true });
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <AuthFormShell
      title="Log in"
      subtitle="Use your verified RigMD account to access the installer."
      footer={
        <>
          Need an account?{' '}
          <Link
            to="/register"
            className="font-semibold text-[var(--rigmd-accent)] hover:text-[var(--rigmd-accent-strong)]"
          >
            Register
          </Link>
        </>
      }
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        {error && (
          <div className="flex gap-3 rounded-lg border border-[var(--rigmd-danger)]/35 bg-[var(--rigmd-danger-soft)] p-4 text-sm text-[var(--rigmd-text-soft)]">
            <AlertCircle
              size={19}
              className="mt-0.5 shrink-0 text-[var(--rigmd-danger)]"
            />
            <span>{error}</span>
          </div>
        )}

        {location.search.includes('verified=1') && (
          <div className="rounded-lg border border-[var(--rigmd-success)]/35 bg-[var(--rigmd-success-soft)] p-4 text-sm text-[var(--rigmd-text-soft)]">
            Your email is verified. Log in to continue.
          </div>
        )}

        <label className="block">
          <span className="text-sm font-medium text-[var(--rigmd-text-soft)]">
            Email
          </span>
          <input
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            autoComplete="email"
            required
            className="mt-2 w-full rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-main-surface)] px-4 py-3 text-sm text-white outline-none transition placeholder:text-[var(--rigmd-text-faint)] focus:border-[var(--rigmd-accent)]"
            placeholder="you@example.com"
          />
        </label>

        <label className="block">
          <span className="text-sm font-medium text-[var(--rigmd-text-soft)]">
            Password
          </span>
          <input
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            autoComplete="current-password"
            required
            className="mt-2 w-full rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-main-surface)] px-4 py-3 text-sm text-white outline-none transition placeholder:text-[var(--rigmd-text-faint)] focus:border-[var(--rigmd-accent)]"
            placeholder="Your password"
          />
        </label>

        <button
          type="submit"
          disabled={isSubmitting}
          className="inline-flex w-full items-center justify-center gap-2 rounded-lg bg-[var(--rigmd-accent)] px-4 py-3 font-semibold text-slate-950 transition hover:brightness-110 disabled:cursor-not-allowed disabled:opacity-70"
        >
          {isSubmitting && (
            <Loader2 size={18} className="animate-spin" />
          )}
          Log in
        </button>

        <div className="text-center text-sm">
          <Link
            to={`/verify-email${
              email.trim()
                ? `?email=${encodeURIComponent(email.trim())}`
                : ''
            }`}
            className="font-medium text-[var(--rigmd-text-muted)] hover:text-[var(--rigmd-accent)]"
          >
            Need a new verification email?
          </Link>
        </div>
      </form>
    </AuthFormShell>
  );
}

export default LoginPage;
