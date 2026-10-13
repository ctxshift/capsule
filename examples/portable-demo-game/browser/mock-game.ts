import type { BrowserGame } from '@capsule/workshop/browser';

export class StarshipGame implements BrowserGame {
  private charge = 35;
  private pending: Uint8Array | null = null;
  constructor(private readonly wake: () => void) {}

  publish() {
    // One replaceable observation bounds the mock queue.
    const bytes = new Uint8Array(10);
    const view = new DataView(bytes.buffer);
    view.setUint16(0, 7, true);
    view.setUint16(2, 1, true);
    view.setUint32(4, 2, true);
    view.setUint16(8, this.charge, true);
    this.pending = bytes;
    queueMicrotask(this.wake);
  }

  nextObservation(): Uint8Array {
    const bytes = this.pending ?? new Uint8Array();
    this.pending = null;
    return bytes;
  }

  request(capability: number, payload: Uint8Array): void {
    if (capability !== 7 || payload.length !== 1 || payload[0] !== 1)
      throw Error('Unsupported starship request');
    this.charge = Math.min(100, this.charge + 5);
    this.publish();
  }
}
