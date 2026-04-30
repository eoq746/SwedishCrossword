import { useCallback } from 'react';
import type { Dispatch, KeyboardEvent, SetStateAction } from 'react';
import { findBestEntry } from './navigation';
import { isSwedishLetter } from './utils';
import type { CellKey, ClueEntry, GridCoord, NavAction, NavState, PuzzleClueEntries, PuzzleData } from './types';

interface UsePuzzleGridInputOptions {
  puzzle: PuzzleData | null;
  puzzleSolved: boolean;
  nav: NavState;
  clueEntries: PuzzleClueEntries;
  activeEntry: ClueEntry | null;
  seconds: number;
  letterHintsUsed: number;
  wordHintsUsed: number;
  activateCell: (row: number, col: number, direction?: ClueEntry['direction']) => void;
  dispatchNav: Dispatch<NavAction>;
  saveProgress: (nextValues: Record<string, string>, nextSeconds: number, nextLetterHints: number, nextWordHints: number) => void;
  setValues: Dispatch<SetStateAction<Record<string, string>>>;
  setIncorrectCells: Dispatch<SetStateAction<Record<string, true>>>;
  setEmptyWarningCells: Dispatch<SetStateAction<Record<string, true>>>;
}

function sanitizeLetters(raw: string): string[] {
  return Array.from(raw.toUpperCase()).filter(isSwedishLetter);
}

