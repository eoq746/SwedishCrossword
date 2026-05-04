import type { ScoreEntry } from './types';
import { formatLeaderboardTime } from './usePuzzleGame';

const MEDALS = ['🥇', '🥈', '🥉'];

interface PuzzleLeaderboardSectionProps {
  puzzleDate: string;
  leaderboard: ScoreEntry[];
  height?: number;
}

export function PuzzleLeaderboardSection({ puzzleDate, leaderboard, height }: PuzzleLeaderboardSectionProps) {
  return (
    <div
      className="leaderboard-section"
      aria-label="Topplista"
      style={{
        height: height ? `${height}px` : undefined,
        minHeight: height ? `${height}px` : undefined,
        maxHeight: height ? `${height}px` : undefined,
      }}
    >
      <h2>Topplista</h2>
      <ul className="leaderboard-list" id="leaderboard-list" role="list">
        {leaderboard.length === 0 ? (
          <li className="leaderboard-empty">Ingen har klarat korsordet än...</li>
        ) : (
          leaderboard.map((entry, index) => {
            const hintTotal = (entry.hintsUsed ?? 0) + (entry.wordHintsUsed ?? 0);
            return (
              <li key={`${entry.name}-${entry.time}-${index}`} className={`leaderboard-item${index < 3 ? ` rank-${index + 1}` : ''}`}>
                <span className="leaderboard-rank">{index < 3 ? MEDALS[index] : `${index + 1}.`}</span>
                <span className="leaderboard-name">
                  {entry.name}
                  {entry.userId ? <span className="verified-badge" title="Verifierat konto">✓</span> : <span className="guest-badge" title="Gäst">👤</span>}
                  {hintTotal > 0 ? (
                    <span className="hint-badge" title={`Ledtrådar: ${entry.hintsUsed ?? 0} bokstav, ${entry.wordHintsUsed ?? 0} ord`}>
                      💡{hintTotal}
                    </span>
                  ) : null}
                </span>
                <span className="leaderboard-time">{formatLeaderboardTime(entry.time)}</span>
              </li>
            );
          })
        )}
      </ul>
      <div className="leaderboard-date" id="leaderboard-date">
        Korsord: {puzzleDate}
      </div>
    </div>
  );
}
