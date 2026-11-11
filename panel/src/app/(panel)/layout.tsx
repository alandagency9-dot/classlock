'use client';

import { useEffect } from 'react';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { supabase } from '@/lib/supabase/client';
import { useTeacher } from '@/lib/useTeacher';

const NAV = [
  { href: '/classes', label: 'Classes' },
];

export default function PanelLayout({ children }: { children: React.ReactNode }) {
  const router = useRouter();
  const pathname = usePathname();
  const { loading, teacher } = useTeacher();

  useEffect(() => {
    if (!loading && !teacher) router.replace('/login');
  }, [loading, teacher, router]);

  if (loading || !teacher) {
    return <div className="min-h-screen flex items-center justify-center text-muted text-sm">Loading…</div>;
  }

  async function signOut() {
    await supabase.auth.signOut();
    router.replace('/login');
  }

  return (
    <div className="min-h-screen">
      <header className="bg-surface border-b border-border">
        <div className="max-w-6xl mx-auto px-4 h-14 flex items-center gap-4">
          <span className="font-bold">ClassLock</span>
          <nav className="flex gap-1">
            {NAV.map((item) => (
              <Link key={item.href} href={item.href}
                className={`px-3 py-1.5 rounded-lg text-sm font-medium ${
                  pathname.startsWith(item.href) ? 'bg-brand-soft text-brand' : 'text-muted hover:bg-surface-alt'
                }`}>
                {item.label}
              </Link>
            ))}
          </nav>
          <div className="ml-auto flex items-center gap-3 text-sm">
            <span className="text-muted hidden sm:inline">{teacher.full_name}</span>
            <button className="btn btn-ghost" onClick={signOut}>Sign out</button>
          </div>
        </div>
      </header>
      <main className="max-w-6xl mx-auto p-4 sm:p-6">{children}</main>
    </div>
  );
}
