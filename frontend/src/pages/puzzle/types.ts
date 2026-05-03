export type Direction = 'across' | 'down';
export type PuzzleSize = '10x10' | '15x15' | '17x17';
export type CellKey = `${number},${number}`;

export interface GridCoord {
  row: number;
  col: number;
}

export type PuzzleCell =
  | {
      num?: number;
      bend?: 'down' | 'right' | string;
    }
  | null;

export interface PuzzleClue {
  number: number;
  clue: string;
  answer?: string;
  cells?: [number, number][];
}

export interface PuzzleData {
  width: number;
  height: number;
  wordCount?: number;
  fillPercentage?: number;
  puzzleHash?: string;
  puzzleDate?: string;
  submissionToken?: string;
  cellCount?: number;
  cells: PuzzleCell[][];
  clues: {
    across: PuzzleClue[];
    down: PuzzleClue[];
  };
}

export interface ClueEntry {
  id: string;
  clueIndex: number;
  direction: Direction;
  number: number;
  clue: string;
  cells: GridCoord[];
}

export interface PuzzleClueEntries {
  across: ClueEntry[];
  down: ClueEntry[];
  byCell: Record<string, ClueEntry[]>;
}

export interface GridCellModel {
  key: CellKey;
  row: number;
  col: number;
  id?: string;
  value: string;
  isBlocked: boolean;
  number?: number;
  bend?: 'down' | 'right' | string;
  isActive: boolean;
  isInActiveWord: boolean;
  isIncorrect: boolean;
  isEmptyWarning: boolean;
  isHintRevealed: boolean;
  buttonLabel?: string;
}

export interface GridModel {
  width: number;
  height: number;
  cells: GridCellModel[][];
}

export interface ScoreEntry {
  name: string;
  time: number;
  timestamp: number | null;
  puzzleHash: string | null;
  hintsUsed: number;
  wordHintsUsed: number;
  userId: string | null;
  puzzleSize?: string | null;
}

export type HistoryResponse = Record<string, ScoreEntry[]>;
export type HistoryRow = [string, ScoreEntry[]];

export interface LeaderboardResponse {
  scores: Record<string, ScoreEntry[]>;
}

export interface NavState {
  active: GridCoord | null;
  direction: Direction;
}

export type NavAction =
  | { type: 'set-active'; cell: GridCoord; direction?: Direction }
  | { type: 'toggle-direction' }
  | { type: 'set-direction'; direction: Direction };

export interface CheckResult {
  status: 'solved' | 'incomplete' | 'incorrect' | 'error';
  emptyCount: number;
  incorrectCount: number;
}

export type HintActionResult = 'ok' | 'no-active-cell' | 'unavailable';
export type RevealSolutionResult = 'ok' | 'unavailable';
