'use client';

import { FormEvent, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { supabase } from '@/lib/supabase/client';

export default function LoginPage() {
  const router = useRouter();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    setError(null);

    const { data, error: signInError } = await supabase.auth.signInWithPassword({ email, password });
    if (signInError || !data.user) {
      setError(signInError?.message ?? 'Could not sign in.');
      setLoading(false);
      return;
    }

    const { data: teacher } = await supabase.from('teachers').select('id').eq('id', data.user.id).maybeSingle();
    if (!teacher) {
      await supabase.auth.signOut();
      setError('This is not a teacher account. Students sign in on the laptop app.');
      setLoading(false);
      return;
    }
    router.replace('/classes');
  }

  return (
    <div className="min-h-screen w-full flex items-center justify-center px-4">
      <form onSubmit={onSubmit} className="card w-full max-w-sm space-y-5 p-8">
        <div>
          <h1 className="text-xl font-bold">ClassLock teacher panel</h1>
          <p className="text-muted text-sm mt-1">Sign in to manage your classes and laptops.</p>
        </div>
        <div className="space-y-1.5">
          <label htmlFor="email" className="text-sm text-muted">Email</label>
          <input id="email" className="input" type="email" autoComplete="username" required
            value={email} onChange={(e) => setEmail(e.target.value)} />
        </div>
        <div className="space-y-1.5">
          <label htmlFor="password" className="text-sm text-muted">Password</label>
          <input id="password" className="input" type="password" autoComplete="current-password" required
            value={password} onChange={(e) => setPassword(e.target.value)} />
        </div>
        {error && <p className="text-sm text-danger">{error}</p>}
        <button className="btn w-full" disabled={loading}>{loading ? 'Signing in…' : 'Sign in'}</button>
        <p className="text-sm text-muted text-center">
          New teacher? <Link href="/signup" className="text-brand font-semibold">Create an account</Link>
        </p>
      </form>
    </div>
  );
}
