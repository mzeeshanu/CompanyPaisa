import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { CoverageArea } from '../api/types';

export interface Origin { latitude: number; longitude: number; label: string }

interface Props {
  coverageMiles: number;
  coverage: CoverageArea[];
  onLocated: (origin: Origin) => void;
}

const SHOWN_METROS = 6;

/** First screen: blurred page behind a small card asking for location or ZIP. */
export function LocationGate({ coverageMiles, coverage, onLocated }: Props) {
  const [zip, setZip] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState<'geo' | 'zip' | null>(null);
  const [allMetros, setAllMetros] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  const example = coverage[0]?.exampleZip ?? '84043';
  const metros = allMetros ? coverage : coverage.slice(0, SHOWN_METROS);

  useEffect(() => { const t = setTimeout(() => input.current?.focus(), 300); return () => clearTimeout(t); }, []);

  // Make sure there is something to show before closing the gate.
  async function accept(latitude: number, longitude: number, label: string) {
    const probe = await api.near({ latitude, longitude, radiusMiles: coverageMiles, pageSize: 1 });
    if (probe.totalCount === 0) {
      setError(`We don't cover your area yet — there are no companies within ${coverageMiles} miles. Pick one of the metros below.`);
      return;
    }
    onLocated({ latitude, longitude, label });
  }

  function submitZip(e: FormEvent) {
    e.preventDefault();
    return lookupZip(zip.trim());
  }

  async function lookupZip(q: string) {
    if (!/^\d{5}$/.test(q)) { setError('Enter a 5-digit ZIP code.'); return; }
    setBusy('zip'); setError('');
    try {
      const hit = await api.lookup(q);
      await accept(hit.point.latitude, hit.point.longitude, `${hit.city}, ${hit.state} ${hit.postalCode ?? q}`);
    } catch (err) {
      setError(err instanceof ApiError && err.status === 404
        ? `We don't recognise ZIP ${q}. Try another, or pick a metro below.`
        : 'Something went wrong looking up that ZIP. Please try again.');
    } finally { setBusy(null); }
  }

  function useMyLocation() {
    // The browser permission prompt only appears after this click (never on page load).
    if (!navigator.geolocation) { setError("This browser can't share location. Enter a ZIP code instead."); return; }
    setBusy('geo'); setError('');
    navigator.geolocation.getCurrentPosition(
      async p => {
        try { await accept(p.coords.latitude, p.coords.longitude, 'your location'); }
        catch { setError('Something went wrong. Enter a ZIP code instead.'); }
        finally { setBusy(null); }
      },
      err => {
        setBusy(null);
        setError(err.code === 1 ? 'Location access was blocked. Enter a ZIP code instead.' : "We couldn't find your location. Enter a ZIP code instead.");
      },
      { timeout: 9000, maximumAge: 600000 });
  }

  return (
    <div className="gate" role="dialog" aria-modal="true" aria-labelledby="gateTitle">
      <div className="card pane">
        <svg className="mark" viewBox="0 0 52 52" aria-hidden="true">
          <defs>
            <radialGradient id="mg1" cx="34%" cy="28%" r="80%"><stop offset="0" stopColor="#fff" /><stop offset=".35" stopColor="#C9BBFF" /><stop offset="1" stopColor="#3F48E0" /></radialGradient>
            <radialGradient id="mg2" cx="34%" cy="28%" r="80%"><stop offset="0" stopColor="#fff" /><stop offset="1" stopColor="#0E9E67" /></radialGradient>
            <radialGradient id="mg3" cx="34%" cy="28%" r="80%"><stop offset="0" stopColor="#fff" /><stop offset="1" stopColor="#FFB38A" /></radialGradient>
          </defs>
          <circle cx="22" cy="28" r="16" fill="url(#mg1)" />
          <circle cx="40" cy="14" r="8" fill="url(#mg2)" />
          <circle cx="42" cy="38" r="5" fill="url(#mg3)" />
        </svg>
        <p className="eyebrow">Public companies{coverage.length > 1 ? ` · ${coverage.length} US metros` : ''}</p>
        <h1 id="gateTitle">Who's making money around you?</h1>
        <p className="lede">See the public companies near you, how big they are and where they're heading. Your location stays in your browser.</p>
        <button className="primary wide" onClick={useMyLocation} disabled={busy !== null}>
          <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true"><path d="M8 1.5c-2.8 0-5 2.1-5 4.9C3 10 8 14.5 8 14.5s5-4.5 5-8.1c0-2.8-2.2-4.9-5-4.9z" fill="none" stroke="currentColor" strokeWidth="1.6" /><circle cx="8" cy="6.4" r="1.8" fill="currentColor" /></svg>
          {busy === 'geo' ? 'Locating…' : 'Use my location'}
        </button>
        <div className="or">OR</div>
        <form className="zip" onSubmit={submitZip}>
          <input ref={input} inputMode="numeric" maxLength={5} placeholder="ZIP" aria-label="ZIP code" autoComplete="postal-code"
            value={zip} onChange={e => { setZip(e.target.value.replace(/\D/g, '')); setError(''); }} />
          <button className="ghost" type="submit" disabled={busy !== null}>{busy === 'zip' ? '…' : 'Go'}</button>
        </form>
        <p className="err" role="alert">{error}</p>
        {coverage.length > 0 ? (
          <div className="metros">
            <p className="fine">Or jump to a metro we cover:</p>
            <div className="metro-list">
              {metros.map(m => (
                <button key={m.name} type="button" disabled={busy !== null} onClick={() => { setZip(m.exampleZip); lookupZip(m.exampleZip); }}>{m.name}</button>
              ))}
              {coverage.length > SHOWN_METROS && (
                <button type="button" className="more" onClick={() => setAllMetros(a => !a)}>
                  {allMetros ? 'Fewer' : `+${coverage.length - SHOWN_METROS} more`}
                </button>
              )}
            </div>
          </div>
        ) : (
          <p className="fine">Try ZIP <b>{example}</b>.</p>
        )}
      </div>
    </div>
  );
}
