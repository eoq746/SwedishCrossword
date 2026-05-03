// ── Types ────────────────────────────────────────────────────────────────────

export interface SizeCompletions {
  completions: number;
  averageTime: number;
}

export interface AnalyticsSummary {
  totalCompletions: number;
  uniquePlayers: number;
  registeredUsers: number;
  completionsToday: number;
  activeToday: number;
  averageTime: number;
  bestTime: number;
  hintUsageRate: number;
  perSize: Record<string, SizeCompletions> | null;
}

export interface DailyAnalytics {
  date: string;
  completions: number;
  uniquePlayers: number;
  averageTime: number;
  bestTime: number;
}

export interface TopPlayer {
  displayName: string;
  rawName: string;
  verified: boolean;
  gamesPlayed: number;
  averageTime: number;
  bestTime: number;
}

export interface AdminGrant {
  userId: string;
  alias: string | null;
  grantedAt: number;
  grantedByAlias: string | null;
}

export interface UserSearchResult {
  userId: string;
  alias: string;
}

// ── Fetch helpers ─────────────────────────────────────────────────────────────

const OPT: RequestInit = { credentials: 'same-origin', signal: AbortSignal.timeout(10_000) };

async function apiFetch<T>(url: string): Promise<T> {
  const res = await fetch(url, OPT);
  if (res.status === 401 || res.status === 403) throw new AccessDeniedError();
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json() as Promise<T>;
}

export class AccessDeniedError extends Error {
  constructor() { super('access_denied'); }
}

// ── Endpoints ─────────────────────────────────────────────────────────────────

export function fetchAnalyticsSummary(): Promise<AnalyticsSummary> {
  return apiFetch('/api/analytics/summary');
}

export function fetchDailyAnalytics(days = 30): Promise<DailyAnalytics[]> {
  return apiFetch(`/api/analytics/daily?days=${days}`);
}

export function fetchTopPlayers(limit = 20): Promise<TopPlayer[]> {
  return apiFetch(`/api/analytics/players?limit=${limit}`);
}

export async function triggerRegenerateFuturePuzzles(): Promise<void> {
  const res = await fetch('/api/admin/puzzle/regenerate-future', {
    method: 'POST',
    credentials: 'same-origin',
    signal: AbortSignal.timeout(120_000),
  });
  if (res.status === 401 || res.status === 403) throw new AccessDeniedError();
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
}

export function searchUserByAlias(alias: string): Promise<UserSearchResult> {
  return apiFetch(`/api/admin/users/search?q=${encodeURIComponent(alias)}`);
}

export function fetchAdminGrants(): Promise<AdminGrant[]> {
  return apiFetch('/api/admin/grants');
}

export async function grantAdmin(userId: string): Promise<void> {
  const res = await fetch('/api/admin/grants', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userId }),
    signal: AbortSignal.timeout(10_000),
  });
  if (res.status === 401 || res.status === 403) throw new AccessDeniedError();
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
}

export async function revokeAdmin(userId: string): Promise<void> {
  const res = await fetch(`/api/admin/grants/${encodeURIComponent(userId)}`, {
    method: 'DELETE',
    credentials: 'same-origin',
    signal: AbortSignal.timeout(10_000),
  });
  if (res.status === 401 || res.status === 403) throw new AccessDeniedError();
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
}
