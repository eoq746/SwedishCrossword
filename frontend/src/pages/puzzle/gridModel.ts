import type { CellKey, ClueEntry, Direction, GridCellModel, GridModel, PuzzleClue, PuzzleClueEntries, PuzzleData } from './types';

function buildFallbackCells(puzzle: PuzzleData, clueNumber: number, direction: Direction): Array<{ row: number; col: number }> {
  let startRow = -1;
  let startCol = -1;

  outer: for (let row = 0; row < puzzle.height; row++) {
    for (let col = 0; col < puzzle.width; col++) {
      if (puzzle.cells[row]?.[col]?.num === clueNumber) {
        startRow = row;
        startCol = col;
        break outer;
      }
    }
  }

  if (startRow < 0 || startCol < 0) return [];

  const cells: Array<{ row: number; col: number }> = [];
  if (direction === 'across') {
    for (let col = startCol; col < puzzle.width; col++) {
      if (puzzle.cells[startRow]?.[col] === null) break;
      cells.push({ row: startRow, col });
    }
  } else {
    for (let row = startRow; row < puzzle.height; row++) {
      if (puzzle.cells[row]?.[startCol] === null) break;
      cells.push({ row, col: startCol });
    }
  }

  return cells;
}

export function buildClueEntries(puzzle: PuzzleData): PuzzleClueEntries {
  const byCell: Record<string, ClueEntry[]> = {};

  const mapDirection = (direction: Direction, clues: PuzzleClue[]) =>
    clues
      .filter(clue => clue.number > 0)
      .map((clue, clueIndex) => {
        const cells = clue.cells?.length
          ? clue.cells.map(([row, col]) => ({ row, col }))
          : buildFallbackCells(puzzle, clue.number, direction);

        const entry: ClueEntry = {
          id: `${direction}:${clueIndex}`,
          clueIndex,
          direction,
          number: clue.number,
          clue: clue.clue,
          cells,
        };

        for (const cell of cells) {
          const key: CellKey = `${cell.row},${cell.col}`;
          byCell[key] = byCell[key] ?? [];
          byCell[key].push(entry);
        }

        return entry;
      });

  return {
    across: mapDirection('across', puzzle.clues.across ?? []),
    down: mapDirection('down', puzzle.clues.down ?? []),
    byCell,
  };
}

interface BuildGridModelOptions {
  puzzle: PuzzleData;
  values: Record<string, string>;
  activeCell: { row: number; col: number } | null;
  activeEntry: ClueEntry | null;
  incorrectCells: Record<string, true>;
  emptyWarningCells: Record<string, true>;
  hintRevealedCells: Record<string, true>;
}

export function buildGridModel({
  puzzle,
  values,
  activeCell,
  activeEntry,
  incorrectCells,
  emptyWarningCells,
  hintRevealedCells,
}: BuildGridModelOptions): GridModel {
  const highlightedCells = new Set<string>(activeEntry?.cells.map(cell => `${cell.row},${cell.col}`) ?? []);

  const cells = puzzle.cells.map((rowCells, row) =>
    rowCells.map((cell, col) => {
      const key = `${row},${col}` as CellKey;
      const value = values[key] ?? '';
      const isBlocked = cell === null;
      const isActive = activeCell?.row === row && activeCell?.col === col;

      const model: GridCellModel = {
        key,
        row,
        col,
        id: isBlocked ? undefined : `puzzle-cell-${row}-${col}`,
        value,
        isBlocked,
        number: cell?.num,
        bend: cell?.bend,
        isActive,
        isInActiveWord: highlightedCells.has(key),
        isIncorrect: Boolean(incorrectCells[key]),
        isEmptyWarning: Boolean(emptyWarningCells[key]),
        isHintRevealed: Boolean(hintRevealedCells[key]),
        buttonLabel: isBlocked ? undefined : `Rad ${row + 1}, kolumn ${col + 1}${value ? `, ${value}` : ''}`,
      };

      return model;
    }),
  );

  return {
    width: puzzle.width,
    height: puzzle.height,
    cells,
  };
}
