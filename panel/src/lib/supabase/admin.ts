import 'server-only';
import { createClient } from '@supabase/supabase-js';

/** Service-role client. It bypasses row-level security and can create and
 * delete login accounts. `server-only` makes any import from a Client
 * Component a build error, so the key cannot leak to the browser.
 * Use it only in files that start with 'use server'. */
export const supabaseAdmin = createClient(
  process.env.NEXT_PUBLIC_SUPABASE_URL!,
  process.env.SUPABASE_SERVICE_ROLE_KEY!,
  { auth: { autoRefreshToken: false, persistSession: false } },
);
