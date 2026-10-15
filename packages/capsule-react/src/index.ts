import type { ComponentType, ReactNode } from 'react';
export type {
  WorldAnchorOptions,
  PresentationEffectOptions,
} from './presentation';
import type { Props } from './props';
export type {
  ActionEvent,
  TextEditEvent,
  PointerEvent,
  KeyEvent,
  WheelEvent,
  Props,
} from './props';

interface Runtime {
  Box: ComponentType<Props>;
  WorldAnchor: ComponentType<Props>;
  Effect: ComponentType<Props>;
  Button: ComponentType<Props>;
  Range: ComponentType<Props>;
  Select: ComponentType<Props>;
  Text: ComponentType<Props>;
  TextInput: ComponentType<Props>;
  Image: ComponentType<Props>;
  Window: ComponentType<Props>;
  Portrait: ComponentType<Props>;
  Viewport: ComponentType<Props>;
  Slot: ComponentType<Props>;
  Progress: ComponentType<Props>;
  Overlay: ComponentType<Props>;
  render(node: ReactNode, options?: { layout: 'yoga' }): void;
  useViewport(): Readonly<{ width: number; height: number }>;
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
  WorldAnchor,
  Effect,
  Button,
  Range,
  Select,
  Text,
  TextInput,
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

/** Full host surface in logical UI units; updates when its dimensions change. */
export function useViewport(): Readonly<{ width: number; height: number }> {
  return runtime.useViewport();
}

/** One-shot work on the guest's bounded scheduler; cancel on unmount. */
export function schedule(delay: number, callback: () => void): () => void {
  if (!Number.isFinite(delay) || delay < 0 || delay > 60000)
    throw Error('Invalid scheduled delay');
  const timers = globalThis as unknown as {
    setTimeout(callback: () => void, delay: number): number;
    clearTimeout(id: number): void;
  };
  const id = timers.setTimeout(callback, delay);
  return () => timers.clearTimeout(id);
}
