import { codec } from '@capsule/features/codec';
import type { ProviderTransport } from '@capsule/features/runtime';
import {
  SkillProgressionState,
  unavailableProgression,
  type SkillProgressionStateValue,
} from './progression';
import {
  TrainingState,
  TrainingIntent,
  TrainingResult,
  unavailableTraining,
  type TrainingStateValue,
} from './training';

export function createProgressionMock(
  initial: SkillProgressionStateValue = unavailableProgression,
) {
  let bytes = codec(SkillProgressionState).encode(initial);
  const listeners = new Set<(bytes: Uint8Array) => void>();
  const transport: ProviderTransport = {
    subscribe(name, receive) {
      if (name !== 'State') throw Error('Unknown progression state');
      listeners.add(receive);
      receive(bytes);
      return () => {
        listeners.delete(receive);
      };
    },
    async request() {
      throw Error('Progression is read-only');
    },
  };
  return {
    transport,
    publish(next: SkillProgressionStateValue) {
      bytes = codec(SkillProgressionState).encode(next);
      for (const receive of listeners) receive(bytes);
    },
  };
}

export function createTrainingMock(
  initial: TrainingStateValue = unavailableTraining,
  interact: (offer: string, close: boolean) => boolean = () => true,
) {
  let state = initial,
    bytes = codec(TrainingState).encode(initial);
  const consumed = new Set<string>();
  const listeners = new Set<(bytes: Uint8Array) => void>();
  const transport: ProviderTransport = {
    subscribe(name, receive) {
      if (name !== 'State') throw Error('Unknown training state');
      listeners.add(receive);
      receive(bytes);
      return () => {
        listeners.delete(receive);
      };
    },
    async request(name, payload, signal) {
      if (name !== 'Interact') throw Error('Unknown training operation');
      const value = codec(TrainingIntent).decode(payload);
      const key = `${value.epoch}:${value.ownerGeneration}:${value.sequence}`;
      const offer = state.offers.find((o) => o.id === value.offer);
      let accepted = false;
      if (
        !signal.aborted &&
        state.open &&
        state.freshness !== 'unavailable' &&
        (value.close || state.freshness === 'fresh') &&
        value.epoch === state.epoch &&
        value.ownerGeneration === state.ownerGeneration &&
        value.sequence === state.sequence &&
        !consumed.has(key) &&
        (value.close ||
          (offer?.available &&
            offer.costs.every((c) => c.affordable) &&
            offer.requirements.every((r) => r.met)))
      ) {
        consumed.add(key);
        accepted = interact(value.offer, value.close);
      }
      return codec(TrainingResult).encode({ accepted });
    },
  };
  return {
    transport,
    publish(next: TrainingStateValue) {
      const nextBytes = codec(TrainingState).encode(next);
      if (
        next.epoch !== state.epoch ||
        next.ownerGeneration !== state.ownerGeneration ||
        next.sequence !== state.sequence
      )
        consumed.clear();
      state = next;
      bytes = nextBytes;
      for (const receive of listeners) receive(bytes);
    },
  };
}
