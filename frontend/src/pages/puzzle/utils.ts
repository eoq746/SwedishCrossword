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

// ── ARIA live-region announcements ──
let _announceTimer: ReturnType<typeof setTimeout> | null = null;
export function announce(message: string): void {
  const el = document.getElementById('announcements');
  if (!el) return;
  el.textContent = message;
  if (_announceTimer !== null) clearTimeout(_announceTimer);
  _announceTimer = setTimeout(() => {
    el.textContent = '';
    _announceTimer = null;
  }, 1000);
}

// ── One-time stale localStorage purge (matches §6 purgeStaleLocalStorage in site.js) ──
const STATS_RESET_DATE = '2026-04-14';
const LOCAL_STORAGE_RESET_KEY = 'dataResetDate';

export function purgeStaleLocalStorage(): void {
  try {
    if (localStorage.getItem(LOCAL_STORAGE_RESET_KEY) === STATS_RESET_DATE) return;

    const keysToRemove: string[] = [];
    for (let i = 0; i < localStorage.length; i++) {
      const key = localStorage.key(i);
      if (!key) continue;
      const lbMatch = key.match(/^crossword-leaderboard-(\d{4}-\d{2}-\d{2})/);
      if (lbMatch && lbMatch[1] < STATS_RESET_DATE) { keysToRemove.push(key); continue; }
      if (key.startsWith('crossword-progress-')) { keysToRemove.push(key); continue; }
      if (key.startsWith('solution-viewed-')) { keysToRemove.push(key); continue; }
    }
    keysToRemove.forEach(k => localStorage.removeItem(k));
    localStorage.setItem(LOCAL_STORAGE_RESET_KEY, STATS_RESET_DATE);
    if (keysToRemove.length > 0) {
      console.log(`Purged ${keysToRemove.length} stale localStorage entries (reset date: ${STATS_RESET_DATE})`);
    }
  } catch (e) {
    console.warn('Failed to purge stale localStorage:', e);
  }
}
