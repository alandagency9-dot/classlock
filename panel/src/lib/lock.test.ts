import { test } from 'node:test';
import assert from 'node:assert/strict';
import { classLockState, deviceLockState, cleanSchedule, cleanSites, toMinutes, type ScheduleSlot } from './lock.ts';

// Wednesday 11 Nov 2026, local time.
const wed = (h: number, m: number) => new Date(2026, 10, 11, h, m);
const weekdays: ScheduleSlot[] = [1, 2, 3, 4, 5].map((day) => ({ day, start: '08:00', end: '14:30' }));

test('toMinutes', () => {
  assert.equal(toMinutes('08:30'), 510);
  assert.equal(toMinutes('8:05'), 485);
  assert.equal(toMinutes('24:00'), 1440);
  assert.equal(toMinutes('24:01'), null);
  assert.equal(toMinutes('12:60'), null);
  assert.equal(toMinutes('noon'), null);
});

test('schedule locks only during class time', () => {
  const c = { lock_mode: 'schedule' as const, lock_until: null, schedule: weekdays };
  assert.equal(classLockState(c, wed(7, 59)).locked, false);
  assert.equal(classLockState(c, wed(8, 0)).locked, true);
  assert.equal(classLockState(c, wed(14, 29)).locked, true);
  assert.equal(classLockState(c, wed(14, 30)).locked, false);
  // Saturday 14 Nov 2026 is not in the schedule.
  assert.equal(classLockState(c, new Date(2026, 10, 14, 10, 0)).locked, false);
});

test('teacher override wins until it expires', () => {
  const until = wed(12, 0).toISOString();
  const unlocked = { lock_mode: 'unlocked' as const, lock_until: until, schedule: weekdays };
  assert.equal(classLockState(unlocked, wed(10, 0)).locked, false);
  assert.equal(classLockState(unlocked, wed(12, 1)).locked, true); // back to the schedule
  const locked = { lock_mode: 'locked' as const, lock_until: null, schedule: [] };
  assert.equal(classLockState(locked, wed(20, 0)).locked, true);
});

test('laptop override wins over class', () => {
  const classOpen = { locked: false, reason: '' };
  assert.equal(deviceLockState('locked', classOpen).locked, true);
  assert.equal(deviceLockState('unlocked', { locked: true, reason: '' }).locked, false);
  assert.equal(deviceLockState('class', classOpen).locked, false);
});

test('cleanSchedule validates rows', () => {
  assert.deepEqual(cleanSchedule([{ day: 1, start: '8:00', end: '9:30' }]), [{ day: 1, start: '08:00', end: '09:30' }]);
  assert.throws(() => cleanSchedule([{ day: 1, start: '10:00', end: '09:00' }]), /after the start/);
  assert.throws(() => cleanSchedule([{ day: 9, start: '10:00', end: '11:00' }]), /choose a day/);
});

test('cleanSites accepts hosts and URLs, rejects junk', () => {
  assert.deepEqual(cleanSites([' classroom.google.com ', '', 'https://example.org/quiz', 'classroom.google.com']), [
    'classroom.google.com',
    'https://example.org/quiz',
  ]);
  assert.throws(() => cleanSites(['*']), /not a website/);
  assert.throws(() => cleanSites(['hello world']), /not a website/);
  assert.throws(() => cleanSites(['localhost']), /not a website/);
});
