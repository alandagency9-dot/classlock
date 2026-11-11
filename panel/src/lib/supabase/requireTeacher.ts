import 'server-only';
import { cookies } from 'next/headers';
import { supabaseAdmin } from './admin';
import { TEACHER_TOKEN_COOKIE } from './session';

/**
 * Anyone on the network can POST to a Server Function directly, not only this
 * panel. Every action that uses the service-role client calls this first.
 * It verifies the access token with Supabase Auth and requires a teachers row.
 * Returns the teacher's user id, or throws.
 */
export async function requireTeacher(): Promise<string> {
  const store = await cookies();
  const token = store.get(TEACHER_TOKEN_COOKIE)?.value;
  if (!token) throw new Error('Not signed in. Sign in again.');

  const { data, error } = await supabaseAdmin.auth.getUser(token);
  if (error || !data.user) throw new Error('Your session has expired. Sign in again.');

  const { data: teacher } = await supabaseAdmin
    .from('teachers')
    .select('id')
    .eq('id', data.user.id)
    .maybeSingle();
  if (!teacher) throw new Error('Only teachers can do this.');
  return data.user.id;
}
