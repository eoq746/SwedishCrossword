import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { fetchPuzzleDates, type DateSizeMap } from '../api/calendar';
import { usePageTitle } from '../hooks/usePageTitle';
import { useSEO } from '../hooks/useSEO';
import { generateBreadcrumbSchema } from '../utils/seoSchemas';
import '../styles/static-pages.css';

const MONTHS_SV = [
  'Januari', 'Februari', 'Mars', 'April', 'Maj', 'Juni',
  'Juli', 'Augusti', 'September', 'Oktober', 'November', 'December',
];
const SIZES = ['10x10', '15x15', '17x17'] as const;
type PuzzleSize = typeof SIZES[number];

function pad(n: number) {
  return n < 10 ? `0${n}` : `${n}`;
}

function toDateStr(year: number, month: number, day: number) {
  return `${year}-${pad(month + 1)}-${pad(day)}`;
}

function getSizeFromHash(): PuzzleSize {
  const h = window.location.hash.replace('#', '');
  return (SIZES as readonly string[]).includes(h) ? (h as PuzzleSize) : '17x17';
}

export default function CalendarPage() {
  usePageTitle('Korsord-arkiv – Svenskt Korsord');
  
  useSEO({
    title: 'Arkiv',
    description: 'Bläddra i arkivet med svenska korsord från tidigare datum. Spela gamla pussel i olika storlekar (10×10, 15×15 och 17×17). Gratis korsord på svenska.',
    canonical: 'https://www.svensktkorsord.se/calendar',
    ogType: 'website',
    ogImage: 'https://www.svensktkorsord.se/android-chrome-512x512.png',
    structuredData: generateBreadcrumbSchema([
      { name: 'Hem', url: 'https://www.svensktkorsord.se/' },
      { name: 'Arkiv', url: 'https://www.svensktkorsord.se/calendar' }
    ])
  });

  const today = useRef(new Date());
  const todayStr = today.current.toISOString().split('T')[0];

  const [year, setYear] = useState(today.current.getFullYear());
  const [month, setMonth] = useState(today.current.getMonth());
  const [selectedSize, setSelectedSize] = useState<PuzzleSize>(getSizeFromHash);
  const [dateSizeMap, setDateSizeMap] = useState<DateSizeMap>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchPuzzleDates()
      .then(setDateSizeMap)
      .catch(() => setError('Kunde inte ladda tillgängliga datum.'))
      .finally(() => setLoading(false));
  }, []);

  function selectSize(size: PuzzleSize) {
    setSelectedSize(size);
    window.location.hash = size;
  }

  function prevMonth() {
    if (month === 0) { setYear(y => y - 1); setMonth(11); }
    else setMonth(m => m - 1);
  }

  function nextMonth() {
    const t = today.current;
    if (year === t.getFullYear() && month >= t.getMonth()) return;
    if (month === 11) { setYear(y => y + 1); setMonth(0); }
    else setMonth(m => m + 1);
  }

  const isCurrentMonth =
    year === today.current.getFullYear() && month >= today.current.getMonth();

  // Build the grid cells
  const cells: React.ReactNode[] = [];
  const firstDow = new Date(year, month, 1).getDay();
  // Monday-first: JS Sunday=0 → index 6, Mon=1 → index 0
  const startOffset = (firstDow + 6) % 7;
  const daysInMonth = new Date(year, month + 1, 0).getDate();

  for (let i = 0; i < startOffset; i++) {
    cells.push(<div key={`e${i}`} className="calendar-cell calendar-empty" />);
  }

  for (let day = 1; day <= daysInMonth; day++) {
    const dateStr = toDateStr(year, month, day);
    const isToday = dateStr === todayStr;
    const sizes = dateSizeMap[dateStr] ?? [];
    const hasSelectedSize = sizes.includes(selectedSize);
    const hasAnySize = sizes.length > 0;
    const isFuture = new Date(year, month, day) > today.current;

    let cellClass = 'calendar-cell';
    if (isToday) cellClass += ' calendar-today';

    if (isToday || hasSelectedSize) {
      cellClass += ' calendar-available';
      const href = isToday
        ? `/puzzle?size=${selectedSize}`
        : `/puzzle?date=${dateStr}&size=${selectedSize}`;
      cells.push(
        <div key={dateStr} className={cellClass}>
          <Link
            to={href}
             className="calendar-link"
             aria-label={`Spela ${selectedSize} korsord för ${dateStr}`}
           >
             {day}
          </Link>
         </div>
       );
    } else if (isFuture) {
      cells.push(
        <div key={dateStr} className={cellClass + ' calendar-future'}>{day}</div>
      );
    } else if (hasAnySize) {
      // Has puzzles but not for the selected size — dim, not linked
      cells.push(
        <div key={dateStr} className={cellClass + ' calendar-other-size'} title={`Tillgänglig i: ${sizes.join(', ')}`}>{day}</div>
      );
    } else {
      cells.push(
        <div key={dateStr} className={cellClass + ' calendar-unavailable'}>{day}</div>
      );
    }
  }

  return (
    <div className="page-content">
      <h1>Korsord-arkiv</h1>
      <p className="tagline">Välj ett datum för att spela ett tidigare korsord</p>

      <div className="size-tabs" role="group" aria-label="Välj storlek">
        {SIZES.map(s => (
          <button
            key={s}
            className={`size-tab${selectedSize === s ? ' active' : ''}`}
            onClick={() => selectSize(s)}
            aria-pressed={selectedSize === s}
          >
            {s.replace('x', '×')}
          </button>
        ))}
      </div>

      {error && <div className="leaderboard-error">{error}</div>}

      <section className="calendar-section">
        <div className="calendar-header">
          <button
            className="calendar-nav-btn"
            onClick={prevMonth}
            aria-label="Föregående månad"
          >
            ←
          </button>
          <h2 className="calendar-title">{MONTHS_SV[month]} {year}</h2>
          <button
            className="calendar-nav-btn"
            onClick={nextMonth}
            disabled={isCurrentMonth}
            aria-label="Nästa månad"
          >
            →
          </button>
        </div>

        {loading ? (
          <div className="leaderboard-loading">Laddar kalender…</div>
        ) : (
          <div className="calendar-grid">
            {['Mån', 'Tis', 'Ons', 'Tor', 'Fre', 'Lör', 'Sön'].map(d => (
              <div key={d} className="calendar-day-header">{d}</div>
            ))}
            {cells}
          </div>
        )}

        <div className="calendar-legend">
          <span className="legend-item">
            <span className="legend-dot legend-available" /> Korsord tillgängligt
          </span>
          <span className="legend-item">
            <span className="legend-dot legend-today" /> Idag
          </span>
        </div>
      </section>

      <div className="page-intro">
        <section>
          <h2>Om arkivet</h2>
          <p>
            Arkivet innehåller alla korsord som publicerats sedan sajten lanserades. Varje datum har
            tre versioner — <strong>Liten (10×10)</strong>, <strong>Mellan (15×15)</strong> och{' '}
            <strong>Stor (17×17)</strong> — och du väljer storlek med knapparna ovanför kalendern.
          </p>
        </section>
        <section>
          <h2>Spela i din egen takt</h2>
          <p>
            Arkivkorsord räknas inte in på den dagliga topplistan, så du kan ta din tid och lösa dem
            utan press. Dina genomförda arkivpussel sparas i din spelhistorik om du är inloggad.
          </p>
        </section>
      </div>
    </div>
  );
}
