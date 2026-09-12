// Visitor preferences (view, theme, map layer). Saved to a first-party cookie ONLY after consent.

export type View = 'list' | 'map';
export type Theme = 'auto' | 'light' | 'dark';
export type Consent = 'yes' | 'no' | null;

export interface Prefs { view: View; theme: Theme; map: boolean }

const CONSENT_KEY = 'cp_consent';
let cookieName = 'cp_prefs';
let cookieDays = 365;

export function configurePrefs(name: string, days: number) { cookieName = name; cookieDays = days; }

export function readPrefs(): Partial<Prefs> | null {
  try {
    const m = document.cookie.match(new RegExp(`(?:^|; )${cookieName}=([^;]*)`));
    if (m) return JSON.parse(decodeURIComponent(m[1]));
  } catch { /* ignore */ }
  try {
    const s = localStorage.getItem(cookieName);
    if (s) return JSON.parse(s);
  } catch { /* storage blocked */ }
  return null;
}

export function writePrefs(p: Prefs) {
  const v = JSON.stringify({ ...p, v: 1 });
  const secure = location.protocol === 'https:' ? '; Secure' : '';
  try { document.cookie = `${cookieName}=${encodeURIComponent(v)}; max-age=${cookieDays * 86400}; path=/; SameSite=Lax${secure}`; } catch { /* ignore */ }
  try { localStorage.setItem(cookieName, v); } catch { /* ignore */ }
}

export function clearPrefs() {
  try { document.cookie = `${cookieName}=; max-age=0; path=/; SameSite=Lax`; } catch { /* ignore */ }
  try { localStorage.removeItem(cookieName); } catch { /* ignore */ }
}

export function readSessionConsent(): Consent {
  try { return (sessionStorage.getItem(CONSENT_KEY) as Consent) ?? null; } catch { return null; }
}
export function writeSessionConsent(c: 'yes' | 'no') {
  try { sessionStorage.setItem(CONSENT_KEY, c); } catch { /* ignore */ }
}

export function applyTheme(theme: Theme) {
  if (theme === 'auto') document.documentElement.removeAttribute('data-mode');
  else document.documentElement.setAttribute('data-mode', theme);
}
