/**
 * Lock rules. The Windows agent (agent/src/Policy.cs) uses the same rules,
 * so the panel shows the state the laptops will have.
 *
 * Order of rules:
 *   1. Laptop override ('locked' / 'unlocked') wins.
 *   2. Class override ('locked' / 'unlocked') wins while lock_until is in the
 *      future, or forever when lock_until is null.
 *   3. Otherwise the laptop is locked during a class time in the schedule.
 */

export type ClassLockMode = 'schedule' | 'locked' | 'unlocked';
export type DeviceLockMode = 'class' | 'locked' | 'unlocked';

/** day: 0 = Sunday ... 6 = Saturday. start/end: "HH:MM", 24-hour, local time. */
export interface ScheduleSlot {
  day: number;
  start: string;
  end: string;
}

export interface ClassLockInput {
  lock_mode: ClassLockMode;
  lock_until: string | null;
  schedule: ScheduleSlot[];
}

export interface LockState {
  locked: boolean;
  reason: string;
}

export const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/** "08:30" -> 510. Returns null for anything that is not a valid time. */
export function toMinutes(hhmm: string): number | null {
  const m = /^(\d{1,2}):(\d{2})$/.exec(hhmm.trim());
  if (!m) return null;
  const h = Number(m[1]);
  const min = Number(m[2]);
  if (h > 24 || min > 59 || (h === 24 && min !== 0)) return null;
  return h * 60 + min;
}

export function activeSlot(schedule: ScheduleSlot[], now: Date): ScheduleSlot | null {
  const day = now.getDay();
  const minutes = now.getHours() * 60 + now.getMinutes();
  for (const slot of schedule) {
    if (slot.day !== day) continue;
    const start = toMinutes(slot.start);
    const end = toMinutes(slot.end);
    if (start === null || end === null) continue;
    if (minutes >= start && minutes < end) return slot;
  }
  return null;
}

function shortTime(iso: string): string {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

export function classLockState(c: ClassLockInput, now: Date = new Date()): LockState {
  const overrideActive =
    c.lock_mode !== 'schedule' && (c.lock_until === null || new Date(c.lock_until).getTime() > now.getTime());
  if (overrideActive) {
    const until = c.lock_until ? ` until ${shortTime(c.lock_until)}` : '';
    return c.lock_mode === 'locked'
      ? { locked: true, reason: `Locked by teacher${until}` }
      : { locked: false, reason: `Unlocked by teacher${until}` };
  }
  const slot = activeSlot(c.schedule, now);
  return slot
    ? { locked: true, reason: `Class time (${slot.start} to ${slot.end})` }
    : { locked: false, reason: 'Outside class time' };
}

export function deviceLockState(mode: DeviceLockMode, classState: LockState): LockState {
  if (mode === 'locked') return { locked: true, reason: 'This laptop is always locked' };
  if (mode === 'unlocked') return { locked: false, reason: 'This laptop is always unlocked' };
  return classState;
}

/** Validates and cleans a schedule from the editor. Throws with a readable message. */
export function cleanSchedule(slots: ScheduleSlot[]): ScheduleSlot[] {
  return slots.map((s, i) => {
    const start = toMinutes(s.start);
    const end = toMinutes(s.end);
    if (!Number.isInteger(s.day) || s.day < 0 || s.day > 6) throw new Error(`Row ${i + 1}: choose a day.`);
    if (start === null || end === null) throw new Error(`Row ${i + 1}: use times like 08:00.`);
    if (end <= start) throw new Error(`Row ${i + 1}: the end time must be after the start time.`);
    return { day: s.day, start: s.start.trim().padStart(5, '0'), end: s.end.trim().padStart(5, '0') };
  });
}

/**
 * Cleans the allowed-sites list. Chrome's URLAllowlist accepts a host
 * ("example.com", which also allows its subdomains) or a URL prefix
 * ("https://example.com/class").
 */
export function cleanSites(lines: string[]): string[] {
  const out: string[] = [];
  for (const raw of lines) {
    const s = raw.trim();
    if (!s) continue;
    const bad = new Error(`"${s}" is not a website address.`);
    if (/\s/.test(s) || s.includes('*')) throw bad;
    let url: URL;
    try {
      url = new URL(/^https?:\/\//i.test(s) ? s : `https://${s}`);
    } catch {
      throw bad;
    }
    if (!url.hostname.includes('.')) throw bad;
    if (!out.includes(s)) out.push(s);
  }
  if (out.length > 20) throw new Error('Use at most 20 websites.');
  return out;
}
