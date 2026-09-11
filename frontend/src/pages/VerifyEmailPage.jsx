import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import {
  AlertCircle,
  CheckCircle2,
  ShieldCheck,
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
    search.has('token_hash') ||
    search.get('type') === 'signup' ||
    hash.has('access_token') ||
    hash.get('type') === 'signup'
  );
}

function getVerificationParams(location) {
  const params = new URLSearchParams(location.search);
  const type = params.get('type') || 'signup';

  return {
    tokenHash: params.get('token_hash') ?? '',
    type,
  };
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
  const [isConfirming, setIsConfirming] = useState(false);
  const signedOutAfterCallback = useRef(false);
  const verified = useRef(false);

  const callbackDetected = useMemo(
    () => hasAuthCallback(location),
    [location]
  );
  const verificationParams = useMemo(
    () => getVerificationParams(location),
    [location]
  );

  useEffect(() => {
    if (!supabase) {
      return undefined;
    }

    let mounted = true;

    const updateFromUser = async (user) => {
      if (!mounted || verified.current) {
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

    const verifyFromEmailLink = async () => {
      const search = new URLSearchParams(location.search);
      const tokenHash = search.get('token_hash');
      const type = search.get('type') || 'signup';

      if (tokenHash) {
        setStatus('checking');
        setError('');
        setMessage('');

        const { data, error: verifyError } =
          await supabase.auth.verifyOtp({
            token_hash: tokenHash,
            type,
          });

        if (!mounted) {
          return;
        }

        if (verifyError) {
          setError(
            verifyError.message ||
              'This verification link is invalid or has already been used.'
          );
          setStatus('waiting');
          return;
        }

        if (data.user?.email) {
          setEmail(data.user.email);
        }

        verified.current = true;
        setStatus('verified');
        signedOutAfterCallback.current = true;

        await supabase.auth.signOut();

        window.history.replaceState(
          null,
          '',
          '/verify-email?verified=1'
        );

        return;
      }

      if (search.has('code')) {
        const { error: exchangeError } =
          await supabase.auth.exchangeCodeForSession(
            search.get('code')
          );

        if (exchangeError && mounted) {
          setError(exchangeError.message);
          setStatus('waiting');
          return;
        }
      }

      const { data } = await supabase.auth.getUser();
      await updateFromUser(data.user);
    };

    verifyFromEmailLink();

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
  }, [callbackDetected, email, location.search]);

  const handleConfirmEmail = async () => {
    setError('');
    setMessage('');

    if (!supabase) {
      setError(supabaseConfigError);
      return;
    }

    if (!verificationParams.tokenHash) {
      setError('The verification link is missing its confirmation token.');
      return;
    }

    setIsConfirming(true);

    try {
      const { data, error: verifyError } =
        await supabase.auth.verifyOtp({
          token_hash: verificationParams.tokenHash,
          type: verificationParams.type,
        });

      if (verifyError) {
        setError(verifyError.message);
        setStatus('waiting');
        return;
      }

      if (data.user?.email && !email) {
        setEmail(data.user.email);
      }

      verified.current = true;
      setStatus('verified');
      await supabase.auth.signOut();
    } finally {
      setIsConfirming(false);
    }
  };

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
        title="Account verified"
        subtitle="Your RigMD account is verified. You can now log in."
      >
        <div className="rounded-lg border border-[var(--rigmd-success)]/35 bg-[var(--rigmd-success-soft)] p-4">
          <div className="flex gap-3">
            <CheckCircle2
              size={22}
              className="mt-0.5 shrink-0 text-[var(--rigmd-success)]"
            />
            <div>
              <p className="font-semibold text-white">
                Your account is verified
              </p>
              <p className="mt-1 text-sm leading-6 text-[var(--rigmd-text-soft)]">
                Continue to the login page and use your verified email
                to unlock the RigMD installer.
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

  if (status === 'ready_to_confirm') {
    return (
      <AuthFormShell
        title="Confirm account"
        subtitle="Click the button below to verify your RigMD account."
      >
        {error && (
          <div className="mb-4 flex gap-3 rounded-lg border border-[var(--rigmd-danger)]/35 bg-[var(--rigmd-danger-soft)] p-4 text-sm text-[var(--rigmd-text-soft)]">
            <AlertCircle
              size={19}
              className="mt-0.5 shrink-0 text-[var(--rigmd-danger)]"
            />
            <span>{error}</span>
          </div>
        )}

        <div className="rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-main-surface)] p-4">
          <div className="flex gap-3">
            <ShieldCheck
              size={22}
              className="mt-0.5 shrink-0 text-[var(--rigmd-accent)]"
            />
            <div>
              <p className="font-semibold text-white">
                Verify this email address
              </p>
              <p className="mt-1 text-sm leading-6 text-[var(--rigmd-text-soft)]">
                This confirms your email and activates installer
                download access for your RigMD account.
              </p>
            </div>
          </div>
        </div>

        <button
          type="button"
          onClick={handleConfirmEmail}
          disabled={isConfirming}
          className="mt-5 inline-flex w-full items-center justify-center gap-2 rounded-lg bg-[var(--rigmd-accent)] px-4 py-3 font-semibold text-slate-950 transition hover:brightness-110 disabled:cursor-not-allowed disabled:opacity-70"
        >
          {isConfirming && (
            <Loader2 size={18} className="animate-spin" />
          )}
          Confirm my RigMD account
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
