import type { KeyboardEvent } from 'react';
import { buildGridModel } from './gridModel';
import { PuzzleGridView } from './PuzzleGridView';
import type { ClueEntry, PuzzleData } from './types';

interface PuzzleGridProps {
  puzzle: PuzzleData;
  values: Record<string, string>;
  activeCell: { row: number; col: number } | null;
  activeEntry: ClueEntry | null;
  incorrectCells: Record<string, true>;
  emptyWarningCells: Record<string, true>;
  hintRevealedCells: Record<string, true>;
  cellSize?: number;
  gridWidth?: number;
  gridHeight?: number;
  onActivate: (row: number, col: number) => void;
  onChange: (row: number, col: number, raw: string) => void;
  onKeyDown: (e: KeyboardEvent<HTMLElement>, row: number, col: number) => void;
}

export function PuzzleGrid({
  puzzle,
  values,
  activeCell,
  activeEntry,
  incorrectCells,
  emptyWarningCells,
  hintRevealedCells,
  cellSize,
  gridWidth,
  gridHeight,
  onActivate,
  onChange,
  onKeyDown,
}: PuzzleGridProps) {
  const model = buildGridModel({
    puzzle,
    values,
    activeCell,
    activeEntry,
    incorrectCells,
    emptyWarningCells,
    hintRevealedCells,
  });

  return (
    <PuzzleGridView
      model={model}
      activeCell={activeCell}
      activeEntry={activeEntry}
      cellSize={cellSize}
      gridWidth={gridWidth}
      gridHeight={gridHeight}
      onActivate={onActivate}
      onInput={onChange}
      onKeyDown={onKeyDown}
    />
  );
}
