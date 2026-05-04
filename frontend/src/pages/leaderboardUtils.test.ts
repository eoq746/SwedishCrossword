import { describe, expect, it } from 'vitest';
import type { LeaderboardResponse } from '../api/leaderboard';
import { resolveTodayEntriesForSize } from './leaderboardUtils';

describe('resolveTodayEntriesForSize', () => {
  it('returns only entries for the selected size hash', () => {
    const leaderboard: LeaderboardResponse = {
      scores: {
        '2026-01-02-hash-17': [
          { name: 'B', time: 45, timestamp: 2, puzzleHash: 'hash-17', hintsUsed: 0, wordHintsUsed: 0, userId: null },
          { name: 'A', time: 30, timestamp: 1, puzzleHash: 'hash-17', hintsUsed: 0, wordHintsUsed: 0, userId: null },
        ],
        '2026-01-02-hash-10': [
          { name: 'C', time: 15, timestamp: 3, puzzleHash: 'hash-10', hintsUsed: 0, wordHintsUsed: 0, userId: null },
        ],
      },
    };

    const result = resolveTodayEntriesForSize(leaderboard, '17x17', { '17x17': 'hash-17', '10x10': 'hash-10' }, '2026-01-02');

    expect(result.map(entry => entry.name)).toEqual(['A', 'B']);
  });

  it('returns no entries when the selected size hash is unavailable', () => {
    const leaderboard: LeaderboardResponse = {
      scores: {
        '2026-01-02-hash-17': [
          { name: 'A', time: 30, timestamp: 1, puzzleHash: 'hash-17', hintsUsed: 0, wordHintsUsed: 0, userId: null },
        ],
      },
    };

    const result = resolveTodayEntriesForSize(leaderboard, '15x15', {}, '2026-01-02');

    expect(result).toEqual([]);
  });
});
