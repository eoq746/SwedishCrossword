import { fetchWithTimeout } from './http';

/** One entry from GET /api/puzzle/dates */
export interface PuzzleDateEntry {
  date: string;   // "yyyy-MM-dd"
  sizes: string[]; // e.g. ["10x10","17x17"]
}

/** Keyed map: date string → available sizes */
export type DateSizeMap = Record<string, string[]>;

/** Fetch available puzzle dates and return as a DateSizeMap. */
export async function fetchPuzzleDates(): Promise<DateSizeMap> {
  const res = await fetchWithTimeout('/api/puzzle/dates', {
    timeoutMs: 20_000,
    retries: 1,
  });
  if (!res.ok) throw new Error(`Failed to load puzzle dates: ${res.status}`);
  const data: (PuzzleDateEntry | string)[] = await res.json();
  const map: DateSizeMap = {};
  for (const item of data) {
    if (typeof item === 'string') {
      map[item] = ['17x17'];
    } else {
      map[item.date] = item.sizes ?? [];
    }
  }
  return map;
}
