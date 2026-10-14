import React, { useRef, type ReactNode } from 'react';
import { Box, Image, type KeyEvent, type PointerEvent } from './index';

export function scrollOffset(
  value: number,
  contentHeight: number,
  height: number,
) {
  if (
    ![value, contentHeight, height].every(Number.isFinite) ||
    contentHeight < 0 ||
    height <= 0
  )
    throw Error('Invalid scroll geometry');
  return Math.max(0, Math.min(Math.max(0, contentHeight - height), value));
}

export function scrollKeyOffset(
  event: KeyEvent,
  offset: number,
  contentHeight: number,
  height: number,
) {
  if (!event.down || event.modifiers) return offset;
  const next =
    event.key === 'Home'
      ? 0
      : event.key === 'End'
        ? contentHeight
        : event.key === 'PageUp'
          ? offset - height
          : event.key === 'PageDown'
            ? offset + height
            : event.key === 'Up'
              ? offset - 64
              : event.key === 'Down'
                ? offset + 64
                : offset;
  return scrollOffset(next, contentHeight, height);
}

export interface ScrollListProps {
  width: number;
  height: number;
  /** Top edge in design coordinates, for track hit testing. */
  originY: number;
  rowHeight: number;
  rowKeys: readonly string[];
  offset: number;
  onScroll: (offset: number) => void;
  onFocus: () => void;
  renderRow: (index: number) => ReactNode;
  skin: { up: string; down: string; track: string; thumb: string };
}

/** Controlled virtual rows; the screen routes keyboard input to its focused list. */
export function ScrollList({
  width,
  height,
  originY,
  rowHeight,
  rowKeys,
  offset,
  onScroll,
  onFocus,
  renderRow,
  skin,
}: ScrollListProps) {
  if (
    !Number.isFinite(rowHeight) ||
    rowHeight <= 0 ||
    width < 20 ||
    height < 50 ||
    rowKeys.length > 1024 ||
    Math.ceil(height / rowHeight) > 64 ||
    new Set(rowKeys).size !== rowKeys.length
  )
    throw Error('Invalid scroll list');
  const contentHeight = rowHeight * rowKeys.length;
  const current = scrollOffset(offset, contentHeight, height);
  const maximum = Math.max(0, contentHeight - height);
  const trackHeight = height - 26;
  const thumbHeight = Math.min(
    trackHeight,
    Math.max(24, (trackHeight * height) / Math.max(height, contentHeight)),
  );
  const travel = trackHeight - thumbHeight;
  const thumbTop = maximum ? (current / maximum) * travel : 0;
  const drag = useRef<{ id: number; y: number; offset: number } | null>(null);
  const change = (value: number) =>
    onScroll(scrollOffset(value, contentHeight, height));
  const thumbPointer = (event: PointerEvent) => {
    if (event.phase === 'down' && event.button === 0) {
      onFocus();
      drag.current = { id: event.pointerId, y: event.y, offset: current };
    } else if (drag.current?.id === event.pointerId) {
      if (event.phase === 'move' && travel > 0)
        change(
          drag.current.offset + ((event.y - drag.current.y) * maximum) / travel,
        );
      if (event.phase === 'up' || event.phase === 'cancel') drag.current = null;
    }
  };
  const first = Math.floor(current / rowHeight);
  const last = Math.min(
    rowKeys.length,
    Math.ceil((current + height) / rowHeight),
  );
  return (
    <Box
      width={width}
      height={height}
      onFocus={onFocus}
      onWheel={(event) => change(current + event.deltaY)}
    >
      <Box width={width - 19} height={height} clip>
        {rowKeys.slice(first, last).map((key, index) => (
          <Box
            key={key}
            position="absolute"
            x={0}
            y={(first + index) * rowHeight - current}
            width={width - 19}
            height={rowHeight}
          >
            {renderRow(first + index)}
          </Box>
        ))}
      </Box>
      {maximum > 0 && (
        <>
          <Box
            position="absolute"
            x={width - 12.5}
            y={0}
            width={12}
            height={10}
            onActivate={() => {
              onFocus();
              change(current - 64);
            }}
          >
            <Image width={12} height={10} asset={skin.up} />
          </Box>
          <Box
            position="absolute"
            x={width - 10.5}
            y={13}
            width={8}
            height={trackHeight}
            onPointer={(event) => {
              if (event.phase === 'down' && event.button === 0) {
                onFocus();
                change(((event.y - originY - 13) / trackHeight) * maximum);
              }
            }}
          >
            <Image width={8} height={trackHeight} asset={skin.track} />
          </Box>
          <Box
            position="absolute"
            x={width - 10.5}
            y={13 + thumbTop}
            width={8}
            height={thumbHeight}
            onPointer={thumbPointer}
          >
            <Image width={8} height={thumbHeight} asset={skin.thumb} />
          </Box>
          <Box
            position="absolute"
            x={width - 12.5}
            y={height - 10}
            width={12}
            height={10}
            onActivate={() => {
              onFocus();
              change(current + 64);
            }}
          >
            <Image width={12} height={10} asset={skin.down} />
          </Box>
        </>
      )}
    </Box>
  );
}
