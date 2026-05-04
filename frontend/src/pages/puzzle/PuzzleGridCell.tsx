import type { GridCellModel } from './types';

interface PuzzleGridCellProps {
  cell: GridCellModel;
  onActivate: (row: number, col: number) => void;
}

export function PuzzleGridCell({ cell, onActivate }: PuzzleGridCellProps) {
  return (
    <div
      role="gridcell"
      aria-selected={cell.isActive}
      className={`cell${cell.isBlocked ? ' blocked' : ''}${cell.isInActiveWord ? ' word-highlight' : ''}${
        cell.isActive ? ' active' : ''
      }${cell.isIncorrect ? ' incorrect' : ''}${cell.isEmptyWarning ? ' empty-warning' : ''}${
        cell.isHintRevealed ? ' hint-revealed' : ''
      }`}
      data-row={cell.row}
      data-col={cell.col}
    >
      {!cell.isBlocked && (
        <>
          {cell.number ? <span className="number">{cell.number}</span> : null}
          {cell.bend ? <span className="bend-arrow">{cell.bend === 'down' ? '↴' : '↳'}</span> : null}
          <button
            id={cell.id}
            type="button"
            className="react-puzzle-cell-button"
            aria-label={cell.buttonLabel}
            aria-pressed={cell.isActive}
            tabIndex={-1}
            onMouseDown={event => {
              event.preventDefault();
            }}
            onClick={() => onActivate(cell.row, cell.col)}
          >
            <span className="react-puzzle-cell-value">{cell.value}</span>
          </button>
        </>
      )}
    </div>
  );
}
