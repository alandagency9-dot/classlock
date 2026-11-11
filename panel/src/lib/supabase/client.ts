import { createClient } from '@supabase/supabase-js';
import { TEACHER_TOKEN_COOKIE } from './session';

const url = process.env.NEXT_PUBLIC_SUPABASE_URL;
const anonKey = process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY;

if (!url || !anonKey) {
  throw new Error('NEXT_PUBLIC_SUPABASE_URL and NEXT_PUBLIC_SUPABASE_ANON_KEY must be set in .env.local');
}

/** Browser client, signed in as the teacher. Every read and write goes through
 * row-level security, so a teacher only ever sees their own classes. */
export const supabase = createClient(url, anonKey);

// Server Functions verify the caller from this cookie (see requireTeacher.ts).
// SameSite=Strict keeps other sites from sending it, which blocks CSRF.
if (typeof document !== 'undefined') {
  supabase.auth.onAuthStateChange((_event, session) => {
    const secure = location.protocol === 'https:' ? '; Secure' : '';
    document.cookie = session
      ? `${TEACHER_TOKEN_COOKIE}=${session.access_token}; Path=/; Max-Age=${session.expires_in ?? 3600}; SameSite=Strict${secure}`
      : `${TEACHER_TOKEN_COOKIE}=; Path=/; Max-Age=0; SameSite=Strict${secure}`;
  });
}
