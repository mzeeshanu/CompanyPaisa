import type { ReactNode } from 'react';

/** A period or year that opens the filing its figures came from (when we have the link). */
export function Filing({ href, children }: { href: string | null | undefined; children: ReactNode }) {
  return href
    ? <a className="linkbtn filing" href={href} target="_blank" rel="noreferrer" title="Open the filing these figures come from">{children}</a>
    : <>{children}</>;
}
