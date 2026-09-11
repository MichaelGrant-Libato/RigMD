import { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
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

function RegisterPage() {
  const navigate = useNavigate();
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [submittedEmail, setSubmittedEmail] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

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
        isEmailVerified(data.user) ? '/download' : '/verify-email',
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
  }, [navigate]);

  const handleSubmit = async (event) => {
    event.preventDefault();
    setError('');

    if (!supabase) {
      setError(supabaseConfigError);
      return;
    }

    const cleanEmail = email.trim();
    const cleanName = fullName.trim();

    if (!cleanName) {
      setError('Please provide your full name.');
      return;
    }

    if (password.length < 6) {
      setError('Use a password with at least 6 characters.');
      return;
    }

    if (password !== confirmPassword) {
      setError('The passwords do not match.');
      return;
    }

    setIsSubmitting(true);

    try {
      const options = {
        emailRedirectTo: getAuthRedirectUrl('/verify-email'),
        data: {
          full_name: cleanName,
        },
      };

      const { data, error: signUpError } =
        await supabase.auth.signUp({
          email: cleanEmail,
          password,
          options,
        });

      if (signUpError) {
        setError(signUpError.message);
        return;
      }

      // Supabase returns an empty identities array if the user already exists
      // to prevent email enumeration. We must check for this explicitly.
      if (data?.user?.identities?.length === 0) {
        setError('An account with this email address already exists. Please log in.');
        return;
      }

      if (data.user && isEmailVerified(data.user)) {
        navigate('/download', { replace: true });
        return;
      }

      setSubmittedEmail(cleanEmail);
    } finally {
      setIsSubmitting(false);
    }
  };

  if (submittedEmail) {
    return (
      <AuthFormShell
        title="Check your email"
        subtitle="Supabase sent a verification link for your RigMD account."
        footer={
          <>
            Already verified?{' '}
            <Link
              to="/login"
              className="font-semibold text-[var(--rigmd-accent)] hover:text-[var(--rigmd-accent-strong)]"
            >
              Log in
            </Link>
          </>
        }
      >
        <div className="rounded-lg border border-[var(--rigmd-success)]/35 bg-[var(--rigmd-success-soft)] p-4">
          <div className="flex gap-3">
            <CheckCircle2
              size={22}
              className="mt-0.5 shrink-0 text-[var(--rigmd-success)]"
            />
            <div>
              <p className="font-semibold text-white">
                Verification email sent
              </p>
              <p className="mt-1 text-sm leading-6 text-[var(--rigmd-text-soft)]">
                Open the link sent to {submittedEmail}, then return to
                log in and download RigMD.
              </p>
            </div>
          </div>
        </div>

        <div className="mt-5 flex flex-col gap-3 sm:flex-row">
          <Link
            to={`/verify-email?email=${encodeURIComponent(
              submittedEmail
            )}`}
            className="inline-flex flex-1 items-center justify-center gap-2 rounded-lg border border-[var(--rigmd-border-soft)] bg-[var(--rigmd-main-surface)] px-4 py-3 text-sm font-semibold text-[var(--rigmd-text-soft)] transition hover:border-[var(--rigmd-accent)]/45"
          >
            <Mail size={18} />
            Verify email
          </Link>

          <Link
            to="/login"
            className="inline-flex flex-1 items-center justify-center rounded-lg bg-[var(--rigmd-accent)] px-4 py-3 text-sm font-semibold text-slate-950 transition hover:brightness-110"
          >
            Log in
          </Link>
        </div>
      </AuthFormShell>
    );
  }

  return (
    <AuthFormShell
      title="Create account"
      subtitle="Register before accessing the RigMD installer."
      footer={
        <>
          Already have an account?{' '}
          <Link
            to="/login"
            className="font-semibold text-[var(--rigmd-accent)] hover:text-[var(--rigmd-accent-strong)]"
          >
            Log in
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

        <label className="block">
          <span className="text-sm font-medium text-[var(--rigmd-text-soft)]">
            Full name
          </span>
          <input
            value={fullName}
            onChange={(event) => setFullName(event.target.value)}
            autoComplete="name"
            className="mt-2 w-full rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-main-surface)] px-4 py-3 text-sm text-white outline-none transition placeholder:text-[var(--rigmd-text-faint)] focus:border-[var(--rigmd-accent)]"
            placeholder="Your name"
          />
        </label>

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
            autoComplete="new-password"
            required
            minLength={6}
            className="mt-2 w-full rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-main-surface)] px-4 py-3 text-sm text-white outline-none transition placeholder:text-[var(--rigmd-text-faint)] focus:border-[var(--rigmd-accent)]"
            placeholder="At least 6 characters"
          />
        </label>

        <label className="block">
          <span className="text-sm font-medium text-[var(--rigmd-text-soft)]">
            Confirm password
          </span>
          <input
            type="password"
            value={confirmPassword}
            onChange={(event) =>
              setConfirmPassword(event.target.value)
            }
            autoComplete="new-password"
            required
            minLength={6}
            className="mt-2 w-full rounded-lg border border-[var(--rigmd-border)] bg-[var(--rigmd-main-surface)] px-4 py-3 text-sm text-white outline-none transition placeholder:text-[var(--rigmd-text-faint)] focus:border-[var(--rigmd-accent)]"
            placeholder="Repeat password"
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
          Create account
        </button>
      </form>
    </AuthFormShell>
  );
}

export default RegisterPage;
