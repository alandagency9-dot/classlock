import type { ScheduleSlot, ClassLockMode, DeviceLockMode } from './lock';

export interface Teacher {
  id: string;
  full_name: string;
}

export interface ClassRow {
  id: string;
  teacher_id: string;
  name: string;
  join_code: string;
  allowed_sites: string[];
  schedule: ScheduleSlot[];
  lock_mode: ClassLockMode;
  lock_until: string | null;
  created_at: string;
}

export interface Device {
  id: string;
  machine_id: string;
  class_id: string;
  hostname: string;
  lock_mode: DeviceLockMode;
  is_locked: boolean;
  agent_version: string | null;
  last_seen: string | null;
  registered_at: string;
}

/** Explicit list: the secret_hash column is not readable by teachers. */
export const DEVICE_COLUMNS =
  'id, machine_id, class_id, hostname, lock_mode, is_locked, agent_version, last_seen, registered_at';
