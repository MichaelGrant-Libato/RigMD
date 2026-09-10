import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import {
  AlertCircle,
  CheckCircle2,
  Loader2,
  Mail,
} from 'lucide-react';

import AuthFormShell from '../components/AuthFormShell';
import {
  getAuthRedirectUrl,
  isEmailVerified,
  supabase,
  supabaseConfigError,
} from '../lib/supabase';

function getEmailFromLocation(location) {
  const params = new URLSearchParams(location.search);
  return params.get('email') ?? location.state?.email ?? '';
}

function hasAuthCallback(location) {
  const search = new URLSearchParams(location.search);
  const hash = new URLSearchParams(window.location.hash.slice(1));

  return (
    search.has('code') ||
    search.get('type') === 'signup' ||
    hash.has('access_token') ||
    hash.get('type') === 'signup'
  );
}

function VerifyEmailPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const [email, setEmail] = useState(() =>
    getEmailFromLocation(location)
  );
  const [status, setStatus] = useState(() =>
    supabase ? 'checking' : 'waiting'
  );
  const [error, setError] = useState(() =>
    supabase ? '' : supabaseConfigError
  );
  const [message, setMessage] = useState('');
  const [isResending, setIsResending] = useState(false);
  const signedOutAfterCallback = useRef(false);
  const verified = useRef(false);

  const callbackDetected = useMemo(
    () => hasAuthCallback(location),
    [location]
  );

  useEffect(() => {
    if (!supabase) {
      return undefined;
    }

    let mounted = true;

    const updateFromUser = async (user) => {
      if (!mounted) {
        return;
      }

      if (user?.email && !email) {
        setEmail(user.email);
      }

      if (user && isEmailVerified(user)) {
        verified.current = true;
        setStatus('verified');

        if (
          callbackDetected &&
          !signedOutAfterCallback.current
        ) {
          signedOutAfterCallback.current = true;
          await supabase.auth.signOut();
        }

        return;
      }

      setStatus('waiting');
    };

    const checkUser = async () => {
      const { data } = await supabase.auth.getUser();
      updateFromUser(data.user);
    };

    checkUser();

    const { data } = supabase.auth.onAuthStateChange(
      (_event, session) => {
        if (verified.current) {
          return;
        }

        updateFromUser(session?.user ?? null);
      }
    );

    return () => {
      mounted = false;
      data.subscription.unsubscribe();
    };
  }, [callbackDetected, email]);

  const handleResend = async (event) => {
    event.preventDefault();
    setError('');
    setMessage('');

    if (!supabase) {
      setError(supabaseConfigError);
      return;
    }

    const cleanEmail = email.trim();

    if (!cleanEmail) {
      setError('Enter the email address you used to register.');
      return;
    }

    setIsResending(true);

    try {
      const { error: resendError } = await supabase.auth.resend({
        type: 'signup',
        email: cleanEmail,
        options: {
          emailRedirectTo: getAuthRedirectUrl('/verify-email'),
        },
      });

      if (resendError) {
        setError(resendError.message);
        return;
      }

      setMessage('A new verification email has been sent.');
    } finally {
      setIsResending(false);
    }
  };

  const handleContinueToLogin = async () => {
    if (supabase) {
      await supabase.auth.signOut();
    }

    navigate('/login?verified=1', {
      replace: true,
      state: { email: email.trim() },
    });
  };

  const footer = (
    <>
      Already verified?{' '}
      <Link
        to="/login"
        className="font-semibold text-[var(--rigmd-accent)] hover:text-[var(--rigmd-accent-strong)]"
      >
        Log in
      </Link>
    </>
  );

  if (status === 'checking') {
    return (
      <AuthFormShell
        title="Verifying email"
        subtitle="RigMD is checking your Supabase verification status."
      >
        <div className="flex items-center gap-3 rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-main-surface)] p-4 text-sm text-[var(--rigmd-text-soft)]">
          <Loader2
            size={20}
            className="animate-spin text-[var(--rigmd-accent)]"
          />
          Checking your account.
        </div>
      </AuthFormShell>
    );
  }

  if (status === 'verified') {
    return (
      <AuthFormShell
        title="Email verified"
        subtitle="Your RigMD account is ready for installer access."
      >
        <div className="rounded-lg border border-[var(--rigmd-success)]/35 bg-[var(--rigmd-success-soft)] p-4">
          <div className="flex gap-3">
            <CheckCircle2
              size={22}
              className="mt-0.5 shrink-0 text-[var(--rigmd-success)]"
            />
            <div>
              <p className="font-semibold text-white">
                Verification complete
              </p>
              <p className="mt-1 text-sm leading-6 text-[var(--rigmd-text-soft)]">
                Log in with your verified account to access the RigMD
                installer.
              </p>
            </div>
          </div>
        </div>

        <button
          type="button"
          onClick={handleContinueToLogin}
          className="mt-5 inline-flex w-full items-center justify-center rounded-lg bg-[var(--rigmd-accent)] px-4 py-3 font-semibold text-slate-950 transition hover:brightness-110"
        >
          Continue to login
        </button>
      </AuthFormShell>
    );
  }

  return (
    <AuthFormShell
      title="Verify email"
      subtitle="Use the link from Supabase before logging in."
      footer={footer}
    >
      {(error ||
        location.state?.reason === 'email_not_confirmed') && (
        <div className="mb-4 flex gap-3 rounded-lg border border-[var(--rigmd-warning)]/35 bg-[var(--rigmd-warning-soft)] p-4 text-sm text-[var(--rigmd-text-soft)]">
          <AlertCircle
            size={19}
            className="mt-0.5 shrink-0 text-[var(--rigmd-warning)]"
          />
          <span>
            {error ||
              'That account still needs email verification.'}
          </span>
        </div>
      )}

      {message && (
        <div className="mb-4 flex gap-3 rounded-lg border border-[var(--rigmd-success)]/35 bg-[var(--rigmd-success-soft)] p-4 text-sm text-[var(--rigmd-text-soft)]">
          <Mail
            size={19}
            className="mt-0.5 shrink-0 text-[var(--rigmd-success)]"
          />
          <span>{message}</span>
        </div>
      )}

      <form onSubmit={handleResend} className="space-y-4">
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

        <button
          type="submit"
          disabled={isResending}
          className="inline-flex w-full items-center justify-center gap-2 rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-main-surface)] px-4 py-3 font-semibold text-[var(--rigmd-text-soft)] transition hover:border-[var(--rigmd-accent)]/45 disabled:cursor-not-allowed disabled:opacity-70"
        >
          {isResending && (
            <Loader2 size={18} className="animate-spin" />
          )}
          Resend verification email
        </button>
      </form>
    </AuthFormShell>
  );
}

export default VerifyEmailPage;
