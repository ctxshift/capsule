import type { ReactNode } from 'react';
export interface ActionEvent {
  request(capability: number, payload?: Uint8Array): void;
}
export interface PointerEvent extends ActionEvent {
  phase: 'down' | 'move' | 'up' | 'cancel';
  pointerId: number;
  x: number;
  y: number;
  /** DOM button numbering: primary 0, auxiliary 1, secondary 2. */
  button: number;
  modifiers: number;
}
export interface KeyEvent extends ActionEvent {
  key:
    | 'Escape'
    | 'Tab'
    | 'Enter'
    | 'Space'
    | 'Left'
    | 'Right'
    | 'Up'
    | 'Down'
    | 'E'
    | 'I'
    | 'C'
    | 'Home'
    | 'End'
    | 'PageUp'
    | 'PageDown'
    | 'J'
    | 'K';
  down: boolean;
  modifiers: number;
  repeat: boolean;
}
export interface WheelEvent {
  /** Design-unit deltas; positive values scroll right/down. */
  deltaX: number;
  deltaY: number;
  modifiers: number;
}
export interface Props {
  /** Opaque host-admitted live surface key, only for Viewport. */
  surface?: string;
  children?: ReactNode;
  width?: number;
  height?: number;
  x?: number;
  y?: number;
  direction?: 'row' | 'column';
  gap?: number;
  position?: 'absolute';
  clip?: boolean;
  grow?: number;
  shrink?: number;
  align?: 'start' | 'center' | 'end' | 'stretch';
  justify?: 'start' | 'center' | 'end' | 'space-between';
  padding?: number;
  paddingLeft?: number;
  paddingTop?: number;
  paddingRight?: number;
  paddingBottom?: number;
  anchor?: 'top-left' | 'top-right' | 'bottom-left' | 'bottom-right';
  style?: string;
  asset?: string;
  title?: string;
  visible?: boolean;
  value?: number;
  step?: number;
  brightness?: number;
  onActivate?: (event: ActionEvent) => void;
  onClose?: (event: ActionEvent) => void;
  onHover?: () => void;
  onHoverChange?: (hovered: boolean) => void;
  onPointer?: (event: PointerEvent) => void;
  onKey?: (event: KeyEvent) => void;
  onWheel?: (event: WheelEvent) => void;
  onFocus?: () => void;
  onValueChange?: (value: number) => void;
  options?: readonly string[];
  selectedIndex?: number;
  onSelectionChange?: (index: number) => void;
}
