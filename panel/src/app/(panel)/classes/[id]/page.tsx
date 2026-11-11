'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { supabase } from '@/lib/supabase/client';
import {
  DAY_NAMES,
  classLockState,
  cleanSchedule,
  cleanSites,
  type ClassLockMode,
  type DeviceLockMode,
  type ScheduleSlot,
} from '@/lib/lock';
import { DEVICE_COLUMNS, type ClassRow, type Device } from '@/lib/types';

const ONLINE_SECONDS = 45;
const CODE_ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';

function randomCode(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(6));
  return Array.from(bytes, (b) => CODE_ALPHABET[b % CODE_ALPHABET.length]).join('');
}

function lastSeenText(iso: string | null, now: number): { online: boolean; text: string } {
  if (!iso) return { online: false, text: 'Never' };
  const seconds = Math.max(0, Math.round((now - new Date(iso).getTime()) / 1000));
  if (seconds <= ONLINE_SECONDS) return { online: true, text: 'Online' };
  if (seconds < 3600) return { online: false, text: `${Math.round(seconds / 60)} min ago` };
  if (seconds < 86400) return { online: false, text: `${Math.round(seconds / 3600)} h ago` };
  return { online: false, text: new Date(iso).toLocaleDateString() };
}

export default function ClassPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const [cls, setCls] = useState<ClassRow | null>(null);
  const [devices, setDevices] = useState<Device[]>([]);
  const [now, setNow] = useState(() => Date.now());
  const [notFound, setNotFound] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  // Editors keep their own copy so a refresh does not overwrite typing.
  const [sitesText, setSitesText] = useState('');
  const [schedule, setSchedule] = useState<ScheduleSlot[]>([]);
  const [editorsReady, setEditorsReady] = useState(false);
  const [overrideFor, setOverrideFor] = useState<'forever' | '1h' | 'today'>('forever');

  const load = useCallback(async () => {
    const [classRes, devicesRes] = await Promise.all([
      supabase.from('classes').select('*').eq('id', id).maybeSingle(),
      supabase.from('devices').select(DEVICE_COLUMNS).eq('class_id', id).order('hostname'),
    ]);
    if (!classRes.data) {
      setNotFound(true);
      return;
    }
    setCls(classRes.data as ClassRow);
    setDevices((devicesRes.data as Device[] | null) ?? []);
    setNow(Date.now());
  }, [id]);

  useEffect(() => {
    // Initial fetch, then refresh every 5 seconds so laptop status stays live.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    load();
    const timer = setInterval(load, 5000);
    return () => clearInterval(timer);
  }, [load]);

  useEffect(() => {
    if (cls && !editorsReady) {
      // One-time copy of saved values into the editors.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSitesText(cls.allowed_sites.join('\n'));
      setSchedule(cls.schedule);
      setEditorsReady(true);
    }
  }, [cls, editorsReady]);

  async function updateClass(patch: Partial<ClassRow>, okText: string) {
    setMessage(null);
    const { error } = await supabase.from('classes').update(patch).eq('id', id);
    if (error) {
      setMessage({ ok: false, text: error.message });
      return false;
    }
    setMessage({ ok: true, text: okText });
    await load();
    return true;
  }

  function overrideUntil(): string | null {
    if (overrideFor === '1h') return new Date(Date.now() + 3600_000).toISOString();
    if (overrideFor === 'today') {
      const end = new Date();
      end.setHours(23, 59, 0, 0);
      return end.toISOString();
    }
    return null;
  }

  async function setClassMode(mode: ClassLockMode) {
    const until = mode === 'schedule' ? null : overrideUntil();
    const text = mode === 'locked' ? 'Laptops are locking now.' : mode === 'unlocked' ? 'Laptops are unlocking now.' : 'Laptops follow the schedule again.';
    await updateClass({ lock_mode: mode, lock_until: until }, text);
  }

  async function saveSites() {
    try {
      const sites = cleanSites(sitesText.split('\n'));
      if (await updateClass({ allowed_sites: sites }, 'Websites saved. Laptops get them within 10 seconds.')) {
        setSitesText(sites.join('\n'));
      }
    } catch (e) {
      setMessage({ ok: false, text: (e as Error).message });
    }
  }

  async function saveSchedule() {
    try {
      const clean = cleanSchedule(schedule);
      if (await updateClass({ schedule: clean }, 'Class times saved.')) setSchedule(clean);
    } catch (e) {
      setMessage({ ok: false, text: (e as Error).message });
    }
  }

  async function newCode() {
    if (!confirm('Make a new join code? The old code stops working. Laptops already in the class stay in it.')) return;
    await updateClass({ join_code: randomCode() }, 'New join code made.');
  }

  async function deleteClass() {
    if (!cls) return;
    if (!confirm(`Delete the class "${cls.name}"? All its laptops are removed from the panel and unlock.`)) return;
    const { error } = await supabase.from('classes').delete().eq('id', id);
    if (error) {
      setMessage({ ok: false, text: error.message });
      return;
    }
    router.replace('/classes');
  }

  async function setDeviceMode(device: Device, mode: DeviceLockMode) {
    const { error } = await supabase.from('devices').update({ lock_mode: mode }).eq('id', device.id);
    if (error) setMessage({ ok: false, text: error.message });
    await load();
  }

  async function removeDevice(device: Device) {
    if (!confirm(`Remove ${device.hostname} from this class? It unlocks until someone types a class code on it again.`)) return;
    const { error } = await supabase.from('devices').delete().eq('id', device.id);
    if (error) setMessage({ ok: false, text: error.message });
    await load();
  }

  if (notFound) {
    return (
      <div className="space-y-3">
        <p>Class not found.</p>
        <Link href="/classes" className="text-brand font-semibold">Back to classes</Link>
      </div>
    );
  }
  if (!cls) return <p className="text-muted text-sm">Loading…</p>;

  const state = classLockState(cls, new Date(now));
  const onlineCount = devices.filter((d) => lastSeenText(d.last_seen, now).online).length;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-3">
        <Link href="/classes" className="text-sm text-muted hover:text-foreground">← Classes</Link>
        <h1 className="text-2xl font-bold w-full sm:w-auto">{cls.name}</h1>
        <button className="btn btn-danger sm:ml-auto" onClick={deleteClass}>Delete class</button>
      </div>

      {message && (
        <p className={`text-sm rounded-xl px-4 py-2 ${message.ok ? 'bg-green-50 text-ok' : 'bg-red-50 text-danger'}`}>
          {message.text}
        </p>
      )}

      <div className="grid gap-4 lg:grid-cols-3">
        <section className="card space-y-3">
          <h2 className="font-semibold">Join code</h2>
          <p className="font-mono text-4xl font-bold tracking-[0.3em]">{cls.join_code}</p>
          <p className="text-sm text-muted">Type this code one time in the ClassLock window on each laptop.</p>
          <button className="btn btn-ghost" onClick={newCode}>Make new code</button>
        </section>

        <section className="card space-y-3 lg:col-span-2">
          <div className="flex items-center gap-3">
            <h2 className="font-semibold">Lock</h2>
            <span className={`text-xs font-semibold px-2 py-1 rounded-full ${
              state.locked ? 'bg-red-100 text-danger' : 'bg-green-100 text-ok'}`}>
              {state.locked ? 'Locked now' : 'Unlocked now'}
            </span>
            <span className="text-sm text-muted">{state.reason}</span>
          </div>
          <p className="text-sm text-muted">
            Locked laptops can only open Google Chrome, and Chrome can only open the websites below.
            Installing apps is blocked all the time.
          </p>
          <div className="flex flex-wrap items-center gap-2">
            <button className="btn" onClick={() => setClassMode('locked')}>Lock now</button>
            <button className="btn btn-ghost" onClick={() => setClassMode('unlocked')}>Unlock now</button>
            <select className="input w-auto" value={overrideFor}
              onChange={(e) => setOverrideFor(e.target.value as typeof overrideFor)}>
              <option value="forever">until I change it</option>
              <option value="1h">for 1 hour</option>
              <option value="today">until the end of today</option>
            </select>
            {cls.lock_mode !== 'schedule' && (
              <button className="btn btn-ghost" onClick={() => setClassMode('schedule')}>Follow class times again</button>
            )}
          </div>
        </section>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <section className="card space-y-3">
          <h2 className="font-semibold">Allowed websites</h2>
          <p className="text-sm text-muted">
            One per line. The first one opens when Chrome starts. <code>example.com</code> also allows its
            subdomains. If a site needs Google sign-in, also add <code>accounts.google.com</code>.
          </p>
          <textarea className="input font-mono text-sm min-h-36" value={sitesText}
            placeholder={'classroom.google.com\naccounts.google.com'}
            onChange={(e) => setSitesText(e.target.value)} />
          {sitesText.trim() === '' && (
            <p className="text-sm text-danger">No websites yet. Locked laptops cannot open any website.</p>
          )}
          <button className="btn" onClick={saveSites}>Save websites</button>
        </section>

        <section className="card space-y-3">
          <h2 className="font-semibold">Class times</h2>
          <p className="text-sm text-muted">Laptops lock automatically during these times (laptop clock).</p>
          <div className="space-y-2">
            {schedule.map((slot, i) => (
              <div key={i} className="flex flex-wrap items-center gap-2">
                <select className="input w-auto" value={slot.day}
                  onChange={(e) => setSchedule(schedule.map((s, j) => (j === i ? { ...s, day: Number(e.target.value) } : s)))}>
                  {DAY_NAMES.map((d, n) => <option key={d} value={n}>{d}</option>)}
                </select>
                <input type="time" className="input w-auto" value={slot.start}
                  onChange={(e) => setSchedule(schedule.map((s, j) => (j === i ? { ...s, start: e.target.value } : s)))} />
                <span className="text-muted">to</span>
                <input type="time" className="input w-auto" value={slot.end}
                  onChange={(e) => setSchedule(schedule.map((s, j) => (j === i ? { ...s, end: e.target.value } : s)))} />
                <button className="text-sm text-danger px-2" onClick={() => setSchedule(schedule.filter((_, j) => j !== i))}>
                  Remove
                </button>
              </div>
            ))}
            {schedule.length === 0 && <p className="text-sm text-muted">No class times. Laptops lock only when you press Lock now.</p>}
          </div>
          <div className="flex flex-wrap gap-2">
            <button className="btn btn-ghost" onClick={() => setSchedule([...schedule, { day: 1, start: '08:00', end: '14:00' }])}>
              Add time
            </button>
            <button className="btn btn-ghost"
              onClick={() => setSchedule([1, 2, 3, 4, 5].map((day) => ({ day, start: '08:00', end: '14:00' })))}>
              Monday to Friday, 08:00 to 14:00
            </button>
            <button className="btn" onClick={saveSchedule}>Save class times</button>
          </div>
        </section>
      </div>

      <section className="card space-y-3">
        <div className="flex items-center gap-3">
          <h2 className="font-semibold">Laptops</h2>
          <span className="text-sm text-muted">{onlineCount} of {devices.length} online · refreshes every 5 seconds</span>
        </div>
        {devices.length === 0 ? (
          <p className="text-sm text-muted">
            No laptops yet. Install ClassLock on a laptop, then type the code {cls.join_code} in the ClassLock window.
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="text-left text-muted">
                <tr>
                  <th className="py-2 pr-4 font-medium">Laptop</th>
                  <th className="py-2 pr-4 font-medium">Connection</th>
                  <th className="py-2 pr-4 font-medium">State</th>
                  <th className="py-2 pr-4 font-medium">Rule for this laptop</th>
                  <th className="py-2 font-medium"></th>
                </tr>
              </thead>
              <tbody>
                {devices.map((d) => {
                  const seen = lastSeenText(d.last_seen, now);
                  return (
                    <tr key={d.id} className="border-t border-border">
                      <td className="py-2 pr-4 font-medium">{d.hostname}</td>
                      <td className="py-2 pr-4">
                        <span className={`inline-block w-2 h-2 rounded-full mr-2 ${seen.online ? 'bg-ok' : 'bg-muted'}`} />
                        {seen.text}
                      </td>
                      <td className="py-2 pr-4">{d.is_locked ? 'Locked' : 'Unlocked'}</td>
                      <td className="py-2 pr-4">
                        <select className="input w-auto py-1" value={d.lock_mode}
                          onChange={(e) => setDeviceMode(d, e.target.value as DeviceLockMode)}>
                          <option value="class">Same as class</option>
                          <option value="locked">Always locked</option>
                          <option value="unlocked">Always unlocked</option>
                        </select>
                      </td>
                      <td className="py-2 text-right">
                        <button className="text-danger text-sm" onClick={() => removeDevice(d)}>Remove</button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
