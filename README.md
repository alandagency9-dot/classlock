# ClassLock

Locks school laptops. The teacher locks and unlocks them from a website. While a laptop is locked, only Google Chrome runs, and Chrome opens only the websites the teacher allows. Students can never install apps.

It has three parts:

| Part | Folder | What it is |
|---|---|---|
| Database | `supabase/` | Its own Supabase project (do not use the Kuber project) |
| Teacher panel | `panel/` | A Next.js website on Vercel. The teacher makes classes, sets websites and class times, and locks or unlocks laptops |
| Laptop app | `agent/` | `ClassLockSetup.exe`. IT runs it once on each laptop |

## How it works

1. The teacher creates the teacher account in the panel. This works only once; after that, sign-up is closed.
2. The teacher creates a class and gets a class code, for example `K7M2QX`.
3. IT runs `ClassLockSetup.exe` on each laptop as administrator.
4. The ClassLock window opens. Someone types the class code one time. The laptop then shows up in the panel. There are no student accounts.
5. The teacher presses **Lock all** / **Unlock all**, or **Lock now** / **Unlock now** for one class, or sets class times. While locked:
   - Chrome fills the whole screen (kiosk mode): no tabs, no address bar, no close button.
   - The desktop, taskbar and Start menu are gone. Other programs, Edge, Task Manager, Settings and Notepad are closed at once.
   - The keys that close Chrome or switch programs do nothing (Alt+F4, Ctrl+W, Alt+Tab, the Windows key, ...).
   - Chrome opens only the allowed websites. Downloads, incognito, guest mode, extensions and developer tools are off.
   - If Chrome closes anyway, it opens again within about 3 seconds.
   - On unlock, Chrome closes and the normal desktop comes back.
6. All the time (locked or not):
   - Programs run only from `C:\Windows` and `C:\Program Files`. Downloaded installers, portable apps, apps from USB drives and apps in the user's folders are closed right away.
   - The Microsoft Store, `winget`, `msiexec`, `cmd`, PowerShell and script hosts are blocked.
   - Chrome extensions are blocked.
7. A connected laptop cannot change its class by itself. To move it, the teacher clicks **Remove** in the panel, then the new class code is typed on the laptop.

Administrator accounts are never blocked, so IT can still install and fix things.

## Requirements

- Windows 10 or 11 laptops owned by the school.
- **Students sign in to Windows with standard (not administrator) accounts.** This is required. An administrator can remove any software, including this one.
- Google Chrome installed for all users (`C:\Program Files\Google\Chrome`). Setup stops if it is missing.
- Internet on the laptops. When the internet is down, the laptop keeps the last rules it received, and the class times still work.

## Setup

### 1. Database

- **New project:** in the Supabase **SQL Editor**, run all of `supabase/schema.sql`.
- **Project that already ran the first schema.sql:** run `supabase/migration_01_code_only.sql` once.

In the SQL Editor, the source next to **Run** must be **Primary Database**, not **Logs**.

Keys: **Project Settings > API Keys > Legacy API Keys** (`anon` and `service_role`).

### 2. Teacher panel

`panel/.env.local` (never commit it):

```
NEXT_PUBLIC_SUPABASE_URL=https://<project>.supabase.co
NEXT_PUBLIC_SUPABASE_ANON_KEY=<anon key>
SUPABASE_SERVICE_ROLE_KEY=<service_role key, secret>
```

Local: `cd panel`, `npm install`, `npm run dev`, then open http://localhost:3000.

Online (Vercel), from the `panel` folder:

```powershell
npx vercel login          # sign in or create a free Vercel account
npx vercel link           # answer: yes, your account, new project "classlock", ./, no changes
# Vercel website > classlock project > Settings > Environment Variables:
#   add the 3 lines from .env.local (you can paste them all into the first Key box)
npx vercel --prod         # prints the panel's web address
```

Open the address and go to `/signup` to create the teacher account.

### 3. Laptop app

```powershell
cd agent
copy config.example.json config.json   # Supabase URL and the anon key
powershell -ExecutionPolicy Bypass -File build.ps1
```

This builds `agent\dist\ClassLockSetup.exe`. Copy only that one file to the laptops.

- `config.json` holds the **anon** key only. Never put the `service_role` key in it.
- Install: right-click `ClassLockSetup.exe` > Run as administrator. Running a newer setup over an old one updates it.
- Install on many laptops (Intune, Group Policy, PDQ): `ClassLockSetup.exe /quiet`
- Remove: `ClassLockSetup.exe /uninstall` (as administrator).

## Before you use it in a class

Test on **one** laptop first, signed in to Windows with a standard account:

1. Install, type the class code. The laptop shows up in the panel.
2. Press **Lock all**. Within 10 seconds Chrome opens on the class website, and other sites are blocked.
3. Try to open Edge, Task Manager, a downloaded `.exe` and a program from a USB drive. Each one closes.
4. Press **Unlock all**. The laptop works normally, but downloaded programs still do not run.
5. Check that the programs the school needs still run when unlocked (Office, and so on).

If a needed program is blocked, an administrator adds its file name or full path to `C:\ProgramData\ClassLock\extra-allowed.txt` and restarts the **ClassLock Agent** service. What the agent closed is logged in `C:\ProgramData\ClassLock\agent.log`.

## Known limits

- **Anyone with a class code can add a laptop to that class.** Keep codes inside the school. **Make new code** in the panel stops the old code; connected laptops stay.
- **One teacher account.** Sign-up closes after the first account.
- **OneDrive and other apps installed only for one user** (in `AppData`) are blocked. Install them for all users, or add them to `extra-allowed.txt`.
- **Sites that need another site.** If the class website signs in with Google, also allow `accounts.google.com`. If a page looks broken, add the other site it loads.
- **Ctrl+Alt+Del cannot be blocked** by any program. It only offers lock, sign out and Task Manager, and Task Manager closes at once.
- **If a student ends the ClassLock app** (possible while unlocked), the service starts it again within about a second.
- **Administrator accounts are never locked**, and an administrator can remove ClassLock. The panel shows "Admin Windows account: NOT protected" for such a laptop.
- **Blocking is fast, but it is not instant.** A blocked program can show for less than a second before it closes. For the strongest protection on Windows Education or Enterprise, IT can also turn on AppLocker with its default rules.
- **Time zone.** Class times use the laptop's clock. Students cannot change it, because standard users cannot set the time.

## Tests

```powershell
cd agent;  powershell -ExecutionPolicy Bypass -File build.ps1        # 64 agent checks
.\agent\obj\ClassLockTests.exe --simulate                            # dry run: what would be closed on THIS PC now
cd panel;  npm test; npx eslint; npm run build
```
