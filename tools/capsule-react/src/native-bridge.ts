// Private SDK transport, not an addon authoring API or a grant of host authority.
export interface NativeBridge {
  reserve(): bigint;
  submit(bytes: Uint8Array): void;
  nextEvent(): Uint8Array;
  nextObservation(): Uint8Array;
  requestAction(handle: bigint, capability: number, payload: Uint8Array): void;
}

export const bridgeMethods = [
  'reserve',
  'submit',
  'nextEvent',
  'nextObservation',
  'requestAction',
] as const;

export const bridgeProfile = Object.freeze({
  abiVersion: 1,
  profile: 'react-ui-v1',
  packageData: false,
  packageImages: false,
});

export function installNativeBridge(scope: {
  portableUi?: unknown;
}): NativeBridge {
  const raw = scope.portableUi;
  if (raw === null || typeof raw !== 'object')
    throw new Error('Capsule bridge unavailable');
  const functions = raw as Record<string, unknown>;
  for (const name of bridgeMethods) {
    if (typeof functions[name] !== 'function')
      throw new Error(`Capsule bridge missing required operation: ${name}`);
  }
  // Capture validated methods before addon evaluation. Legacy carrier package
  // functions deliberately remain outside this host profile's JS surface.
  const bridge = Object.freeze({
    ...bridgeProfile,
    reserve: functions.reserve as NativeBridge['reserve'],
    submit: functions.submit as NativeBridge['submit'],
    nextEvent: functions.nextEvent as NativeBridge['nextEvent'],
    nextObservation:
      functions.nextObservation as NativeBridge['nextObservation'],
    requestAction: functions.requestAction as NativeBridge['requestAction'],
  });
  Object.defineProperty(scope, 'portableUi', {
    value: bridge,
    configurable: false,
    writable: false,
  });
  return bridge;
}
