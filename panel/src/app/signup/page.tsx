'use client';

import { FormEvent, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { supabase } from '@/lib/supabase/client';
import { signUpTeacher, signupOpen } from '@/lib/actions';

export default function SignupPage() {
  const router = useRouter();
  const [open, setOpen] = useState<boolean | null>(null);
  const [form, setForm] = useState({ fullName: '', email: '', password: '' });
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    signupOpen().then(setOpen, () => setOpen(true));
  }, []);

  const set = (key: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm({ ...form, [key]: e.target.value });

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    setError(null);
    const result = await signUpTeacher(form);
    if (!result.ok) {
      setError(result.error ?? 'Could not create the account.');
      setLoading(false);
      return;
    }
    const { error: signInError } = await supabase.auth.signInWithPassword({ email: form.email, password: form.password });
    if (signInError) {
      router.replace('/login');
      return;
    }
    router.replace('/classes');
  }

  if (open === null) {
    return <div className="min-h-screen flex items-center justify-center text-muted text-sm">Loading…</div>;
  }

  if (!open) {
    return (
      <div className="min-h-screen w-full flex items-center justify-center px-4">
        <div className="card w-full max-w-sm space-y-4 p-8">
          <h1 className="text-xl font-bold">The teacher account already exists</h1>
          <p className="text-muted text-sm">Sign-up is closed. Sign in with the teacher email and password.</p>
          <Link href="/login" className="btn w-full text-center block">Sign in</Link>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen w-full flex items-center justify-center px-4">
      <form onSubmit={onSubmit} className="card w-full max-w-sm space-y-4 p-8">
        <div>
          <h1 className="text-xl font-bold">Create the teacher account</h1>
          <p className="text-muted text-sm mt-1">You do this one time. After that, sign-up closes.</p>
        </div>
        <div className="space-y-1.5">
          <label htmlFor="name" className="text-sm text-muted">Your name</label>
          <input id="name" className="input" required value={form.fullName} onChange={set('fullName')} />
        </div>
        <div className="space-y-1.5">
          <label htmlFor="email" className="text-sm text-muted">Email</label>
          <input id="email" className="input" type="email" autoComplete="username" required
            value={form.email} onChange={set('email')} />
        </div>
        <div className="space-y-1.5">
          <label htmlFor="password" className="text-sm text-muted">Password (8 characters or more)</label>
          <input id="password" className="input" type="password" autoComplete="new-password" minLength={8} required
            value={form.password} onChange={set('password')} />
        </div>
        {error && <p className="text-sm text-danger">{error}</p>}
        <button className="btn w-full" disabled={loading}>{loading ? 'Creating…' : 'Create account'}</button>
        <p className="text-sm text-muted text-center">
          Have an account? <Link href="/login" className="text-brand font-semibold">Sign in</Link>
        </p>
      </form>
    </div>
  );
}
