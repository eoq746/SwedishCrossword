import { useMemo } from 'react';
import type { FriendsLeaderboardEntry } from '../../api/profile';
import type { ScoreEntry } from './types';
import { formatLeaderboardTime } from './usePuzzleGame';

const MEDALS = ['🥇', '🥈', '🥉'];

type LeaderboardTab = 'global' | 'friends';

type GlobalLeaderboardEntry = ScoreEntry & { kind: 'global' };
type FriendsTabEntry = FriendsLeaderboardEntry & { kind: 'friends' };
type LeaderboardEntry = GlobalLeaderboardEntry | FriendsTabEntry;

interface PuzzleLeaderboardSectionProps {
  puzzleDate: string;
  leaderboard: ScoreEntry[];
  friendsLeaderboard: FriendsLeaderboardEntry[];
  currentUserDisplayName?: string | null;
  activeTab: LeaderboardTab;
  onTabChange: (tab: LeaderboardTab) => void;
  showFriendsTab: boolean;
  hasFriends: boolean;
  height?: number;
}

function renderEmptyState(activeTab: LeaderboardTab, hasFriends: boolean) {
  if (activeTab === 'global')
    return <li className="leaderboard-empty">Ingen har klarat korsordet än...</li>;

  return hasFriends
    ? <li className="leaderboard-empty">Ingen i din vänlista har klarat det här korsordet än.</li>
    : <li className="leaderboard-empty">Inga vänner ännu — lägg till vänner för att jämföra tider här.</li>;
}

export function PuzzleLeaderboardSection({
  puzzleDate,
  leaderboard,
  friendsLeaderboard,
  currentUserDisplayName,
  activeTab,
  onTabChange,
  showFriendsTab,
  hasFriends,
  height,
}: PuzzleLeaderboardSectionProps) {
  const entries = useMemo<LeaderboardEntry[]>(() => {
    if (activeTab === 'friends') {
      if (!hasFriends) return [];
      return friendsLeaderboard.map(entry => ({ ...entry, kind: 'friends' as const }));
    }
    return leaderboard.map(entry => ({ ...entry, kind: 'global' as const }));
  }, [activeTab, friendsLeaderboard, hasFriends, leaderboard]);

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
      <div className="leaderboard-header-row">
        <h2>Topplista</h2>
        {showFriendsTab && (
          <div className="leaderboard-tabs" role="tablist" aria-label="Välj topplista">
            <button
              type="button"
              role="tab"
              aria-selected={activeTab === 'global'}
              className={`leaderboard-tab${activeTab === 'global' ? ' active' : ''}`}
              onClick={() => onTabChange('global')}
            >
              Global
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={activeTab === 'friends'}
              className={`leaderboard-tab${activeTab === 'friends' ? ' active' : ''}`}
              onClick={() => onTabChange('friends')}
            >
              Vänner
            </button>
          </div>
        )}
      </div>
      <ul className="leaderboard-list" id="leaderboard-list" role="list">
        {entries.length === 0 ? (
          renderEmptyState(activeTab, hasFriends)
        ) : (
          entries.map((entry, index) => {
            const hL = entry.hintsUsed ?? 0;
            const hW = entry.wordHintsUsed ?? 0;
            const hintTotal = hL + hW;
            const hintParts: string[] = [];
            if (hL > 0) hintParts.push(`${hL} bokst${hL > 1 ? 'äver' : 'av'}`);
            if (hW > 0) hintParts.push(`${hW} ord`);

            const isCurrentUser = entry.kind === 'friends'
              && !!currentUserDisplayName
              && entry.name.localeCompare(currentUserDisplayName, 'sv', { sensitivity: 'accent' }) === 0;

            const verifiedLine = entry.kind === 'global'
              ? (entry.userId ? '\n✓ Verifierat konto' : '\n👤 Gäst')
              : (isCurrentUser ? '\nDu' : '');
            const hintLine = hintParts.length > 0
              ? `\n💡 Ledtrådar: ${hintParts.join(', ')}`
              : '\n🏅 Inga ledtrådar';
            const rowTooltip = `${entry.name} — ${formatLeaderboardTime(entry.time)}${verifiedLine}${hintLine}`;

            return (
              <li
                key={`${entry.kind}-${entry.name}-${entry.time}-${index}`}
                className={`leaderboard-item${index < 3 ? ` rank-${index + 1}` : ''}${isCurrentUser ? ' current-user' : ''}`}
                title={rowTooltip}
              >
                <span className="leaderboard-rank">{index < 3 ? MEDALS[index] : `${index + 1}.`}</span>
                <span className="leaderboard-name">
                  {entry.name}
                  {entry.kind === 'global'
                    ? (entry.userId ? <span className="verified-badge" title="Verifierat konto">✓</span> : <span className="guest-badge" title="Gäst">👤</span>)
                    : (isCurrentUser ? <span className="verified-badge" title="Du">Du</span> : null)}
                  {hintTotal > 0 ? (
                    <span className="hint-badge" title={`Ledtrådar: ${hintParts.join(', ')}`}>
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