export function usePuzzleGridInput({
  puzzle,
  puzzleSolved,
  nav,
  clueEntries,
  activeEntry,
  seconds,
  letterHintsUsed,
  wordHintsUsed,
  activateCell,
  dispatchNav,
  saveProgress,
  setValues,
  setIncorrectCells,
  setEmptyWarningCells,
}: UsePuzzleGridInputOptions) {
  const clearValidationState = useCallback((key: CellKey) => {
    setIncorrectCells(prev => {
      const next = { ...prev };
      delete next[key];
      return next;
    });

    setEmptyWarningCells(prev => {
      const next = { ...prev };
      delete next[key];
      return next;
    });
  }, [setEmptyWarningCells, setIncorrectCells]);

  const moveToNeighbor = useCallback((delta: 1 | -1) => {
    if (!nav.active) return;
    const key: CellKey = `${nav.active.row},${nav.active.col}`;
    const entry = findBestEntry(clueEntries.byCell[key], nav.direction, nav.active.row, nav.active.col);

    if (entry) {
      const idx = entry.cells.findIndex(cell => cell.row === nav.active!.row && cell.col === nav.active!.col);
      const next = entry.cells[idx + delta];
      if (next) {
        const ref = delta > 0 ? entry.cells[idx + 2] ?? entry.cells[idx] : entry.cells[idx - 2] ?? entry.cells[idx];
        const localDir = ref.row === next.row ? 'across' : 'down';
        activateCell(next.row, next.col, localDir);
        return;
      }
    }

    const nextRow = nav.direction === 'down' ? nav.active.row + delta : nav.active.row;
    const nextCol = nav.direction === 'across' ? nav.active.col + delta : nav.active.col;
    activateCell(nextRow, nextCol);
  }, [activateCell, clueEntries.byCell, nav.active, nav.direction]);

  const moveClue = useCallback((direction: 1 | -1) => {
    if (!nav.active) return;
    const list = nav.direction === 'across' ? clueEntries.across : clueEntries.down;
    if (list.length === 0) return;

    const current = activeEntry;
    const idx = current ? list.findIndex(entry => entry.id === current.id) : -1;
    const nextIdx = idx >= 0 ? (idx + direction + list.length) % list.length : 0;
    const nextEntry = list[nextIdx];
    const firstCell = nextEntry.cells[0];
    if (firstCell) activateCell(firstCell.row, firstCell.col, nextEntry.direction);
  }, [activateCell, activeEntry, clueEntries.across, clueEntries.down, nav.active, nav.direction]);

  const applyLetters = useCallback((start: GridCoord, letters: string[]) => {
    if (!puzzle || letters.length === 0) return;

    const key: CellKey = `${start.row},${start.col}`;
    const entry = findBestEntry(clueEntries.byCell[key], nav.direction, start.row, start.col);
    const targetCells = entry?.cells.length ? entry.cells : [start];
    const startIndex = Math.max(0, targetCells.findIndex(cell => cell.row === start.row && cell.col === start.col));
    const cellsToFill = targetCells.slice(startIndex, startIndex + letters.length);
    if (cellsToFill.length === 0) return;

    setValues(prev => {
      const next = { ...prev };
      cellsToFill.forEach((cell, index) => {
        const targetKey = `${cell.row},${cell.col}` as CellKey;
        next[targetKey] = letters[index];
      });
      saveProgress(next, seconds, letterHintsUsed, wordHintsUsed);
      return next;
    });

    cellsToFill.forEach(cell => clearValidationState(`${cell.row},${cell.col}` as CellKey));

    if (entry) {
      const nextCell = targetCells[startIndex + letters.length];
      const anchor = nextCell ?? cellsToFill[cellsToFill.length - 1];
      activateCell(anchor.row, anchor.col, entry.direction);
      return;
    }

    const lastCell = cellsToFill[cellsToFill.length - 1];
    activateCell(lastCell.row, lastCell.col);
  }, [activateCell, clearValidationState, clueEntries.byCell, letterHintsUsed, nav.direction, puzzle, saveProgress, seconds, setValues, wordHintsUsed]);

  const handleCellChange = useCallback((row: number, col: number, raw: string) => {
    if (!puzzle || puzzleSolved) return;

    if (raw.includes(' ') && sanitizeLetters(raw).length === 0) {
      dispatchNav({ type: 'toggle-direction' });
      return;
    }

    const letters = sanitizeLetters(raw);
    const key: CellKey = `${row},${col}`;

    if (letters.length === 0) {
      setValues(prev => {
        const next = { ...prev };
        delete next[key];
        saveProgress(next, seconds, letterHintsUsed, wordHintsUsed);
        return next;
      });
      clearValidationState(key);
      return;
    }

    applyLetters({ row, col }, letters);
  }, [applyLetters, clearValidationState, dispatchNav, letterHintsUsed, puzzle, puzzleSolved, saveProgress, seconds, setValues, wordHintsUsed]);

  const handleCellKeyDown = useCallback((e: KeyboardEvent<HTMLElement>, row: number, col: number) => {
    if (!puzzle || puzzleSolved) return;
    if (e.ctrlKey || e.metaKey) {
      e.preventDefault();
      return;
    }

    const key: CellKey = `${row},${col}`;

    switch (e.key) {
      case 'Tab':
        e.preventDefault();
        moveClue(e.shiftKey ? -1 : 1);
        return;
      case 'Backspace':
        e.preventDefault();
        setValues(prev => {
          const next = { ...prev };
          delete next[key];
          saveProgress(next, seconds, letterHintsUsed, wordHintsUsed);
          return next;
        });
        clearValidationState(key);
        moveToNeighbor(-1);
        return;
      case 'Delete':
        e.preventDefault();
        setValues(prev => {
          const next = { ...prev };
          delete next[key];
          saveProgress(next, seconds, letterHintsUsed, wordHintsUsed);
          return next;
        });
        clearValidationState(key);
        return;
      case 'ArrowRight':
        e.preventDefault();
        dispatchNav({ type: 'set-direction', direction: 'across' });
        activateCell(row, col + 1);
        return;
      case 'ArrowLeft':
        e.preventDefault();
        dispatchNav({ type: 'set-direction', direction: 'across' });
        activateCell(row, col - 1);
        return;
      case 'ArrowDown':
        e.preventDefault();
        dispatchNav({ type: 'set-direction', direction: 'down' });
        activateCell(row + 1, col);
        return;
      case 'ArrowUp':
        e.preventDefault();
        dispatchNav({ type: 'set-direction', direction: 'down' });
        activateCell(row - 1, col);
        return;
      case ' ':
      case 'Spacebar':
        e.preventDefault();
        dispatchNav({ type: 'toggle-direction' });
        return;
      default:
        if (e.key.length === 1 && !isSwedishLetter(e.key)) e.preventDefault();
    }
  }, [activateCell, clearValidationState, dispatchNav, letterHintsUsed, moveClue, moveToNeighbor, puzzle, puzzleSolved, saveProgress, seconds, setValues, wordHintsUsed]);

  return {
    handleCellChange,
    handleCellKeyDown,
  };
}
