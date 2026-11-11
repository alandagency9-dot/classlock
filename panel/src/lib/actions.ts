'use server';

import { supabaseAdmin } from '@/lib/supabase/admin';

export interface ActionResult {
  ok: boolean;
  error?: string;
}

function fail(e: unknown): ActionResult {
  return { ok: false, error: e instanceof Error ? e.message : 'Something went wrong.' };
}

function checkName(name: string): string {
  const n = name.trim();
  if (n.length < 1 || n.length > 100) throw new Error('Enter a name (1 to 100 characters).');
  return n;
}

function checkEmail(email: string): string {
  const e = email.trim().toLowerCase();
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(e)) throw new Error('Enter a valid email address.');
  return e;
}

function checkPassword(password: string): string {
  if (password.length < 8) throw new Error('The password must have at least 8 characters.');
  if (password.length > 72) throw new Error('The password must have at most 72 characters.');
  return password;
}

async function teacherExists(): Promise<boolean> {
  const { count, error } = await supabaseAdmin.from('teachers').select('id', { count: 'exact', head: true });
  if (error) throw new Error(error.message);
  return (count ?? 0) > 0;
}

/** True while nobody has made the teacher account yet. */
export async function signupOpen(): Promise<boolean> {
  return !(await teacherExists());
}

/**
 * Creates the teacher login. This works only once: when a teacher account
 * exists, sign-up is closed, so nobody else can make an account on the
 * public website.
 */
export async function signUpTeacher(input: { fullName: string; email: string; password: string }): Promise<ActionResult> {
  try {
    if (await teacherExists()) throw new Error('The teacher account already exists. Sign in instead.');

    const fullName = checkName(input.fullName);
    const email = checkEmail(input.email);
    const password = checkPassword(input.password);

    const { data, error } = await supabaseAdmin.auth.admin.createUser({
      email,
      password,
      email_confirm: true,
      app_metadata: { role: 'teacher' },
      user_metadata: { full_name: fullName },
    });
    if (error || !data.user) throw new Error(error?.message ?? 'Could not create the account.');

    const { error: rowError } = await supabaseAdmin.from('teachers').insert({ id: data.user.id, full_name: fullName });
    if (rowError) {
      await supabaseAdmin.auth.admin.deleteUser(data.user.id);
      throw new Error(rowError.message);
    }
    return { ok: true };
  } catch (e) {
    return fail(e);
  }
}
