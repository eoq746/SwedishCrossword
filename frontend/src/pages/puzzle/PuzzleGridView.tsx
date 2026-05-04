import { useEffect, useRef, useState } from 'react';
import type { CSSProperties, KeyboardEvent } from 'react';
import { PuzzleGridCell } from './PuzzleGridCell';
import type { ClueEntry, GridModel } from './types';

interface PuzzleGridViewProps {
  model: GridModel;
  activeCell: { row: number; col: number } | null;
  activeEntry: ClueEntry | null;
  cellSize?: number;
  gridWidth?: number;
  gridHeight?: number;
  onActivate: (row: number, col: number) => void;
  onInput: (row: number, col: number, raw: string) => void;
  onKeyDown: (e: KeyboardEvent<HTMLElement>, row: number, col: number) => void;
}

export function PuzzleGridView({ model, activeCell, activeEntry, cellSize, gridWidth, gridHeight, onActivate, onInput, onKeyDown }: PuzzleGridViewProps) {
  const hiddenInputRef = useRef<HTMLInputElement | null>(null);
  const [hiddenValue, setHiddenValue] = useState('');

  useEffect(() => {
    if (!activeCell) return;
    hiddenInputRef.current?.focus({ preventScroll: true });
  }, [activeCell]);

  const activeCellLabel = activeCell
    ? `Rad ${activeCell.row + 1}, kolumn ${activeCell.col + 1}`
    : 'Välj en ruta i korsordet';
  const activeCellId = activeCell ? `puzzle-cell-${activeCell.row}-${activeCell.col}` : undefined;
  const activeCellValue = activeCell ? model.cells[activeCell.row]?.[activeCell.col]?.value ?? '' : '';
  const activeClueDescription = activeEntry
    ? `${activeEntry.number} ${activeEntry.direction === 'across' ? 'vågrätt' : 'lodrätt'}: ${activeEntry.clue}`
    : 'Ingen aktiv ledtråd';

  const handleActivate = (row: number, col: number) => {
    onActivate(row, col);
    hiddenInputRef.current?.focus({ preventScroll: true });
  };

  const shellStyle: CSSProperties = {
    width: gridWidth ? `${gridWidth}px` : undefined,
    height: gridHeight ? `${gridHeight}px` : undefined,
    maxWidth: '100%',
    maxHeight: '100%',
  };

  return (
    <div className="react-puzzle-grid-shell" style={shellStyle}>
      <input
        ref={hiddenInputRef}
        className="react-puzzle-input-proxy"
        type="text"
        value={hiddenValue}
        autoComplete="off"
        autoCorrect="off"
        autoCapitalize="characters"
        spellCheck={false}
        inputMode="text"
        aria-label={activeCellLabel}
        aria-activedescendant={activeCellId}
        aria-controls="crossword-grid"
        aria-describedby="react-puzzle-active-cell-description"
        onChange={event => {
          const nextValue = event.target.value;
          setHiddenValue('');
          if (!activeCell) return;
          onInput(activeCell.row, activeCell.col, nextValue);
        }}
        onPaste={event => {
          if (!activeCell) return;
          event.preventDefault();
          setHiddenValue('');
          onInput(activeCell.row, activeCell.col, event.clipboardData.getData('text'));
        }}
        onKeyDown={event => {
          if (!activeCell) return;
          onKeyDown(event, activeCell.row, activeCell.col);
        }}
      />
      <div id="react-puzzle-active-cell-description" className="sr-only">
        {activeCell
          ? `${activeCellLabel}${activeCellValue ? `, bokstav ${activeCellValue}.` : ', tom ruta.'} ${activeClueDescription}`
          : 'Välj en ruta i korsordet för att börja skriva.'}
      </div>

      <div
        className="crossword-grid react-puzzle-grid"
        id="crossword-grid"
        role="grid"
        aria-label="Korsord"
        style={{
          width: gridWidth ? `${gridWidth}px` : undefined,
          height: gridHeight ? `${gridHeight}px` : undefined,
          ['--cols' as string]: String(model.width),
          ['--rows' as string]: String(model.height),
          ['--cell-size' as string]: cellSize ? `${cellSize}px` : undefined,
        }}
      >
        {model.cells.map(row =>
          row.map(cell => <PuzzleGridCell key={cell.key} cell={cell} onActivate={handleActivate} />),
        )}
      </div>
    </div>
  );
}
