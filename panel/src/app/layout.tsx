import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: 'ClassLock',
  description: 'Lock school laptops to the websites the teacher allows',
};

export default function RootLayout({ children }: LayoutProps<'/'>) {
  return (
    <html lang="en" className="h-full antialiased">
      <body className="min-h-full flex flex-col">{children}</body>
    </html>
  );
}
