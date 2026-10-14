import type { ComponentType, ReactNode } from 'react';
import type { Props } from './props';
export type {
  ActionEvent,
  PointerEvent,
  KeyEvent,
  WheelEvent,
  Props,
} from './props';

interface Runtime {
  Box: ComponentType<Props>;
  Button: ComponentType<Props>;
  Range: ComponentType<Props>;
  Select: ComponentType<Props>;
  Text: ComponentType<Props>;
  Image: ComponentType<Props>;
  Window: ComponentType<Props>;
  Portrait: ComponentType<Props>;
  Viewport: ComponentType<Props>;
  Slot: ComponentType<Props>;
  Progress: ComponentType<Props>;
  Overlay: ComponentType<Props>;
  render(node: ReactNode, options?: { layout: 'yoga' }): void;
  useObservation<T>(
    topic: number,
    schema: number,
    decode: (bytes: Uint8Array) => T,
    fallback: T,
  ): T;
}

// The host installs the compatible runtime before evaluating an addon bundle.
// This binding grants no capabilities; the native boundary enforces them.
const runtime = (globalThis as unknown as { __portableReact: Runtime })
  .__portableReact;
if (!runtime) throw new Error('Capsule React runtime is not installed');

export const {
  Box,
  Button,
  Range,
  Select,
  Text,
  Image,
  Window,
  Portrait,
  Viewport,
  Slot,
  Progress,
  Overlay,
} = runtime;
export function render(node: ReactNode, options?: { layout: 'yoga' }): void {
  runtime.render(node, options);
}
export function useObservation<T>(
  topic: number,
  schema: number,
  decode: (bytes: Uint8Array) => T,
  fallback: T,
): T {
  return runtime.useObservation(topic, schema, decode, fallback);
}
