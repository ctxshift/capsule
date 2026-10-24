import { codec } from '@capsule/features/codec';
import type { Value } from '@capsule/features';
import type { ProviderTransport } from '@capsule/features/runtime';
import { DialogueState, DialogueIntent, DialogueResult } from './dialogue';
export function createDialogueMock(
  initial: Value<typeof DialogueState> = {
    revision: 0,
    open: false,
    title: '',
    body: '',
    prompt: '',
    primary: '',
    primaryEnabled: false,
    secondary: '',
    secondaryEnabled: false,
  },
  act: (action: number) => boolean = () => true,
) {
  let state = initial;
  const listeners = new Set<(bytes: Uint8Array) => void>();
  const transport: ProviderTransport = {
    subscribe(name, receive) {
      if (name !== 'State') throw Error('Unknown dialogue state');
      listeners.add(receive);
      receive(codec(DialogueState).encode(state));
      return () => {
        listeners.delete(receive);
      };
    },
    async request(name, bytes, signal) {
      if (name !== 'Act') throw Error('Unknown dialogue request');
      const intent = codec(DialogueIntent).decode(bytes);
      const allowed =
        state.open &&
        intent.revision === state.revision &&
        !signal.aborted &&
        (intent.action === 0 ||
          (intent.action === 1
            ? state.primaryEnabled
            : intent.action === 2 && state.secondaryEnabled));
      return codec(DialogueResult).encode({
        accepted: allowed && act(intent.action),
      });
    },
  };
  return {
    transport,
    publish(next: Value<typeof DialogueState>) {
      state = next;
      for (const receive of listeners)
        receive(codec(DialogueState).encode(state));
    },
  };
}
