import type { HistoryRow } from './types';
import { formatLeaderboardTime } from './usePuzzleGame';

const MEDALS = ['🥇', '🥈', '🥉'];

interface PuzzleHistorySectionProps {
  historyRows: HistoryRow[];
  height?: number;
}

export function PuzzleHistorySection({ historyRows, height }: PuzzleHistorySectionProps) {
  return (
    <div
      className="leaderboard-section history-section"
      id="history-section"
      style={{
        height: height ? `${height}px` : undefined,
        minHeight: height ? `${height}px` : undefined,
        maxHeight: height ? `${height}px` : undefined,
      }}
    >
      <h2>Historisk Topplista</h2>
      <ul className="leaderboard-list" id="history-list">
        {historyRows.length === 0 ? (
          <li className="leaderboard-empty">Ingen historik tillgänglig ännu.</li>
        ) : (
          historyRows.map(([date, entries]) => {
            const top3 = [...entries].sort((a, b) => a.time - b.time).slice(0, 3);
            return (
              <li key={date} className="history-date-group">
                <h4 className="history-date-heading">{date}</h4>
                <ul className="history-entries">
                  {top3.map((entry, idx) => (
                    <li key={`${date}-${entry.name}-${idx}`} className={`leaderboard-item history-item${idx < 3 ? ` rank-${idx + 1}` : ''}`}>
                      <span className="leaderboard-rank">{idx < 3 ? MEDALS[idx] : `${idx + 1}.`}</span>
                      <span className="leaderboard-name">
                        {entry.name}
                        {entry.userId ? <span className="verified-badge" title="Verifierat konto">✓</span> : <span className="guest-badge" title="Gäst">👤</span>}
                      </span>
                      <span className="leaderboard-time">{formatLeaderboardTime(entry.time)}</span>
                    </li>
                  ))}
                </ul>
              </li>
            );
          })
        )}
      </ul>
    </div>
  );
}
