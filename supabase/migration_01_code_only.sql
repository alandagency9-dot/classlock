-- ClassLock update 1
-- Laptops now join with only the class code. There are no student accounts.
--
-- Run this ONCE in the SQL Editor of the ClassLock project.
-- Next to the Run button, the source must be "Primary Database", not "Logs".
-- (A new project does not need this file: schema.sql already has these changes.)

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

-- Remove the student logins and the students table.
delete from auth.users where id in (select id from public.students);
alter table public.devices drop column student_id;
drop table public.students;
