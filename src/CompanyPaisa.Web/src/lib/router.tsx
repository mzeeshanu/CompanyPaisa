import { useEffect, useState, type AnchorHTMLAttributes, type MouseEvent } from 'react';

/**
 * The website's addresses: the search (/), one page per company (/company/AAPL) and per executive
 * (/executive/dylan-field-2073586). Plain history API; the API serves index.html for all of them.
 */
export type Route =
  | { kind: 'home' }
  | { kind: 'company'; ticker: string }
  | { kind: 'executive'; personId: string };

export const companyPath = (ticker: string) => `/company/${encodeURIComponent(ticker.toUpperCase())}`;
export const personPath = (personId: string) => `/executive/${encodeURIComponent(personId)}`;

export function parseRoute(path: string): Route {
  const m = /^\/(company|executive)\/([^/]+)\/?$/.exec(path);
  if (!m) return { kind: 'home' };
  const id = decodeURIComponent(m[2]);
  return m[1] === 'company' ? { kind: 'company', ticker: id.toUpperCase() } : { kind: 'executive', personId: id };
}

/** How many pages deep into the site this tab is (0 = the page it was opened on), so "Back" knows whether it can go back. */
const state = () => (history.state ?? {}) as { depth?: number; prev?: string; scroll?: number };
const depth = () => state().depth ?? 0;

const listeners = new Set<() => void>();

export function navigate(path: string, { replace = false } = {}) {
  if (path === location.pathname) return;
  // Remember how far down this page the visitor was, for when they come back to it.
  history.replaceState({ ...state(), scroll: scrollY }, '');
  if (replace) history.replaceState(state(), '', path);
  else history.pushState({ depth: depth() + 1, prev: location.pathname }, '', path);
  listeners.forEach(l => l());
}

/** Back to where the visitor came from inside the site, or to the search when the page was opened from a link. */
export function goBack() {
  if (depth() > 0) history.back();
  else navigate('/');
}

/** How far down the current page was when the visitor left it (0 for a new page). */
export function savedScroll(): number { return state().scroll ?? 0; }

export function canGoBack() { return depth() > 0; }

/** The address the visitor came from inside the site (null when this page was opened directly). */
export function previousPath(): string | null { return depth() > 0 ? state().prev ?? null : null; }

export function useRoute(): Route {
  const [route, setRoute] = useState(() => parseRoute(location.pathname));
  useEffect(() => {
    const update = () => setRoute(parseRoute(location.pathname));
    listeners.add(update);
    addEventListener('popstate', update);
    return () => { listeners.delete(update); removeEventListener('popstate', update); };
  }, []);
  return route;
}

/** A real link (long-press, middle-click and "open in new tab" work) that moves inside the site without reloading. */
export function Link({ to, onClick, ...rest }: { to: string } & AnchorHTMLAttributes<HTMLAnchorElement>) {
  const go = (e: MouseEvent<HTMLAnchorElement>) => {
    onClick?.(e);
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    e.preventDefault();
    navigate(to);
  };
  return <a href={to} onClick={go} {...rest} />;
}
