import { forwardRef } from 'react';
import type { Mode, Theme, View } from '../lib/prefs';
import { Link } from '../lib/router';
import { NameSearch } from './NameSearch';

interface Props {
  placeLabel: string;
  onChangeLocation: () => void;
  mode: Mode; onMode: (m: Mode) => void; showExecutives: boolean;
  view: View; onView: (v: View) => void; showMapView: boolean;
  theme: Theme; onTheme: (t: Theme) => void;
  radii: number[]; radius: number; onRadius: (r: number) => void;
  sectors: string[]; sector: string; onSector: (s: string) => void;
  hqOnly: boolean; onHqOnly: (b: boolean) => void;
  isSample: boolean;
}

const NEXT_THEME: Record<Theme, Theme> = { auto: 'light', light: 'dark', dark: 'auto' };
const THEME_LABEL: Record<Theme, string> = { auto: 'Auto', light: 'Light', dark: 'Dark' };

function ThemeIcon({ theme }: { theme: Theme }) {
  if (theme === 'light') return <svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="3.2" fill="none" stroke="currentColor" strokeWidth="1.5" /><path d="M8 1v1.8M8 13.2V15M1 8h1.8M13.2 8H15M3 3l1.3 1.3M11.7 11.7 13 13M3 13l1.3-1.3M11.7 4.3 13 3" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" /></svg>;
  if (theme === 'dark') return <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M13.5 10.2A6 6 0 0 1 5.8 2.5a6 6 0 1 0 7.7 7.7z" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" /></svg>;
  return <svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="6.2" fill="none" stroke="currentColor" strokeWidth="1.5" /><path d="M8 1.8a6.2 6.2 0 0 1 0 12.4z" fill="currentColor" /></svg>;
}

export function ThemeButton({ theme, onTheme }: { theme: Theme; onTheme: (t: Theme) => void }) {
  return (
    <button className="themebtn" onClick={() => onTheme(NEXT_THEME[theme])} aria-label={`Theme: ${THEME_LABEL[theme]}. Click to change.`}>
      <ThemeIcon theme={theme} /><span>{THEME_LABEL[theme]}</span>
    </button>
  );
}

/** The header on a company or executive page: the name (back to the search) and the theme. */
export const PageBar = forwardRef<HTMLElement, { theme: Theme; onTheme: (t: Theme) => void; isSample: boolean; showExecutives: boolean }>(function PageBar(p, ref) {
  return (
    <header className="top" ref={ref}>
      <div className="wrap">
        <div className="bar pane">
          <div className="bar-row">
            <Link className="wordmark" to="/">Company<span>Paisa</span></Link>
            {p.isSample && <span className="sample-badge" title="Figures are synthetic sample data">Sample data</span>}
            <span className="spacer" />
            <NameSearch showExecutives={p.showExecutives} />
            <ThemeButton theme={p.theme} onTheme={p.onTheme} />
          </div>
        </div>
      </div>
    </header>
  );
});

export const TopBar = forwardRef<HTMLElement, Props>(function TopBar(p, ref) {
  return (
    <header className="top" ref={ref}>
      <div className="wrap">
        <div className="bar pane">
          <div className="bar-row">
            <Link className="wordmark" to="/">Company<span>Paisa</span></Link>
            <div className="where">Near <b>{p.placeLabel}</b> <button className="linkbtn" onClick={p.onChangeLocation}>Change</button></div>
            {p.isSample && <span className="sample-badge" title="Figures are synthetic sample data">Sample data</span>}
            <span className="spacer" />
            <NameSearch showExecutives={p.showExecutives} />
            {p.showExecutives && (
              <div className="chips" role="group" aria-label="Look up">
                <button aria-pressed={p.mode === 'companies'} onClick={() => p.onMode('companies')}>Companies</button>
                <button aria-pressed={p.mode === 'executives'} onClick={() => p.onMode('executives')}>Executives</button>
              </div>
            )}
            {p.showMapView && p.mode === 'companies' && (
              <div className="chips" role="group" aria-label="View">
                <button aria-pressed={p.view === 'map'} onClick={() => p.onView('map')}>
                  <svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="6.5" fill="none" stroke="currentColor" strokeWidth="1.4" /><circle cx="8" cy="8" r="3" fill="none" stroke="currentColor" strokeWidth="1.4" /><circle cx="8" cy="8" r="1.2" fill="currentColor" /></svg>Map
                </button>
                <button aria-pressed={p.view === 'list'} onClick={() => p.onView('list')}>
                  <svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="3" cy="4" r="1.3" fill="currentColor" /><circle cx="3" cy="8" r="1.3" fill="currentColor" /><circle cx="3" cy="12" r="1.3" fill="currentColor" /><path d="M6.5 4h7M6.5 8h7M6.5 12h7" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" /></svg>List
                </button>
              </div>
            )}
            <ThemeButton theme={p.theme} onTheme={p.onTheme} />
          </div>
          <div className="bar-row">
            <span className="lbl">Within</span>
            <div className="chips" role="group" aria-label="Search radius">
              {p.radii.map(r => <button key={r} aria-pressed={r === p.radius} onClick={() => p.onRadius(r)}>{r} mi</button>)}
            </div>
            <div className="sel-wrap">
              <select aria-label="Sector" value={p.sector} onChange={e => p.onSector(e.target.value)}>
                <option value="">All sectors</option>
                {p.sectors.map(s => <option key={s} value={s}>{s}</option>)}
              </select>
            </div>
            {p.mode === 'companies' && (
              <label className="switch"><input type="checkbox" checked={p.hqOnly} onChange={e => p.onHqOnly(e.target.checked)} /> Headquartered here only</label>
            )}
          </div>
        </div>
      </div>
    </header>
  );
});
