import type { CellKey, ClueEntry, Direction, GridCoord, NavAction, NavState, PuzzleData } from './types';

export function navReducer(state: NavState, action: NavAction): NavState {
  switch (action.type) {
    case 'set-active':
      return {
        active: action.cell,
        direction: action.direction ?? state.direction,
      };
    case 'toggle-direction':
      return {
        ...state,
        direction: state.direction === 'across' ? 'down' : 'across',
      };
    case 'set-direction':
      return { ...state, direction: action.direction };
    default:
      return state;
  }
}

export function localDirectionAtCell(entry: ClueEntry, row: number, col: number): Direction | null {
  const idx = entry.cells.findIndex(c => c.row === row && c.col === col);
  if (idx < 0 || entry.cells.length < 2) return null;
  const ref = idx < entry.cells.length - 1 ? entry.cells[idx + 1] : entry.cells[idx - 1];
  return ref.row === row ? 'across' : 'down';
}

export function findBestEntry(entries: ClueEntry[] | undefined, direction: Direction, row: number, col: number): ClueEntry | null {
  if (!entries || entries.length === 0) return null;
  const localMatch = entries.find(entry => localDirectionAtCell(entry, row, col) === direction);
  if (localMatch) return localMatch;
  const nominalMatch = entries.find(entry => entry.direction === direction);
  return nominalMatch ?? entries[0];
}

export function findFirstFillableCell(puzzle: PuzzleData): GridCoord | null {
  for (let row = 0; row < puzzle.cells.length; row++) {
    for (let col = 0; col < puzzle.cells[row].length; col++) {
      if (puzzle.cells[row][col] !== null) return { row, col };
    }
  }
  return null;
}

export function getCellKey(cell: GridCoord): CellKey {
  return `${cell.row},${cell.col}`;
}
