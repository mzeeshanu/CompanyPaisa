import { useEffect, useState, type AnchorHTMLAttributes, type MouseEvent } from 'react';

/**
 * The website's addresses: the location screen (/), a search (/near/84043, /near/84043/executives?radius=25), one page per
 * company (/company/AAPL) and per executive (/executive/dylan-field-2073586). Plain history API; the API serves index.html
 * for all of them.
 */
export type Route =
  | { kind: 'home'; search: { place: string; executives: boolean } | null }
  | { kind: 'company'; ticker: string }
  | { kind: 'executive'; personId: string };

export const companyPath = (ticker: string) => `/company/${encodeURIComponent(ticker.toUpperCase())}`;
export const personPath = (personId: string) => `/executive/${encodeURIComponent(personId)}`;

/**
 * A place in a search address: "me" (the visitor's own location, never their coordinates), or a postcode without spaces;
 * European, Australian and New Zealand codes carry their country ("FR-75008"), because a bare 5-digit code reads as a US ZIP.
 */
export function placeToken(postcode: string, country?: string | null): string {
  const code = postcode.replace(/\s+/g, '').toUpperCase();
  return country && ['FR', 'NL', 'IT', 'ES', 'AU', 'NZ'].includes(country) ? `${country}-${code}` : code;
}

export const searchPath = (place: string, executives: boolean) =>
  `/near/${encodeURIComponent(place)}${executives ? '/executives' : ''}`;

export function parseRoute(path: string): Route {
  const near = /^\/near\/([^/]+)(\/executives)?\/?$/.exec(path);
  if (near) return { kind: 'home', search: { place: decodeURIComponent(near[1]), executives: !!near[2] } };
  const m = /^\/(company|executive)\/([^/]+)\/?$/.exec(path);
  if (!m) return { kind: 'home', search: null };
  const id = decodeURIComponent(m[2]);
  return m[1] === 'company' ? { kind: 'company', ticker: id.toUpperCase() } : { kind: 'executive', personId: id };
}

/** How many pages deep into the site this tab is (0 = the page it was opened on), so "Back" knows whether it can go back. */
const state = () => (history.state ?? {}) as { depth?: number; prev?: string; scroll?: number };
const depth = () => state().depth ?? 0;

const listeners = new Set<() => void>();

export function navigate(path: string, { replace = false } = {}) {
  if (path === location.pathname + location.search) return;
  // Remember how far down this page the visitor was, for when they come back to it.
  history.replaceState({ ...state(), scroll: scrollY }, '');
  if (replace) history.replaceState(state(), '', path);
  else history.pushState({ depth: depth() + 1, prev: location.pathname }, '', path);
  listeners.forEach(l => l());
}

/**
 * Keeps the address in step with the search on screen (place, filters, sort) without a new history entry or a re-render:
 * the screen already shows what the address describes.
 */
export function replaceAddress(pathAndQuery: string) {
  if (pathAndQuery !== location.pathname + location.search) history.replaceState(state(), '', pathAndQuery);
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

/** Whether an address inside the site is the search (the location screen or /near/…). */
export const isSearchPath = (path: string | null) => !!path && parseRoute(path).kind === 'home';

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
