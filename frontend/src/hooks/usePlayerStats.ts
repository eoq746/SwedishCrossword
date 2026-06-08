/**
 * usePlayerStats — local player statistics per puzzle size (mirrors §6 of site.js).
 *
 * Stats are stored under the 'playerStats' localStorage key and are reset whenever
 * the data reset date changes (currently 2026-04-14).
 */

export type PuzzleSize = '10x10' | '15x15' | '17x17';

export interface SizeStats {
  totalSolved: number;
  currentStreak: number;
  bestStreak: number;
  bestTime: number | null;
  totalTime: number;
  lastSolvedDate: string | null;
  solvedDates: string[];
}

export interface PlayerStats {
  sizes: Partial<Record<PuzzleSize, SizeStats>>;
  resetDate: string;
}

const PLAYER_STATS_KEY = 'playerStats';
const STATS_RESET_DATE = '2026-04-14';

function defaultSizeStats(): SizeStats {
  return {
    totalSolved: 0,
    currentStreak: 0,
    bestStreak: 0,
    bestTime: null,
    totalTime: 0,
    lastSolvedDate: null,
    solvedDates: [],
  };
}

function loadStats(): PlayerStats {
  try {
    const raw = localStorage.getItem(PLAYER_STATS_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as PlayerStats;

      if (!parsed.resetDate || parsed.resetDate < STATS_RESET_DATE) {
        const fresh: PlayerStats = { sizes: {}, resetDate: STATS_RESET_DATE };
        saveStats(fresh);
        return fresh;
      }

      // Migrate legacy flat format → per-size
      if (!parsed.sizes) {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const legacy = parsed as any;
        const migrated: PlayerStats = {
          sizes: {
            '17x17': {
              totalSolved: legacy.totalSolved || 0,
              currentStreak: legacy.currentStreak || 0,
              bestStreak: legacy.bestStreak || 0,
              bestTime: legacy.bestTime ?? null,
              totalTime: legacy.totalTime || 0,
              lastSolvedDate: legacy.lastSolvedDate || null,
              solvedDates: legacy.solvedDates || [],
            },
          },
          resetDate: STATS_RESET_DATE,
        };
        saveStats(migrated);
        return migrated;
      }

      return parsed;
    }
  } catch (e) {
    console.warn('Failed to load player stats:', e);
  }
  return { sizes: {}, resetDate: STATS_RESET_DATE };
}

function saveStats(stats: PlayerStats): void {
  try {
    localStorage.setItem(PLAYER_STATS_KEY, JSON.stringify(stats));
  } catch (e) {
    console.warn('Failed to save player stats:', e);
  }
}

function getOrCreateSizeStats(stats: PlayerStats, size: PuzzleSize): SizeStats {
  if (!stats.sizes[size]) stats.sizes[size] = defaultSizeStats();
  return stats.sizes[size]!;
}

function getTodayIso(): string {
  return new Date().toISOString().split('T')[0];
}

/** Read the stats for all sizes. */
export function readPlayerStats(): PlayerStats {
  return loadStats();
}

/**
 * Record a completed puzzle solve for the given size.
 * Safe to call multiple times for the same date — duplicate dates are ignored.
 */
export function recordPuzzleSolve(size: PuzzleSize, solveTimeSeconds: number): PlayerStats {
  const stats = loadStats();
  const s = getOrCreateSizeStats(stats, size);
  const todayStr = getTodayIso();

  if (s.solvedDates.includes(todayStr)) return stats;

  s.totalSolved = (s.totalSolved || 0) + 1;
  s.totalTime = (s.totalTime || 0) + solveTimeSeconds;

  if (s.bestTime === null || solveTimeSeconds < s.bestTime) {
    s.bestTime = solveTimeSeconds;
  }

  const yesterday = new Date();
  yesterday.setDate(yesterday.getDate() - 1);
  const yesterdayStr = yesterday.toISOString().split('T')[0];

  if (s.lastSolvedDate === yesterdayStr) {
    s.currentStreak = (s.currentStreak || 0) + 1;
  } else if (s.lastSolvedDate === todayStr) {
    // already counted — keep streak
  } else {
    s.currentStreak = 1;
  }

  s.bestStreak = Math.max(s.bestStreak || 0, s.currentStreak);
  s.lastSolvedDate = todayStr;

  if (!s.solvedDates.includes(todayStr)) s.solvedDates.push(todayStr);
  if (s.solvedDates.length > 90) s.solvedDates = s.solvedDates.slice(-90);

  saveStats(stats);
  return stats;
}
