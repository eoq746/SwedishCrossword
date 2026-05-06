import { fetchWithTimeout } from './http';

export interface CreateClueFlagRequest {
  currentClue: string;
  clueCells: number[][];
  suggestedClue?: string;
  reason?: string;
  puzzleDate?: string;
  puzzleSize?: string;
  puzzleHash?: string;
}

export async function createClueFlag(request: CreateClueFlagRequest): Promise<{ id: string }> {
  const res = await fetchWithTimeout('/api/clues/flags', {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
    timeoutMs: 20_000,
    retries: 1,
  });

  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json() as Promise<{ id: string }>;
}
