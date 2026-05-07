import { fetchWithTimeout } from './http';

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
  exactMatch?: boolean;
}

export interface ClueFlag {
  id: string;
  word: string;
  currentClue: string;
  suggestedClue: string | null;
  reason: string | null;
  status: string;
  createdAt: number;
  reviewedAt: number | null;
  updatedClue: string | null;
  puzzleDate: string | null;
  puzzleSize: string | null;
  puzzleHash: string | null;
  adminNote: string | null;
  reportCount: number;
}

export interface BlobSyncConflictDetail {
  word: string;
  reason: string;
  resolution: string;
}

export interface BlobSyncFileResult {
  fileName: string;
  added: number;
  updated: number;
  removed: number;
  conflicts: number;
  changed: boolean;
  conflictDetails: BlobSyncConflictDetail[] | null;
  error: string | null;
}

export interface BlobSyncResult {
  dryRun: boolean;
  filesProcessed: number;
  filesChanged: number;
  totalAdded: number;
  totalUpdated: number;
  totalRemoved: number;
  totalConflicts: number;
  files: BlobSyncFileResult[];
}

export interface PuzzleRegenerationStatus {
  state: 'idle' | 'queued' | 'running' | 'failed' | string;
  pendingChangeCount: number;
  notBeforeAt: number | null;
  lastQueuedAt: number | null;
  lastStartedAt: number | null;
  lastCompletedAt: number | null;
  lastError: string | null;
}

// ── Fetch helpers ─────────────────────────────────────────────────────────────

const BASE_TIMEOUT_MS = 20_000;
const OPT: RequestInit = { credentials: 'same-origin' };

async function readApiError(res: Response): Promise<string> {
  try {
    const body = await res.json() as { error?: string; Error?: string; title?: string };
    if (typeof body?.error === 'string' && body.error) return body.error;
    if (typeof body?.Error === 'string' && body.Error) return body.Error;
    if (typeof body?.title === 'string' && body.title) return body.title;
  } catch {
    // ignore parse errors and fall back to status-based message
  }

  return `HTTP ${res.status}`;
}

async function apiFetch<T>(url: string): Promise<T> {
  const res = await fetchWithTimeout(url, {
    ...OPT,
    timeoutMs: BASE_TIMEOUT_MS,
    retries: 1,
  });
  if (res.status === 401 || res.status === 403) throw new AccessDeniedError();
  if (!res.ok) throw new Error(await readApiError(res));
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

export async function triggerRegenerateFuturePuzzles(): Promise<PuzzleRegenerationStatus> {
  const res = await fetchWithTimeout('/api/admin/puzzle/regenerate-future', {
    method: 'POST',
    credentials: 'same-origin',
    timeoutMs: BASE_TIMEOUT_MS,
    retries: 0,
  });
  if (res.status === 401 || res.status === 403) throw new AccessDeniedError();
  if (!res.ok) throw new Error(await readApiError(res));
  return res.json() as Promise<PuzzleRegenerationStatus>;
}

export function fetchPuzzleRegenerationStatus(): Promise<PuzzleRegenerationStatus> {
  return apiFetch('/api/admin/puzzle/regeneration-status');
}

export function searchUserByAlias(alias: string, limit = 10): Promise<UserSearchResult[]> {
  return apiFetch(`/api/admin/users/search?q=${encodeURIComponent(alias)}&limit=${limit}`);
}

export function fetchAdminGrants(): Promise<AdminGrant[]> {
  return apiFetch('/api/admin/grants');
}

export async function grantAdmin(userId: string): Promise<void> {
  const res = await fetchWithTimeout('/api/admin/grants', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userId }),
    timeoutMs: BASE_TIMEOUT_MS,
    retries: 1,
  });
  if (res.status === 401 || res.status === 403) throw new AccessDeniedError();
  if (!res.ok) throw new Error(await readApiError(res));
}

export async function revokeAdmin(userId: string): Promise<void> {
  const res = await fetchWithTimeout(`/api/admin/grants/${encodeURIComponent(userId)}`, {
    method: 'DELETE',
    credentials: 'same-origin',
    timeoutMs: BASE_TIMEOUT_MS,
    retries: 1,
  });
  if (res.status === 401 || res.status === 403) throw new AccessDeniedError();
  if (!res.ok) throw new Error(await readApiError(res));
}

export function fetchPendingClueFlags(limit = 100): Promise<ClueFlag[]> {
  return apiFetch(`/api/admin/clues/flags?limit=${limit}`);
}

export async function resolveClueFlag(
  id: string,
  status: 'approved' | 'rejected',
  updatedClue?: string,
  adminNote?: string,
  expectedWordListVersion?: string,
  removeClue = false,
): Promise<{ wordListVersion?: string }> {
  const res = await fetchWithTimeout(`/api/admin/clues/flags/${encodeURIComponent(id)}/resolve`, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ status, updatedClue, adminNote, expectedWordListVersion, removeClue }),
    timeoutMs: BASE_TIMEOUT_MS,
    retries: 0,
  });
  if (res.status === 401 || res.status === 403) throw new AccessDeniedError();
  if (!res.ok) throw new Error(await readApiError(res));
  return res.json() as Promise<{ wordListVersion?: string }>;
}

export async function createCustomClue(word: string, clue: string, category?: string, difficulty?: string): Promise<void> {
  const res = await fetchWithTimeout('/api/admin/clues/custom', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ word, clue, category, difficulty }),
    timeoutMs: 180_000,
    retries: 0,
  });
  if (res.status === 401 || res.status === 403) throw new AccessDeniedError();
  if (!res.ok) throw new Error(await readApiError(res));
}

export async function syncWordListsDevToProd(dryRun: boolean): Promise<BlobSyncResult> {
  const res = await fetchWithTimeout('/api/admin/wordlists/sync-dev-to-prod', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ dryRun }),
    timeoutMs: 240_000,
    retries: 0,
  });
  if (res.status === 401 || res.status === 403) throw new AccessDeniedError();
  if (!res.ok) throw new Error(await readApiError(res));
  return res.json() as Promise<BlobSyncResult>;
}
