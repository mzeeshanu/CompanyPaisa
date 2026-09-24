import { useState } from 'react';
import type { Mode, Theme } from '../lib/prefs';
import { Link } from '../lib/router';
import { NameSearch } from './NameSearch';

interface Props {
  placeLabel: string;
  onChangeLocation: () => void;
  mode: Mode; onMode: (m: Mode) => void; showExecutives: boolean;
  theme: Theme; onTheme: (t: Theme) => void;
  radii: number[]; radius: number; onRadius: (r: number) => void;
  /** A whole country or state is being shown ("Texas"): no radius to pick. */
  region?: string;
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

/** A small round icon in the header's corner; the tooltip says which theme is on. */
export function ThemeButton({ theme, onTheme }: { theme: Theme; onTheme: (t: Theme) => void }) {
  const label = `Theme: ${THEME_LABEL[theme]}. Click to change.`;
  return (
    <button className="themebtn" onClick={() => onTheme(NEXT_THEME[theme])} aria-label={label} title={label}>
      <ThemeIcon theme={theme} />
    </button>
  );
}

const Wordmark = () => <Link className="wordmark" to="/">Company<span>Paisa</span></Link>;
const SampleBadge = () => <span className="sample-badge" title="Figures are synthetic sample data">Sample</span>;

/** The header on a company or executive page: the name (back to the search), the search box and the theme. */
export function PageBar(p: { theme: Theme; onTheme: (t: Theme) => void; isSample: boolean; showExecutives: boolean }) {
  return (
    <header className="top">
      <div className="wrap">
        <div className="bar pane">
          <div className="bar-row bar-page">
            <Wordmark />
            {p.isSample && <SampleBadge />}
            <NameSearch showExecutives={p.showExecutives} />
            <ThemeButton theme={p.theme} onTheme={p.onTheme} />
          </div>
        </div>
      </div>
    </header>
  );
}

/**
 * The search screen's header. A slim top line (name, place, theme) and under it the search box with
 * what to look up and the filters beside it. On phones range and sector fold behind a Filters button.
 */
export function TopBar(p: Props) {
  // "Lehi, UT 84043": phones show just the town.
  const [city, ...more] = p.placeLabel.split(',');
  const rest = more.join(',');
  // Phones: range and sector fold behind one "Filters" button (showing the range) so the line never scrolls;
  // the HQ switch stays on the line, at the right.
  const [filtersOpen, setFiltersOpen] = useState(false);
  const extra = p.sector ? 1 : 0;
  return (
    <header className="top">
      <div className="wrap">
        <div className="bar pane">
          <div className="bar-row bar-meta">
            <Wordmark />
            <button className="where" onClick={p.onChangeLocation} title="Change location">
              <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 14.5s4.8-4.3 4.8-8.1A4.8 4.8 0 0 0 3.2 6.4c0 3.8 4.8 8.1 4.8 8.1z" fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinejoin="round" /><circle cx="8" cy="6.4" r="1.7" fill="currentColor" /></svg>
              <b>{city}{rest && <span className="where-rest">,{rest}</span>}</b>
              <span className="where-change">Change</span>
            </button>
            {p.isSample && <SampleBadge />}
            <span className="spacer" />
            <ThemeButton theme={p.theme} onTheme={p.onTheme} />
          </div>
          <div className="bar-row bar-find">
            <NameSearch showExecutives={p.showExecutives} />
            <div className="bar-opts">
              {p.showExecutives && (
                <div className="chips" role="group" aria-label="Look up">
                  <button aria-pressed={p.mode === 'companies'} onClick={() => p.onMode('companies')}>Companies</button>
                  <button aria-pressed={p.mode === 'executives'} onClick={() => p.onMode('executives')}>Executives</button>
                </div>
              )}
              <button className="filters-btn" aria-expanded={filtersOpen} aria-controls="bar-filters" onClick={() => setFiltersOpen(o => !o)}>
                <svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2 4h12M4.5 8h7M7 12h2" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" /></svg>
                {p.region ? 'Filters' : `${p.radius} mi`}{extra > 0 && <span className="filters-count">+{extra}</span>}
              </button>
              <div className={`bar-filters${filtersOpen ? ' open' : ''}`} id="bar-filters">
                {p.region
                  ? <span className="region-chip" title={`Every public company in ${p.region}. Change the place to search around a ZIP code or city.`}>All of {p.region}</span>
                  : (
                    <div className="sel-wrap">
                      <select aria-label="Search radius" value={p.radius} onChange={e => p.onRadius(Number(e.target.value))}>
                        {p.radii.map(r => <option key={r} value={r}>Within {r} mi</option>)}
                      </select>
                    </div>
                  )}
                <div className="sel-wrap">
                  <select aria-label="Sector" value={p.sector} onChange={e => p.onSector(e.target.value)}>
                    <option value="">All sectors</option>
                    {p.sectors.map(s => <option key={s} value={s}>{s}</option>)}
                  </select>
                </div>
              </div>
              {p.mode === 'companies' && (
                <label className="switch hq-switch" title="Only companies headquartered in this area">
                  <input type="checkbox" checked={p.hqOnly} onChange={e => p.onHqOnly(e.target.checked)} /> <span>HQ<span className="hq-more"> here only</span></span>
                </label>
              )}
            </div>
          </div>
        </div>
      </div>
    </header>
  );
}
