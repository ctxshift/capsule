// Engine diagnostic only; this private bridge is not the addon authoring API.
const bridge = (globalThis as any).portableUi;
let hello = 'hello',
  changes = 0;
bridge.requestAction(0n, 999, new Uint8Array([0, hello.length, 0]));
(globalThis as any).portableUiUpdate = () => {
  const bytes = bridge.nextObservation();
  if (bytes.length) {
    const value = bytes[8];
    hello = 'hello ' + value;
    changes++;
    bridge.requestAction(
      0n,
      999,
      new Uint8Array([changes, hello.length, value]),
    );
  }
};
