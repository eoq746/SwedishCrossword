import { fetchWithTimeout } from './http';

// ── Types ────────────────────────────────────────────────────────────────────

export interface SizeStatsEntry {
  count: number;
  averageTime: number;
  bestTime: number;
  currentStreak: number;
  bestStreak: number;
}

export interface UserSolveRecord {
  date: string;
  time: number;
  puzzleSize: string | null;
  hintsUsed: number;
  wordHintsUsed: number;
}

export interface AchievementBadge {
  id: string;
  name: string;
  description: string;
  icon: string;
  unlocked: boolean;
}

export interface UserStatsResponse {
  totalSolved: number;
  averageTime: number;
  bestTime: number;
  currentStreak: number;
  bestStreak: number;
  recentSolves: UserSolveRecord[];
  perSize: Record<string, SizeStatsEntry> | null;
  badges: AchievementBadge[] | null;
}

export interface FriendInfo {
  alias: string;
  friendId: string;
}

export interface FriendRequestInfo {
  id: string;
  fromAlias: string;
  toAlias: string;
  direction: 'incoming' | 'outgoing';
  status: string;
  createdAt: number;
}

export interface FriendChallengeSolveSummary {
  playerAlias: string;
  time: number;
  hintsUsed: number;
  wordHintsUsed: number;
}

export interface FriendChallengeInfo {
  id: string;
  friendAlias: string;
  date: string;
  puzzleSize: string;
  status: 'pending' | 'accepted' | 'declined';
  direction: 'incoming' | 'outgoing';
  createdAt: number;
  respondedAt: number | null;
  resultStatus: 'pending' | 'accepted' | 'declined' | 'completed' | 'expired' | null;
  winnerAlias: string | null;
  resultReason: string | null;
  currentUserSolve: FriendChallengeSolveSummary | null;
  friendSolve: FriendChallengeSolveSummary | null;
}

// ── Helpers ──────────────────────────────────────────────────────────────────

const DEFAULT_TIMEOUT_MS = 20_000;
const OPT: RequestInit = { credentials: 'same-origin' };

async function apiFetch<T>(input: string, init?: RequestInit): Promise<T> {
  const res = await fetchWithTimeout(input, {
    ...OPT,
    ...init,
    timeoutMs: DEFAULT_TIMEOUT_MS,
    retries: 1,
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({})) as Record<string, unknown>;
    throw new Error((body['error'] as string | undefined) ?? `HTTP ${res.status}`);
  }
  return res.json() as Promise<T>;
}

// ── Stats ─────────────────────────────────────────────────────────────────────

export function fetchMyStats(): Promise<UserStatsResponse> {
  return apiFetch('/api/auth/my-stats');
}

// ── Alias ─────────────────────────────────────────────────────────────────────

export function saveAlias(alias: string): Promise<{ alias: string }> {
  return apiFetch('/api/auth/alias', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ alias }),
  });
}

// ── Friends ───────────────────────────────────────────────────────────────────

export function fetchFriends(): Promise<FriendInfo[]> {
  return apiFetch('/api/friends/');
}

export function fetchFriendRequests(): Promise<FriendRequestInfo[]> {
  return apiFetch('/api/friends/requests');
}

export function sendFriendRequest(alias: string): Promise<{ ok: boolean }> {
  return apiFetch('/api/friends/request', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ alias }),
  });
}

export function acceptFriendRequest(requestId: string): Promise<{ ok: boolean }> {
  return apiFetch(`/api/friends/accept/${encodeURIComponent(requestId)}`, { method: 'POST' });
}

export function declineFriendRequest(requestId: string): Promise<{ ok: boolean }> {
  return apiFetch(`/api/friends/decline/${encodeURIComponent(requestId)}`, { method: 'POST' });
}

export function removeFriend(friendshipId: string): Promise<{ ok: boolean }> {
  return apiFetch(`/api/friends/${encodeURIComponent(friendshipId)}`, { method: 'DELETE' });
}

// ── Challenges ────────────────────────────────────────────────────────────────

export function fetchChallenges(): Promise<FriendChallengeInfo[]> {
  return apiFetch('/api/friends/challenges');
}

export function fetchExpiredChallenges(): Promise<FriendChallengeInfo[]> {
  return apiFetch('/api/friends/challenges/expired');
}

export interface FriendsLeaderboardEntry {
  name: string;
  time: number;
  timestamp: number | null;
  puzzleHash: string | null;
  hintsUsed: number;
  wordHintsUsed: number;
}

export function fetchFriendsLeaderboard(date: string, puzzleHash?: string): Promise<FriendsLeaderboardEntry[]> {
  const params = new URLSearchParams({ date });
  if (puzzleHash) params.set('puzzleHash', puzzleHash);
  return apiFetch(`/api/friends/leaderboard?${params.toString()}`);
}

export interface FriendChallengesCreateResponse {
  sent: number;
  skipped: number;
}

export function sendChallenge(friendId: string, date: string, puzzleSize: string): Promise<{ ok: boolean }> {
  return apiFetch('/api/friends/challenges', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ friendId, date, puzzleSize }),
  });
}

export function sendChallenges(date: string, puzzleSize: string, friendIds: string[]): Promise<FriendChallengesCreateResponse> {
  return apiFetch('/api/friends/challenges/bulk', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ date, puzzleSize, friendIds }),
  });
}

export function sendChallengesToAll(date: string, puzzleSize: string): Promise<FriendChallengesCreateResponse> {
  return apiFetch('/api/friends/challenges/bulk', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ date, puzzleSize, allFriends: true }),
  });
}

export function respondChallenge(challengeId: string, accepted: boolean): Promise<{ ok: boolean }> {
  return apiFetch(`/api/friends/challenges/${encodeURIComponent(challengeId)}/respond`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ accepted }),
  });
}

// ── GDPR ──────────────────────────────────────────────────────────────────────

export async function exportMyData(): Promise<void> {
  const res = await fetchWithTimeout('/api/auth/my-data', {
    credentials: 'same-origin',
    timeoutMs: 45_000,
    retries: 1,
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  const data: unknown = await res.json();
  const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = 'svenskt-korsord-mina-uppgifter.json';
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

export function deleteMyAccount(): Promise<{ deleted: boolean }> {
  return apiFetch('/api/auth/account', { method: 'DELETE' });
}

// ── Formatting ────────────────────────────────────────────────────────────────

export const SIZE_LABELS: Record<string, string> = {
  '10x10': 'Liten (10×10)',
  '15x15': 'Mellan (15×15)',
  '17x17': 'Stor (17×17)',
};

export function formatTime(seconds: number | null | undefined): string {
  if (seconds == null) return '--:--';
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m}:${s < 10 ? '0' : ''}${s}`;
}

export function todayIso(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}
