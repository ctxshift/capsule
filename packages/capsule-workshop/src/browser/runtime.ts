import { DomTarget, type BrowserTheme } from './dom';
import { applyPacket, type UiNode } from './packet';
export interface BrowserGame {
  nextObservation(): Uint8Array;
  request(capability: number, payload: Uint8Array): unknown;
}

interface GuestRealm extends Window {
  Function: FunctionConstructor;
  portableUi: {
    reserve(): bigint;
    submit(bytes: Uint8Array): number;
    nextEvent(): Uint8Array;
    nextObservation(): Uint8Array;
    requestAction(
      handle: bigint,
      capability: number,
      payload: Uint8Array,
    ): void;
  };
  portableUiUpdate(): void;
  portableUiDispose(): void;
  __portableNextDelayV1(): number;
}

export class BrowserGeneration {
  private retired = false;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private queue: Uint8Array[] = [];
  private reserved = 0n;
  private nodes = new Map<bigint, UiNode>();
  private target: DomTarget;
  private realm: GuestRealm;
  private scheduled = false;
  private active = false;
  private stagedRequests: { capability: number; payload: Uint8Array }[] = [];
  readonly frame: HTMLIFrameElement;
  private constructor(
    theme: BrowserTheme,
    private game: BrowserGame,
    restoreFocus: () => void,
    private failure: (error: Error) => void,
    private layoutNodeLimit = 128,
  ) {
    this.frame = document.createElement('iframe');
    this.frame.title = 'Native-compatible addon';
    this.frame.style.cssText =
      'width:1440px;height:900px;border:0;display:none';
    document.querySelector('#stage-host')!.append(this.frame);
    this.realm = this.frame.contentWindow as GuestRealm;
    const doc = this.realm.document;
    const style = doc.createElement('style');
    style.textContent = theme.css;
    doc.head.append(style);
    const stage = doc.createElement('div');
    stage.id = 'stage';
    stage.style.cssText =
      'width:1440px;height:900px;position:relative;overflow:hidden';
    doc.body.append(stage);
    this.target = new DomTarget(
      stage,
      theme,
      (bytes) => this.enqueue(bytes),
      restoreFocus,
    );
    this.realm.portableUi = {
      reserve: () => ++this.reserved,
      submit: (bytes) => {
        if (!this.retired) {
          const next = applyPacket(this.nodes, bytes, this.layoutNodeLimit);
          this.target.apply(next);
          this.nodes = next;
        }
        return 0;
      },
      nextEvent: () => this.queue.shift() ?? new Uint8Array(),
      nextObservation: () =>
        this.active ? this.game.nextObservation() : new Uint8Array(),
      requestAction: (handle, capability, payload) => {
        if (this.retired) return;
        const node = this.nodes.get(handle);
        if (!node || !(node.events & (1 | 64)))
          throw Error('Mock action source is not a live activation');
        if (
          payload.length > 256 ||
          !Number.isInteger(capability) ||
          capability < 0 ||
          capability > 65535
        )
          throw Error('Mock action bounds');
        if (this.active) {
          this.game.request(capability, payload);
          // A host may publish a pending/result observation while handling intent.
          this.enqueue(new Uint8Array());
        } else {
          if (this.stagedRequests.length >= 128)
            throw Error('Mock candidate action quota');
          this.stagedRequests.push({ capability, payload: payload.slice() });
        }
      },
    };
  }
  static async create(
    sdk: string,
    bundle: string,
    theme: BrowserTheme,
    game: BrowserGame,
    restoreFocus: () => void,
    failure: (error: Error) => void,
    layoutNodeLimit = 128,
  ) {
    const generation = new BrowserGeneration(
      theme,
      game,
      restoreFocus,
      failure,
      layoutNodeLimit,
    );
    try {
      for (const font of theme.fonts)
        if (!(await generation.realm.document.fonts.load(font)).length)
          throw Error('Exact local font unavailable: ' + font);
      await Promise.all(
        theme.images.map((url) => {
          const image = generation.realm.document.createElement('img');
          image.src = url;
          return image.decode();
        }),
      );
      generation.realm.Function(
        sdk + '\n' + bundle + '\n//# sourceURL=portable-ui-addon.js',
      )();
      generation.realm.portableUiUpdate();
      return generation;
    } catch (error) {
      try {
        generation.dispose();
      } catch {
        /* Preserve the candidate's original rejection. */
      }
      throw error;
    }
  }
  enqueue(bytes: Uint8Array) {
    if (this.retired) return;
    if (this.queue.length >= 128) {
      this.failure(Error('Browser input queue quota'));
      this.dispose();
      return;
    }
    this.queue.push(bytes.slice());
    if (!this.scheduled) {
      this.scheduled = true;
      queueMicrotask(() => {
        this.scheduled = false;
        this.update();
      });
    }
  }
  private update() {
    if (this.retired) return;
    try {
      this.realm.portableUiUpdate();
      this.schedule();
    } catch (error) {
      this.failure(error instanceof Error ? error : Error(String(error)));
      this.dispose();
    }
  }
  private schedule() {
    if (this.timer) clearTimeout(this.timer);
    const delay = this.realm.__portableNextDelayV1();
    if (delay >= 0 || this.queue.length)
      this.timer = setTimeout(
        () => this.update(),
        this.queue.length ? 0 : delay,
      );
  }
  show() {
    this.active = true;
    this.frame.style.display = 'block';
    for (const request of this.stagedRequests)
      this.game.request(request.capability, request.payload);
    this.stagedRequests = [];
    this.schedule();
  }
  snapshot() {
    return [...this.nodes.values()].map((node) => ({
      ...node,
      id: String(node.id),
      parent: String(node.parent),
    }));
  }
  dispose() {
    if (this.retired) return;
    this.retired = true;
    if (this.timer) clearTimeout(this.timer);
    this.queue = [];
    this.stagedRequests = [];
    try {
      this.realm.portableUiDispose?.();
    } finally {
      this.target.dispose();
      this.nodes.clear();
      this.frame.remove();
    }
  }
}
