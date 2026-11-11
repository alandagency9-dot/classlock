/** Cookie that carries the signed-in teacher's Supabase access token to Server
 * Functions. The browser client writes it; requireTeacher reads it. */
export const TEACHER_TOKEN_COOKIE = 'teacher_token';
