-- ClassLock database.
-- Run this ONCE in the SQL editor of a NEW Supabase project (do not use the
-- Kuber project). It creates the tables, the row-level security rules and the
-- two functions that the Windows agent calls.

create extension if not exists pgcrypto with schema extensions;

-- ---------------------------------------------------------------------------
-- Tables
-- ---------------------------------------------------------------------------

-- One row per teacher. Created by the panel's sign-up action (service role).
create table public.teachers (
  id uuid primary key references auth.users (id) on delete cascade,
  full_name text not null check (length(full_name) between 1 and 100),
  created_at timestamptz not null default now()
);

-- Short join codes without look-alike characters (no 0/O, 1/I/L).
create or replace function public.new_join_code()
returns text
language sql
volatile
set search_path = ''
as $$
  select string_agg(
    substr('ABCDEFGHJKMNPQRSTUVWXYZ23456789', 1 + floor(random() * 31)::int, 1),
    ''
  )
  from generate_series(1, 6);
$$;

-- A class. Students join it on a laptop by typing its join code.
--   allowed_sites: the only websites Chrome may open while the class is locked.
--                  The first entry is the page Chrome opens on.
--   schedule:      class times, e.g. [{"day":1,"start":"08:00","end":"14:30"}]
--                  day: 0 = Sunday ... 6 = Saturday, laptop local time.
--   lock_mode:     'schedule' = lock during class times only,
--                  'locked' / 'unlocked' = teacher override.
--   lock_until:    when an override ends and the schedule takes over again
--                  (null = until the teacher changes it).
create table public.classes (
  id uuid primary key default gen_random_uuid(),
  teacher_id uuid not null references public.teachers (id) on delete cascade,
  name text not null check (length(name) between 1 and 100),
  join_code text not null unique default public.new_join_code(),
  allowed_sites text[] not null default '{}'
    check (cardinality(allowed_sites) <= 20),
  schedule jsonb not null default '[]'::jsonb
    check (jsonb_typeof(schedule) = 'array'),
  lock_mode text not null default 'schedule'
    check (lock_mode in ('schedule', 'locked', 'unlocked')),
  lock_until timestamptz,
  created_at timestamptz not null default now()
);
create index classes_teacher_id_idx on public.classes (teacher_id);

-- One row per laptop.
--   machine_id:  Windows MachineGuid, so one laptop is one row.
--   secret_hash: sha256 of a random secret that only the laptop's SYSTEM
--                service knows. The agent proves who it is with it.
--   lock_mode:   'class' = follow the class, or a per-laptop override.
--   is_locked:   what the laptop reported in its last heartbeat.
create table public.devices (
  id uuid primary key default gen_random_uuid(),
  machine_id text not null unique,
  class_id uuid not null references public.classes (id) on delete cascade,
  hostname text not null,
  secret_hash text not null,
  lock_mode text not null default 'class'
    check (lock_mode in ('class', 'locked', 'unlocked')),
  is_locked boolean not null default false,
  agent_version text,
  last_seen timestamptz,
  registered_at timestamptz not null default now()
);
create index devices_class_id_idx on public.devices (class_id);

-- ---------------------------------------------------------------------------
-- Row-level security
-- Teachers use the panel with their own login. They see only their own data.
-- Laptops never read tables directly; the agent uses the functions below.
-- ---------------------------------------------------------------------------

alter table public.teachers enable row level security;
alter table public.classes enable row level security;
alter table public.devices enable row level security;

revoke all on public.teachers, public.classes, public.devices from anon;
revoke all on public.teachers, public.classes, public.devices from authenticated;

-- teachers: read own row, rename self.
grant select on public.teachers to authenticated;
grant update (full_name) on public.teachers to authenticated;
create policy teachers_select_self on public.teachers
  for select to authenticated using (id = (select auth.uid()));
create policy teachers_update_self on public.teachers
  for update to authenticated
  using (id = (select auth.uid())) with check (id = (select auth.uid()));

-- classes: full control of own classes (teacher_id can never change).
grant select, insert, delete on public.classes to authenticated;
grant update (name, join_code, allowed_sites, schedule, lock_mode, lock_until)
  on public.classes to authenticated;
create policy classes_teacher_all on public.classes
  for all to authenticated
  using (teacher_id = (select auth.uid()))
  with check (teacher_id = (select auth.uid()));

-- devices: read (without the secret hash), change the override, remove.
grant select (id, machine_id, class_id, hostname, lock_mode,
              is_locked, agent_version, last_seen, registered_at)
  on public.devices to authenticated;
