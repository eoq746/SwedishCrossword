import type { LeaderboardResponse, ScoreEntry, SizeHashMap } from '../api/leaderboard';

export function getTodayIso(): string {
  return new Date().toISOString().split('T')[0];
}

export function resolveTodayEntriesForSize(
  leaderboard: LeaderboardResponse | null,
  selectedSize: string,
  sizeHashes: SizeHashMap,
  today = getTodayIso(),
): ScoreEntry[] {
  if (!leaderboard) return [];

  const hash = sizeHashes[selectedSize];
  if (!hash) return [];

  return [...(leaderboard.scores?.[`${today}-${hash}`] ?? [])].sort((a, b) => a.time - b.time);
}
