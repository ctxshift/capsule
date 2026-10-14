import type { ReactNode } from 'react';
export interface ActionEvent {
  request(capability: number, payload?: Uint8Array): void;
}
export interface PointerEvent {
  phase: 'down' | 'move' | 'up' | 'cancel';
  pointerId: number;
  x: number;
  y: number;
  button: number;
  modifiers: number;
}
export interface KeyEvent {
  key: 'Escape' | 'Tab' | 'Enter' | 'Space' | 'Left' | 'Right' | 'Up' | 'Down';
  down: boolean;
  modifiers: number;
  repeat: boolean;
}
export interface Props {
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
  onClose?: () => void;
  onHover?: () => void;
  onHoverChange?: (hovered: boolean) => void;
  onPointer?: (event: PointerEvent) => void;
  onKey?: (event: KeyEvent) => void;
  onFocus?: () => void;
  onValueChange?: (value: number) => void;
  options?: readonly string[];
  selectedIndex?: number;
  onSelectionChange?: (index: number) => void;
}
