import { codec } from '@capsule/features/codec';
import type { ProviderTransport } from '@capsule/features/runtime';
import {
  NameplateState,
  NameplateSelection,
  SelectNameplate,
  unavailableNameplates,
  type NameplateStateValue,
} from './nameplates';

/** Explicit browser fixture for the contract; never a game authority fallback. */
export function createNameplatesMock(
  initial: NameplateStateValue = unavailableNameplates,
) {
  let state = initial;
  let bytes = codec(NameplateState).encode(state);
  const subscribers = new Set<(bytes: Uint8Array) => void>();
  function publish(next: NameplateStateValue) {
    const encoded = codec(NameplateState).encode(next);
    state = next;
    bytes = encoded;
    for (const receive of subscribers) receive(bytes);
  }
  const transport: ProviderTransport = {
    subscribe(name, receive) {
      if (name !== 'State') throw Error('Unknown nameplate state');
      subscribers.add(receive);
      receive(bytes);
      return () => {
        subscribers.delete(receive);
      };
    },
    async request(operation, payload, signal) {
      if (operation !== 'Select') throw Error('Unknown nameplate operation');
      const intent = codec(SelectNameplate).decode(payload);
      const selected =
        !signal.aborted &&
        state.available &&
        intent.epoch === state.epoch &&
        state.actors.some(
          (actor) =>
            actor.entity === intent.entity &&
            actor.generation === intent.generation,
        );
      if (selected) publish({ ...state, selected: intent.entity });
      return codec(NameplateSelection).encode({ selected });
    },
  };
  return { transport, publish };
}
