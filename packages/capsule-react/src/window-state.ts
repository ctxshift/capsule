// Generic bounded window state. Game-specific names and content stay in TSX.
export interface WindowPosition {
  x: number;
  y: number;
}
export interface WindowState {
  order: readonly string[];
  positions: Readonly<Record<string, WindowPosition>>;
}
export const emptyWindows = (): WindowState => ({ order: [], positions: {} });

export function openWindow(state: WindowState, id: string): WindowState {
  if (!id || id.length > 64) throw Error('Invalid window id');
  if (!state.order.includes(id) && state.order.length >= 16)
    throw Error('Window quota');
  if (state.order[state.order.length - 1] === id) return state;
  return {
    ...state,
    order: [...state.order.filter((value) => value !== id), id],
  };
}
export function closeWindow(
  state: WindowState,
  id = state.order[state.order.length - 1],
): WindowState {
  if (!state.order.includes(id)) return state;
  return { ...state, order: state.order.filter((value) => value !== id) };
}
export function moveWindow(
  state: WindowState,
  id: string,
  x: number,
  y: number,
  width: number,
  height: number,
  viewportWidth: number,
  viewportHeight: number,
): WindowState {
  if (
    ![x, y, width, height, viewportWidth, viewportHeight].every(
      Number.isFinite,
    ) ||
    width <= 0 ||
    height <= 0 ||
    viewportWidth <= 0 ||
    viewportHeight <= 0
  )
    throw Error('Invalid window geometry');
  if (!state.order.includes(id)) return state;
  if (
    !Object.prototype.hasOwnProperty.call(state.positions, id) &&
    Object.keys(state.positions).length >= 16
  )
    throw Error('Retained window position quota');
  return {
    ...state,
    positions: {
      ...state.positions,
      [id]: {
        x: Math.max(0, Math.min(viewportWidth - width, x)),
        y: Math.max(0, Math.min(viewportHeight - height, y)),
      },
    },
  };
}

export interface WindowDrag {
  readonly id: string;
  readonly pointerId: number;
  readonly startX: number;
  readonly startY: number;
  readonly origin: WindowPosition;
}
export function dragPosition(
  drag: WindowDrag,
  pointerId: number,
  x: number,
  y: number,
): WindowPosition | null {
  return pointerId !== drag.pointerId
    ? null
    : {
        x: drag.origin.x + x - drag.startX,
        y: drag.origin.y + y - drag.startY,
      };
}
