import { test, expect } from 'vitest';
import {
  emptyWindows,
  openWindow,
  closeWindow,
  moveWindow,
  dragPosition,
} from '../src/window-state';

test('window order and retained position survive close/reopen', () => {
  let state = openWindow(openWindow(emptyWindows(), 'system'), 'settings');
  state = moveWindow(state, 'system', -20, 1000, 370, 318, 1440, 900);
  expect(state.positions.system).toEqual({ x: 0, y: 582 });
  state = openWindow(state, 'system');
  expect(state.order.at(-1)).toBe('system');
  state = closeWindow(state);
  expect(state.order.at(-1)).toBe('settings');
  state = openWindow(state, 'system');
  expect(state.positions.system.y).toBe(582);
});
test('drag uses capture pointer identity', () => {
  const drag = {
    id: 'system',
    pointerId: 2,
    startX: 10,
    startY: 20,
    origin: { x: 50, y: 60 },
  };
  expect(dragPosition(drag, 3, 15, 25)).toBeNull();
  expect(dragPosition(drag, 2, 15, 25)).toEqual({ x: 55, y: 65 });
});
test('window state rejects nonfinite positions and bounds window count', () => {
  let state = openWindow(emptyWindows(), 'system');
  expect(() =>
    moveWindow(state, 'system', Infinity, 0, 1, 1, 1440, 900),
  ).toThrow();
  for (let i = 0; i < 15; i++) state = openWindow(state, 'extra' + i);
  expect(() => openWindow(state, 'overflow')).toThrow();
});
