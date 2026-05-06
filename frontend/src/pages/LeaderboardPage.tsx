import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  fetchLeaderboard,
  fetchHistory,
  fetchSizeHashes,
  formatTime,
  type ScoreEntry,
  type HistoryEntry,
  type LeaderboardResponse,
  type HistoryResponse,
  type SizeHashMap,
} from '../api/leaderboard';
import { usePageTitle } from '../hooks/usePageTitle';
import { useSEO } from '../hooks/useSEO';
import { generateBreadcrumbSchema } from '../utils/seoSchemas';
import { getTodayIso, resolveTodayEntriesForSize } from './leaderboardUtils';
import '../styles/static-pages.css';

const SIZES = ['10x10', '15x15', '17x17'] as const;
type PuzzleSize = (typeof SIZES)[number];

const MEDALS = ['🥇', '🥈', '🥉'];

const SIZE_LABELS: Record<string, string> = {
  '10x10': '10×10 — Liten',
  '15x15': '15×15 — Medel',
  '17x17': '17×17 — Stor',
};

function HintBadge({ hintsUsed, wordHintsUsed }: { hintsUsed: number; wordHintsUsed: number }) {
  const total = (hintsUsed ?? 0) + (wordHintsUsed ?? 0);
  if (total === 0) return null;
  const parts = [
    hintsUsed > 0 ? `${hintsUsed} bokstav` : '',
    wordHintsUsed > 0 ? `${wordHintsUsed} ord` : '',
  ]
    .filter(Boolean)
    .join(', ');
  return (
    <span className="hint-badge" title={`Ledtrådar: ${parts}`}>
      💡{total}
    </span>
  );
}

function PlayerBadge({ userId }: { userId: string | null }) {
  return userId ? (
    <span className="verified-badge" title="Verifierat konto">✓</span>
  ) : (
    <span className="guest-badge" title="Gäst">👤</span>
  );
}

function TodayList({ entries }: { entries: ScoreEntry[] }) {
  if (entries.length === 0) {
    return (
      <p className="leaderboard-empty-msg">
        Ingen har klarat korsordet ännu idag — bli den första! 🎉
      </p>
    );
  }
  return (
    <ul className="leaderboard-list" role="list" aria-label="Dagens topplista">
      {entries.slice(0, 10).map((entry, i) => (
        <li key={i} className={`leaderboard-item${i < 3 ? ` rank-${i + 1}` : ''}`}>
          <span className="leaderboard-rank" aria-label={`Plats ${i + 1}`}>
            {i < 3 ? MEDALS[i] : `${i + 1}.`}
          </span>
          <span className="leaderboard-name">
            {entry.name}
            <PlayerBadge userId={entry.userId} />
            <HintBadge hintsUsed={entry.hintsUsed} wordHintsUsed={entry.wordHintsUsed} />
          </span>
          <span className="leaderboard-time" aria-label={`Tid: ${entry.time} sekunder`}>
            {formatTime(entry.time)}
          </span>
        </li>
      ))}
    </ul>
  );
}

function HistoryTable({
  history,
  selectedSize,
}: {
  history: HistoryResponse;
  selectedSize: PuzzleSize;
}) {
  // Filter to selected size (entries with no puzzleSize are included as legacy data)
  const filtered: Record<string, HistoryEntry[]> = {};
  for (const [date, entries] of Object.entries(history)) {
    const matching = entries.filter(e => !e.puzzleSize || e.puzzleSize === selectedSize);
    if (matching.length > 0) filtered[date] = matching;
  }

  const sortedDates = Object.keys(filtered).sort().reverse().slice(0, 7);

  if (sortedDates.length === 0) {
    return <p className="leaderboard-empty-msg">Ingen historik för denna storlek.</p>;
  }

  return (
    <div className="admin-table-wrap">
      <table className="history-table">
        <thead>
          <tr>
            <th>Datum</th>
            <th>Namn</th>
            <th>Tid</th>
          </tr>
        </thead>
        <tbody>
          {sortedDates.map(date => {
            const top3 = [...filtered[date]].sort((a, b) => a.time - b.time).slice(0, 3);
            return top3.map((entry, i) => (
              <tr key={`${date}-${i}`}>
                <td>{i === 0 ? date : ''}</td>
                <td>
                  {MEDALS[i]} {entry.name}
                  <PlayerBadge userId={entry.userId} />
                </td>
                <td>{formatTime(entry.time)}</td>
              </tr>
            ));
          })}
        </tbody>
      </table>
    </div>
  );
}

