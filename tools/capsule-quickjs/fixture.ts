// Temporary runtime smoke fixture. React/TSX components replace this imperative
// protocol test with declarative React/TSX components; addon authors will not
// write against this surface.
type Handle = bigint;
type Rect = { x: number; y: number; width: number; height: number };
type Observation = { topic: number; schema: number; payload: Uint8Array };
type UiEvent = { kind: number; handle: Handle; payload: Uint8Array };
interface ProtocolTestBridge {
  readonly Event: { readonly Activate: number };
  window(rect: Rect, title: string): Handle;
  portrait(parent: Handle, rect: Rect, asset: string): Handle;
  slot(parent: Handle, rect: Rect, asset: string, events: number): Handle;
  text(parent: Handle, rect: Rect, value: string): Handle;
  setText(handle: Handle, value: string): void;
  flush(): void;
  observations(): Observation[];
  events(): UiEvent[];
  requestAction(handle: Handle, capability: number): void;
}
declare const ui: ProtocolTestBridge;

const HealthObservation = 1;
const UseFixtureSlot = 1;
let windowHandle: Handle = 0n;
let slotHandle: Handle = 0n;
let healthHandle: Handle = 0n;
let health = 72;
let shownHealth = -1;

globalThis.portableUiUpdate = () => {
  for (const observation of ui.observations()) {
    if (
      observation.topic === HealthObservation &&
      observation.schema === 1 &&
      observation.payload.length >= 2
    ) {
      const view = new DataView(observation.payload.buffer);
      health = view.getUint16(0, true);
    }
  }
  if (windowHandle === 0n) {
    windowHandle = ui.window(
      { x: 80, y: 210, width: 460, height: 260 },
      'Inventory',
    );
    ui.portrait(
      windowHandle,
      { x: 28, y: 68, width: 106, height: 106 },
      'player-portrait-fixture-aelith',
    );
    slotHandle = ui.slot(
      windowHandle,
      { x: 184, y: 74, width: 55, height: 55 },
      'merchant-sword',
      ui.Event.Activate,
    );
    healthHandle = ui.text(
      windowHandle,
      { x: 184, y: 142, width: 240, height: 32 },
      `Fixture health ${health}/100`,
    );
    ui.flush();
    shownHealth = health;
  }
  if (health !== shownHealth) {
    ui.setText(healthHandle, `Fixture health ${health}/100`);
    ui.flush();
    shownHealth = health;
  }
  for (const event of ui.events())
    if (event.kind === ui.Event.Activate && event.handle === slotHandle)
      ui.requestAction(slotHandle, UseFixtureSlot);
};
