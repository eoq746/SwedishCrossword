import { useEffect, useRef } from 'react';
import type { ClueEntry } from './types';

interface PuzzleCluesProps {
  across: ClueEntry[];
  down: ClueEntry[];
  activeEntryId: string | null;
  isClueFilled: (entry: ClueEntry) => boolean;
  onSelect: (entry: ClueEntry) => void;
  onReport?: (entry: ClueEntry) => void;
  isReported?: (entry: ClueEntry) => boolean;
  height?: number;
}

export function PuzzleClues({ across, down, activeEntryId, isClueFilled, onSelect, onReport, isReported, height }: PuzzleCluesProps) {
  const acrossListRef = useRef<HTMLUListElement | null>(null);
  const downListRef = useRef<HTMLUListElement | null>(null);
  const itemRefs = useRef<Record<string, HTMLLIElement | null>>({});

  useEffect(() => {
    if (!activeEntryId) return;
    const item = itemRefs.current[activeEntryId];
    if (!item) return;

    const list = activeEntryId.startsWith('across:') ? acrossListRef.current : downListRef.current;
    if (!list) return;

    const target = item.offsetTop - list.offsetTop - 8;
    list.scrollTo({ top: Math.max(0, target), behavior: 'smooth' });
  }, [activeEntryId]);

  return (
    <div
      className="clues-section"
      id="clues-section"
      style={{
        height: height ? `${height}px` : undefined,
        minHeight: height ? `${height}px` : undefined,
        maxHeight: height ? `${height}px` : undefined,
      }}
    >
      <h2>Ledtrådar</h2>
      <div className="clues-columns">
        <div className="clue-column">
          <div className="clue-direction">
            <h3 id="across-clues-heading">Vågrätt</h3>
            <ul className="clue-list" id="across-clues" role="list" aria-labelledby="across-clues-heading" ref={acrossListRef}>
              {across.map(entry => {
                const reported = isReported?.(entry) ?? false;
                return (
                <li
                  key={entry.id}
                  ref={el => {
                    itemRefs.current[entry.id] = el;
                  }}
                  className={`clue-item${activeEntryId === entry.id ? ' active' : ''}${isClueFilled(entry) ? ' filled' : ''}`}
                  data-number={entry.number}
                  data-direction="across"
                >
                  <div style={{ display: 'flex', alignItems: 'flex-start', gap: 6 }}>
                    <button
                      type="button"
                      className="clue-select"
                      style={{ flex: 1 }}
                      aria-current={activeEntryId === entry.id ? 'true' : undefined}
                      onClick={() => onSelect(entry)}
                    >
                      <span className="clue-number">{entry.number}. </span>
                      {entry.clue}
                    </button>
                    {onReport ? (
                      <button
                        type="button"
                        className="clue-report-btn"
                        aria-label={reported ? `Ledtråd ${entry.number} är redan rapporterad` : `Rapportera ledtråd ${entry.number} vågrätt`}
                        title={reported ? 'Redan rapporterad' : 'Rapportera dålig ledtråd'}
                        onClick={() => onReport(entry)}
                        disabled={reported}
                      >
                        {reported ? '✅' : '🚩'}
                      </button>
                    ) : null}
                  </div>
                </li>
              );
              })}
            </ul>
          </div>
        </div>
        <div className="clue-column">
          <div className="clue-direction">
            <h3 id="down-clues-heading">Lodrätt</h3>
            <ul className="clue-list" id="down-clues" role="list" aria-labelledby="down-clues-heading" ref={downListRef}>
              {down.map(entry => {
                const reported = isReported?.(entry) ?? false;
                return (
                <li
                  key={entry.id}
                  ref={el => {
                    itemRefs.current[entry.id] = el;
                  }}
                  className={`clue-item${activeEntryId === entry.id ? ' active' : ''}${isClueFilled(entry) ? ' filled' : ''}`}
                  data-number={entry.number}
                  data-direction="down"
                >
                  <div style={{ display: 'flex', alignItems: 'flex-start', gap: 6 }}>
                    <button
                      type="button"
                      className="clue-select"
                      style={{ flex: 1 }}
                      aria-current={activeEntryId === entry.id ? 'true' : undefined}
                      onClick={() => onSelect(entry)}
                    >
                      <span className="clue-number">{entry.number}. </span>
                      {entry.clue}
                    </button>
                    {onReport ? (
                      <button
                        type="button"
                        className="clue-report-btn"
                        aria-label={reported ? `Ledtråd ${entry.number} är redan rapporterad` : `Rapportera ledtråd ${entry.number} lodrätt`}
                        title={reported ? 'Redan rapporterad' : 'Rapportera dålig ledtråd'}
                        onClick={() => onReport(entry)}
                        disabled={reported}
                      >
                        {reported ? '✅' : '🚩'}
                      </button>
                    ) : null}
                  </div>
                </li>
              );
              })}
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}
