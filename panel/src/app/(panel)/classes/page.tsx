'use client';

import { FormEvent, useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { supabase } from '@/lib/supabase/client';
import { classLockState } from '@/lib/lock';
import type { ClassRow } from '@/lib/types';

interface ClassWithDevices extends ClassRow {
  // Only the id column: teachers may read some device columns, not the whole row,
  // so an embedded count(*) is refused by the database.
  devices: { id: string }[];
}

export default function ClassesPage() {
  const [classes, setClasses] = useState<ClassWithDevices[]>([]);
  const [loading, setLoading] = useState(true);
  const [name, setName] = useState('');
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  const load = useCallback(async () => {
    const { data, error } = await supabase
      .from('classes')
      .select('*, devices(id)')
      .order('created_at', { ascending: true });
    if (error) setMessage({ ok: false, text: error.message });
    setClasses((data as ClassWithDevices[] | null) ?? []);
    setLoading(false);
  }, []);

  useEffect(() => {
    // Initial fetch, then refresh every 10 seconds.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    load();
    const timer = setInterval(load, 10000);
    return () => clearInterval(timer);
  }, [load]);

  async function createClass(e: FormEvent) {
    e.preventDefault();
    setMessage(null);
    const { data: auth } = await supabase.auth.getUser();
    if (!auth.user) return;
    const { error } = await supabase.from('classes').insert({ name: name.trim(), teacher_id: auth.user.id });
    if (error) {
      setMessage({ ok: false, text: error.message });
      return;
    }
    setName('');
    load();
  }

  /** Locks or unlocks every laptop in every class, and clears per-laptop rules. */
  async function setAll(locked: boolean) {
    setMessage(null);
    const { data: auth } = await supabase.auth.getUser();
    if (!auth.user) return;
    const mode = locked ? 'locked' : 'unlocked';
    const classes = await supabase
      .from('classes')
      .update({ lock_mode: mode, lock_until: null })
      .eq('teacher_id', auth.user.id);
    const devices = await supabase.from('devices').update({ lock_mode: 'class' }).neq('lock_mode', 'class');
    const error = classes.error ?? devices.error;
    setMessage(error
      ? { ok: false, text: error.message }
      : { ok: true, text: locked ? 'All laptops are locking now (within 10 seconds).' : 'All laptops are unlocking now (within 10 seconds).' });
    load();
  }

  const total = classes.reduce((n, c) => n + c.devices.length, 0);

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold">Classes</h1>
        <p className="text-muted text-sm mt-1">
          Each class has a code. Type it once in the ClassLock app on a laptop, and the laptop joins the class.
        </p>
      </div>

      <section className="card flex flex-col sm:flex-row sm:items-center gap-3">
        <div className="flex-1">
          <h2 className="font-semibold">All laptops ({total})</h2>
          <p className="text-sm text-muted">Locked laptops can only open Google Chrome on the class websites.</p>
        </div>
        <button className="btn" onClick={() => setAll(true)}>Lock all</button>
        <button className="btn btn-ghost" onClick={() => setAll(false)}>Unlock all</button>
      </section>

      <form onSubmit={createClass} className="card flex flex-col sm:flex-row gap-3">
        <input className="input" placeholder="New class name, for example Grade 8 B" required maxLength={100}
          value={name} onChange={(e) => setName(e.target.value)} />
        <button className="btn shrink-0">Create class</button>
      </form>

      {message && (
        <p className={`text-sm rounded-xl px-4 py-2 ${message.ok ? 'bg-green-50 text-ok' : 'bg-red-50 text-danger'}`}>
          {message.text}
        </p>
      )}

      {loading ? (
        <p className="text-muted text-sm">Loading…</p>
      ) : classes.length === 0 ? (
        <p className="text-muted text-sm">No classes yet. Create your first class above.</p>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {classes.map((c) => {
            const state = classLockState(c);
            return (
              <Link key={c.id} href={`/classes/${c.id}`} className="card hover:border-brand block space-y-3">
                <div className="flex items-start justify-between gap-2">
                  <h2 className="font-semibold text-lg">{c.name}</h2>
                  <span className={`text-xs font-semibold px-2 py-1 rounded-full ${
                    state.locked ? 'bg-red-100 text-danger' : 'bg-green-100 text-ok'}`}>
                    {state.locked ? 'Locked' : 'Unlocked'}
                  </span>
                </div>
                <p className="text-sm text-muted">{state.reason}</p>
                <div className="flex justify-between text-sm">
                  <span>Code: <span className="font-mono font-bold tracking-widest">{c.join_code}</span></span>
                  <span className="text-muted">{c.devices.length} laptops</span>
                </div>
              </Link>
            );
          })}
        </div>
      )}
    </div>
  );
}
