export interface ScoreEntry {
  name: string;
  time: number;
  timestamp: number | null;
  puzzleHash: string | null;
  hintsUsed: number;
  wordHintsUsed: number;
  userId: string | null;
}

export interface HistoryEntry extends ScoreEntry {
  puzzleSize: string | null;
}

/** Response from GET /api/leaderboard */
export interface LeaderboardResponse {
  scores: Record<string, ScoreEntry[]>;
}

/** Response from GET /api/leaderboard/history?days=N  — keyed by "YYYY-MM-DD" */
export type HistoryResponse = Record<string, HistoryEntry[]>;

/** Response from GET /api/puzzle/hashes — maps puzzle size to today's puzzle hash */
export type SizeHashMap = Record<string, string>;

const TIMEOUT_MS = 10_000;

function signal(): AbortSignal {
  return AbortSignal.timeout(TIMEOUT_MS);
}

export async function fetchLeaderboard(): Promise<LeaderboardResponse> {
  const res = await fetch('/api/leaderboard', { credentials: 'same-origin', signal: signal() });
  if (!res.ok) throw new Error(`Leaderboard fetch failed: ${res.status}`);
  return res.json() as Promise<LeaderboardResponse>;
}

export async function fetchHistory(days = 7): Promise<HistoryResponse> {
  const res = await fetch(`/api/leaderboard/history?days=${days}`, { credentials: 'same-origin', signal: signal() });
  if (!res.ok) throw new Error(`History fetch failed: ${res.status}`);
  return res.json() as Promise<HistoryResponse>;
}

export async function fetchSizeHashes(): Promise<SizeHashMap> {
  const res = await fetch('/api/puzzle/hashes', { credentials: 'same-origin', signal: signal() });
  if (!res.ok) throw new Error(`Hash fetch failed: ${res.status}`);
  return res.json() as Promise<SizeHashMap>;
}

/** Format seconds as M:SS */
export function formatTime(seconds: number): string {
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m}:${s.toString().padStart(2, '0')}`;
}
