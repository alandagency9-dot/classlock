'use client';

import { useEffect, useState } from 'react';
import { supabase } from '@/lib/supabase/client';
import type { Teacher } from '@/lib/types';

/** The signed-in teacher, or null when nobody (or a non-teacher) is signed in. */
export function useTeacher() {
  const [loading, setLoading] = useState(true);
  const [teacher, setTeacher] = useState<Teacher | null>(null);

  useEffect(() => {
    let mounted = true;

    async function hydrate() {
      const {
        data: { session },
      } = await supabase.auth.getSession();
      let row: Teacher | null = null;
      if (session) {
        const { data } = await supabase
          .from('teachers')
          .select('id, full_name')
          .eq('id', session.user.id)
          .maybeSingle();
        row = (data as Teacher | null) ?? null;
      }
      if (mounted) {
        setTeacher(row);
        setLoading(false);
      }
    }

    hydrate();
    const { data: sub } = supabase.auth.onAuthStateChange(() => {
      hydrate();
    });
    return () => {
      mounted = false;
      sub.subscription.unsubscribe();
    };
  }, []);

  return { loading, teacher };
}