grant update (lock_mode) on public.devices to authenticated;
grant delete on public.devices to authenticated;
create policy devices_teacher_select on public.devices
  for select to authenticated using (
    exists (select 1 from public.classes c
            where c.id = devices.class_id and c.teacher_id = (select auth.uid())));
create policy devices_teacher_update on public.devices
  for update to authenticated
  using (exists (select 1 from public.classes c
                 where c.id = devices.class_id and c.teacher_id = (select auth.uid())))
  with check (exists (select 1 from public.classes c
                      where c.id = devices.class_id and c.teacher_id = (select auth.uid())));
create policy devices_teacher_delete on public.devices
  for delete to authenticated using (
    exists (select 1 from public.classes c
            where c.id = devices.class_id and c.teacher_id = (select auth.uid())));

-- ---------------------------------------------------------------------------
-- Agent functions
-- ---------------------------------------------------------------------------

-- Called once by the laptop service when someone types the class code
-- (anon key, no login). Puts the laptop in the class with that code.
-- If the laptop is already registered, the call must carry the same secret.
-- So nobody can take over another laptop's row; to move a reinstalled laptop,
-- the teacher removes it in the panel first.
create or replace function public.register_device(
  p_join_code text,
  p_machine_id text,
  p_hostname text,
  p_secret text
)
returns json
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_class public.classes;
  v_existing public.devices;
  v_hash text;
  v_device_id uuid;
begin
  if p_secret is null or length(p_secret) < 32 then
    raise exception 'Invalid device secret.';
  end if;
  if p_machine_id is null or length(p_machine_id) not between 8 and 100 then
    raise exception 'Invalid machine id.';
  end if;

  select * into v_class from public.classes
  where join_code = upper(trim(coalesce(p_join_code, '')));
  if not found then
    raise exception 'Class code not found. Check the code with your teacher.';
  end if;

  v_hash := encode(extensions.digest(p_secret, 'sha256'), 'hex');

  select * into v_existing from public.devices where machine_id = p_machine_id;
  if found then
    if v_existing.secret_hash <> v_hash then
      raise exception 'This laptop is already registered. Ask your teacher to remove it in the panel, then try again.';
    end if;
    update public.devices
      set class_id = v_class.id,
          hostname = left(coalesce(p_hostname, 'laptop'), 100),
          last_seen = now()
      where id = v_existing.id
      returning id into v_device_id;
  else
    insert into public.devices (machine_id, class_id, hostname, secret_hash, last_seen)
    values (p_machine_id, v_class.id, left(coalesce(p_hostname, 'laptop'), 100), v_hash, now())
    returning id into v_device_id;
  end if;

  return json_build_object('device_id', v_device_id, 'class_name', v_class.name);
end;
$$;

revoke all on function public.register_device(text, text, text, text) from public;
grant execute on function public.register_device(text, text, text, text) to anon, authenticated;

-- Called every few seconds by the laptop service (anon key + device secret).
-- Saves "I am online and locked / unlocked" and returns the class rules.
create or replace function public.device_heartbeat(
  p_device_id uuid,
  p_secret text,
  p_is_locked boolean,
  p_version text
)
returns json
language plpgsql
security definer
set search_path = ''
as $$
declare
  v_device public.devices;
  v_class public.classes;
begin
  select * into v_device from public.devices
  where id = p_device_id
    and secret_hash = encode(extensions.digest(coalesce(p_secret, ''), 'sha256'), 'hex');
  if not found then
    return json_build_object('registered', false);
  end if;

  update public.devices
    set last_seen = now(),
        is_locked = coalesce(p_is_locked, false),
        agent_version = left(p_version, 20)
    where id = v_device.id;

  select * into v_class from public.classes where id = v_device.class_id;

  return json_build_object(
    'registered', true,
    'class_name', v_class.name,
    'allowed_sites', v_class.allowed_sites,
    'schedule', v_class.schedule,
    'class_lock_mode', v_class.lock_mode,
    'class_lock_until', v_class.lock_until,
    'device_lock_mode', v_device.lock_mode,
    'server_time', now()
  );
end;
$$;

revoke all on function public.device_heartbeat(uuid, text, boolean, text) from public;
grant execute on function public.device_heartbeat(uuid, text, boolean, text) to anon, authenticated;

-- Teachers need EXECUTE because it is the default of classes.join_code.
revoke all on function public.new_join_code() from public, anon;
grant execute on function public.new_join_code() to authenticated;
