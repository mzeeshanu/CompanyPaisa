import type { GeoPoint } from '../api/types';
import type { Mode } from '../lib/prefs';
import { canGoBack, goBack, isSearchPath, Link, previousPath, searchPath } from '../lib/router';

/** The visitor's current search, when a company or executive page was opened from it. */
export interface Nearby { point: GeoPoint; label: string; place: string; mode: Mode }

/** Straight-line miles between two points (haversine). */
export function milesBetween(a: GeoPoint, b: GeoPoint): number {
  const rad = Math.PI / 180, dLat = (b.latitude - a.latitude) * rad, dLng = (b.longitude - a.longitude) * rad;
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(a.latitude * rad) * Math.cos(b.latitude * rad) * Math.sin(dLng / 2) ** 2;
  return 3958.8 * 2 * Math.asin(Math.min(1, Math.sqrt(h)));
}

/** "← Back to companies near Lehi", or a way into the search for someone who arrived from a shared link. */
export function PageBack({ from }: { from: Nearby | null }) {
  const what = from ? `${from.mode === 'executives' ? 'executives' : 'companies'} near ${from.label.split(',')[0]}` : null;
  if (canGoBack()) {
    return <nav className="page-back"><button className="linkbtn" onClick={goBack}>← Back{what && isSearchPath(previousPath()) ? ` to ${what}` : ''}</button></nav>;
  }
  return (
    <nav className="page-back">
      <Link className="linkbtn" to={from ? searchPath(from.place, from.mode === 'executives') : '/'}>
        ← {what ? what[0].toUpperCase() + what.slice(1) : 'Find public companies near you'}
      </Link>
    </nav>
  );
}
