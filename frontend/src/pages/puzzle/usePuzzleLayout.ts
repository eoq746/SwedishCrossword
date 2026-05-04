import { useLayoutEffect, useState } from 'react';
import type { RefObject } from 'react';

interface PuzzleLayoutOptions {
  enabled: boolean;
  gridSectionRef: RefObject<HTMLDivElement | null>;
  gridHeaderRef: RefObject<HTMLDivElement | null>;
  controlsRef: RefObject<HTMLDivElement | null>;
  columns: number;
  rows: number;
  layoutKey?: string;
}

interface PuzzleLayoutState {
  boardHeight?: number;
  gridAreaHeight?: number;
  gridCellSize?: number;
  gridWidth?: number;
  gridHeight?: number;
  supportPanelHeight?: number;
}

export function usePuzzleLayout({
  enabled,
  gridSectionRef,
  gridHeaderRef,
  controlsRef,
  columns,
  rows,
  layoutKey,
}: PuzzleLayoutOptions): PuzzleLayoutState {
  const [layout, setLayout] = useState<PuzzleLayoutState>({});

  useLayoutEffect(() => {
    if (!enabled) {
      setLayout({});
      return;
    }

    const computeLayout = () => {
      const gridSection = gridSectionRef.current;
      if (!gridSection || columns <= 0 || rows <= 0) return;

      const isLargeScreen = window.matchMedia('(min-width:1200px)').matches;
      if (!isLargeScreen) {
        setLayout({});
        return;
      }

      const gridSectionStyle = window.getComputedStyle(gridSection);
      const verticalPadding =
        (parseFloat(gridSectionStyle.paddingTop || '0') || 0) +
        (parseFloat(gridSectionStyle.paddingBottom || '0') || 0);
      const horizontalPadding =
        (parseFloat(gridSectionStyle.paddingLeft || '0') || 0) +
        (parseFloat(gridSectionStyle.paddingRight || '0') || 0);

      const headerHeight = Math.ceil(gridHeaderRef.current?.getBoundingClientRect().height ?? 0);
      const headerMarginBottom = Math.ceil(parseFloat(window.getComputedStyle(gridHeaderRef.current ?? gridSection).marginBottom || '0') || 0);
      const controlsHeight = Math.ceil(controlsRef.current?.getBoundingClientRect().height ?? 0);
      const controlsMarginBottom = Math.ceil(parseFloat(window.getComputedStyle(controlsRef.current ?? gridSection).marginBottom || '0') || 0);
      const gridInnerGap = 12;

      const chromeHeight = verticalPadding + headerHeight + headerMarginBottom + controlsHeight + controlsMarginBottom + gridInnerGap;
      const availableGridWidth = Math.max(320, Math.floor(gridSection.clientWidth - horizontalPadding));

      const borderAllowance = 8;
      const columnGapAllowance = Math.max(0, columns - 1);
      const rowGapAllowance = Math.max(0, rows - 1);
      const maxCellSize = rows <= 10 ? 78 : rows <= 15 ? 64 : 54;
      const gridCellSize = Math.max(
        28,
        Math.min(
          Math.floor((availableGridWidth - borderAllowance - columnGapAllowance) / columns),
          maxCellSize,
        ),
      );

      const gridWidth = borderAllowance + columnGapAllowance + gridCellSize * columns;
      const gridHeight = borderAllowance + rowGapAllowance + gridCellSize * rows;
      const boardHeight = chromeHeight + gridHeight;
      const supportPanelHeight = Math.max(260, Math.floor((boardHeight - 20) / 2));

      setLayout({
        boardHeight,
        gridAreaHeight: gridHeight,
        gridCellSize,
        gridWidth,
        gridHeight,
        supportPanelHeight,
      });
    };

    const raf = requestAnimationFrame(computeLayout);
    const settleRaf = requestAnimationFrame(() => requestAnimationFrame(computeLayout));
    const settleTimeout = window.setTimeout(computeLayout, 120);
    window.addEventListener('resize', computeLayout);
    window.addEventListener('orientationchange', computeLayout);

    let resizeObserver: ResizeObserver | null = null;
    if (typeof ResizeObserver !== 'undefined') {
      resizeObserver = new ResizeObserver(computeLayout);
      if (gridSectionRef.current) resizeObserver.observe(gridSectionRef.current);
      if (gridHeaderRef.current) resizeObserver.observe(gridHeaderRef.current);
      if (controlsRef.current) resizeObserver.observe(controlsRef.current);
    }

    return () => {
      cancelAnimationFrame(raf);
      cancelAnimationFrame(settleRaf);
      window.clearTimeout(settleTimeout);
      window.removeEventListener('resize', computeLayout);
      window.removeEventListener('orientationchange', computeLayout);
      resizeObserver?.disconnect();
    };
  }, [columns, controlsRef, enabled, gridHeaderRef, gridSectionRef, layoutKey, rows]);

  return layout;
}
