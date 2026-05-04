import type { ScoreEntry } from './types';

export function isSwedishLetter(value: string): boolean {
  return /^[A-Za-zåäöÅÄÖ]$/.test(value);
}

export function formatTime(seconds: number): string {
  const mm = Math.floor(seconds / 60);
  const ss = Math.floor(seconds % 60);
  const m = mm < 10 ? `0${mm}` : String(mm);
  const s = ss < 10 ? `0${ss}` : String(ss);
  return `${m}:${s}`;
}

export function getTodayIso(): string {
  return new Date().toISOString().split('T')[0];
}

export function getProgressKey(puzzleHash: string): string {
  return `crossword-progress-${puzzleHash}`;
}

export function getLeaderboardStorageKey(date: string, puzzleHash: string): string {
  return `crossword-leaderboard-${date}-${puzzleHash}`;
}

export function loadLocalLeaderboard(date: string, puzzleHash: string): ScoreEntry[] {
  try {
    const raw = localStorage.getItem(getLeaderboardStorageKey(date, puzzleHash));
    if (!raw) return [];
    const parsed = JSON.parse(raw) as ScoreEntry[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

export function saveLocalLeaderboard(date: string, puzzleHash: string, entries: ScoreEntry[]): void {
  localStorage.setItem(getLeaderboardStorageKey(date, puzzleHash), JSON.stringify(entries.slice(0, 10)));
}
