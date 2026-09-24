/**
 * The site's own analytics: tells our API about page views and a few clicks. Searches and the company / executive pages are
 * recorded by the API itself. No cookies, no third parties, and nothing is ever shown as an error to the visitor.
 */
export type ClientEvent =
  | 'page_view'
  | 'location_gps' | 'location_zip' | 'location_area' | 'location_link'
  | 'name_search'
  | 'mode_companies' | 'mode_executives'
  | 'fact_next' | 'fact_info' | 'executives_more' | 'companies_more'
  | 'about_open' | 'privacy_open' | 'report_open';

const ENDPOINT = '/api/v1/events';
let pageViewSent = false;

export function track(name: ClientEvent, subject?: string): void {
  if (location.pathname.startsWith('/admin')) return;
  // React's development mode mounts twice; count the visit once.
  if (name === 'page_view') {
    if (pageViewSent) return;
    pageViewSent = true;
  }
  const body = JSON.stringify({
    name,
    subject,
    path: name === 'page_view' ? location.pathname : undefined,
    referrer: name === 'page_view' && document.referrer ? document.referrer : undefined,
  });
  try {
    // sendBeacon survives the visitor leaving the page and never holds anything up.
    if (navigator.sendBeacon?.(ENDPOINT, new Blob([body], { type: 'application/json' }))) return;
  } catch { /* fall back to fetch */ }
  fetch(ENDPOINT, { method: 'POST', body, headers: { 'Content-Type': 'application/json' }, keepalive: true }).catch(() => {});
}