export default function LeaderboardPage() {
  usePageTitle('Topplista');
  
  useSEO({
    title: 'Topplista',
    description: 'Se dagens topplista över snabbaste lösare av svenska korsord. Tävla mot andra spelare och se hur du placerar dig.',
    canonical: 'https://www.svensktkorsord.se/leaderboard',
    ogType: 'website',
    ogImage: 'https://www.svensktkorsord.se/android-chrome-512x512.png',
    structuredData: generateBreadcrumbSchema([
      { name: 'Hem', url: 'https://www.svensktkorsord.se/' },
      { name: 'Topplista', url: 'https://www.svensktkorsord.se/leaderboard' }
    ])
  });

  const [selectedSize, setSelectedSize] = useState<PuzzleSize>('17x17');

  const [leaderboard, setLeaderboard] = useState<LeaderboardResponse | null>(null);
  const [history, setHistory] = useState<HistoryResponse | null>(null);
  const [sizeHashes, setSizeHashes] = useState<SizeHashMap | null>(null);

  const [leaderboardError, setLeaderboardError] = useState(false);
  const [historyError, setHistoryError] = useState(false);
  const [sizeHashesError, setSizeHashesError] = useState(false);

  // Prevent state updates after unmount
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
  }, []);

  useEffect(() => {
    fetchSizeHashes()
      .then(data => { if (mounted.current) setSizeHashes(data); })
      .catch(() => {
        if (mounted.current) {
          setSizeHashesError(true);
          setSizeHashes({});
        }
      });

    fetchLeaderboard()
      .then(data => { if (mounted.current) setLeaderboard(data); })
      .catch(() => { if (mounted.current) setLeaderboardError(true); });

    fetchHistory(7)
      .then(data => { if (mounted.current) setHistory(data); })
      .catch(() => { if (mounted.current) setHistoryError(true); });
  }, []);

  const todayEntries: ScoreEntry[] = resolveTodayEntriesForSize(leaderboard, selectedSize, sizeHashes ?? {});
  const todayLeaderboardLoading = !leaderboard && !leaderboardError;
  const todaySizeLoading = sizeHashes === null && !sizeHashesError;
  const todayLeaderboardUnavailable = leaderboardError || sizeHashesError;

  return (
    <>
      <h1>🏆 Topplista</h1>
      <p className="tagline">Se vilka som har löst dagens korsord snabbast</p>

      <div className="leaderboard-page">
        {/* Size tabs */}
        <div className="size-tabs" role="tablist" aria-label="Välj korsordsformat">
          {SIZES.map(size => (
            <button
              key={size}
              role="tab"
              aria-selected={size === selectedSize}
              aria-label={SIZE_LABELS[size] ?? size}
              className={`size-tab${size === selectedSize ? ' active' : ''}`}
              onClick={() => setSelectedSize(size)}
            >
              {size.replace('x', '×')}
            </button>
          ))}
        </div>

        {/* Today */}
        <section className="leaderboard-section" aria-labelledby="today-heading">
          <h2 id="today-heading">
            <span aria-hidden="true">📅</span> Dagens topplista
          </h2>
          {(todayLeaderboardLoading || todaySizeLoading) && (
            <p className="leaderboard-loading">Laddar topplista…</p>
          )}
          {todayLeaderboardUnavailable && (
            <p className="leaderboard-error" role="alert">⚠️ Kunde inte ladda topplistan för vald storlek.</p>
          )}
          {!todayLeaderboardLoading && !todaySizeLoading && !todayLeaderboardUnavailable && (
            <TodayList entries={todayEntries} />
          )}
          {!todayLeaderboardLoading && !todaySizeLoading && !todayLeaderboardUnavailable && leaderboard && (
            <p className="leaderboard-date">
              Korsord: {getTodayIso()} — {selectedSize.replace('x', '×')}
            </p>
          )}
        </section>

        {/* History */}
        <section className="leaderboard-section" aria-labelledby="history-heading">
          <h2 id="history-heading">
            <span aria-hidden="true">📖</span> Senaste dagarna
          </h2>
          {!history && !historyError && (
            <p className="leaderboard-loading">Laddar historik…</p>
          )}
          {historyError && (
            <p className="leaderboard-error" role="alert">⚠️ Kunde inte ladda historik.</p>
          )}
          {history && <HistoryTable history={history} selectedSize={selectedSize} />}
        </section>

        <Link to="/puzzle" className="back-link">🎯 Spela dagens korsord</Link>
      </div>
    </>
  );
}
