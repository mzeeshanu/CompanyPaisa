import { useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import type { CoverageArea } from '../api/types';
import { DISCLAIMER } from '../lib/disclaimer';

export interface Origin { latitude: number; longitude: number; label: string }

interface Props {
  coverageMiles: number;
  coverage: CoverageArea[];
  onLocated: (origin: Origin) => void;
}

const SHOWN_METROS = 6;
const US_ZIP = /^\d{5}$/;
const UK_POSTCODE = /^[A-Z]{1,2}\d[A-Z\d]?(\s*\d[A-Z]{2})?$/i;
const COUNTRY_NAMES = { US: 'United States', UK: 'United Kingdom' } as const;

/** First screen: blurred page behind a small card asking for location, a US ZIP or a UK postcode. */
export function LocationGate({ coverageMiles, coverage, onLocated }: Props) {
  const [zip, setZip] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState<'geo' | 'zip' | null>(null);
  const [allMetros, setAllMetros] = useState(false);
  const countries = [...new Set(coverage.map(c => c.country ?? 'US'))];
  // A British browser starts on the UK areas.
  const [country, setCountry] = useState<'US' | 'UK'>(() =>
    countries.includes('UK') && /-GB$/i.test(navigator.language) ? 'UK' : 'US');
  const input = useRef<HTMLInputElement>(null);
  const inCountry = coverage.filter(c => (c.country ?? 'US') === country);
  const example = inCountry[0]?.exampleZip ?? coverage[0]?.exampleZip ?? '84043';
  const metros = allMetros ? inCountry : inCountry.slice(0, SHOWN_METROS);

  useEffect(() => { const t = setTimeout(() => input.current?.focus(), 300); return () => clearTimeout(t); }, []);

  // Make sure there is something to show before closing the gate.
  async function accept(latitude: number, longitude: number, label: string) {
    const probe = await api.near({ latitude, longitude, radiusMiles: coverageMiles, pageSize: 1 });
    if (probe.totalCount === 0) {
      setError(`We don't cover your area yet — there are no companies within ${coverageMiles} miles. Pick one of the areas below.`);
      return;
    }
    onLocated({ latitude, longitude, label });
  }

  function submitZip(e: FormEvent) {
    e.preventDefault();
    return lookupZip(zip.trim());
  }

  async function lookupZip(q: string) {
    q = q.toUpperCase();
    if (!US_ZIP.test(q) && !UK_POSTCODE.test(q)) { setError('Enter a 5-digit US ZIP code or a UK postcode (e.g. SW1A 1AA).'); return; }
    setBusy('zip'); setError('');
    try {
      const hit = await api.lookup(q);
      await accept(hit.point.latitude, hit.point.longitude, `${hit.city}, ${hit.state} ${hit.postalCode ?? q}`);
    } catch (err) {
      setError(err instanceof ApiError && err.status === 404
        ? `We don't recognise ${q}. Try another, or pick an area below.`
        : 'Something went wrong looking that up. Please try again.');
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
        <p className="eyebrow">Public companies{coverage.length > 1 ? ` · ${coverage.length} areas in the ${countries.join(' & ')}` : ''}</p>
        <h1 id="gateTitle">Who's making money around you?</h1>
        <p className="lede">See the public companies near you, how big they are and where they're heading. Your location stays in your browser.</p>
        <button className="primary wide" onClick={useMyLocation} disabled={busy !== null}>
          <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true"><path d="M8 1.5c-2.8 0-5 2.1-5 4.9C3 10 8 14.5 8 14.5s5-4.5 5-8.1c0-2.8-2.2-4.9-5-4.9z" fill="none" stroke="currentColor" strokeWidth="1.6" /><circle cx="8" cy="6.4" r="1.8" fill="currentColor" /></svg>
          {busy === 'geo' ? 'Locating…' : 'Use my location'}
        </button>
        <div className="or">OR</div>
        <form className="zip" onSubmit={submitZip}>
          <input ref={input} maxLength={8} placeholder={countries.includes('UK') ? 'ZIP / postcode' : 'ZIP'} aria-label="US ZIP code or UK postcode"
            autoComplete="postal-code" autoCapitalize="characters" spellCheck={false}
            value={zip} onChange={e => { setZip(e.target.value.replace(/[^A-Za-z0-9 ]/g, '').toUpperCase()); setError(''); }} />
          <button className="ghost" type="submit" disabled={busy !== null}>{busy === 'zip' ? '…' : 'Go'}</button>
        </form>
        <p className="err" role="alert">{error}</p>
        <p className="fine gate-note" role="note">{DISCLAIMER}</p>
        {coverage.length > 0 ? (
          <div className="metros">
            <div className="metros-h">
              <p className="fine">Or jump to an area we cover:</p>
              {countries.length > 1 && (
                <div className="country-tabs" role="tablist" aria-label="Country">
                  {countries.map(c => (
                    <button key={c} role="tab" type="button" aria-selected={country === c} title={COUNTRY_NAMES[c]}
                      onClick={() => { setCountry(c); setAllMetros(false); }}>{c}</button>
                  ))}
                </div>
              )}
            </div>
            <div className="metro-list">
              {metros.map(m => (
                <button key={m.name} type="button" disabled={busy !== null} onClick={() => { setZip(m.exampleZip); lookupZip(m.exampleZip); }}>{m.name}</button>
              ))}
              {inCountry.length > SHOWN_METROS && (
                <button type="button" className="more" onClick={() => setAllMetros(a => !a)}>
                  {allMetros ? 'Fewer' : `+${inCountry.length - SHOWN_METROS} more`}
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
