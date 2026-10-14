import { input, type UiNode } from './packet';
import { layout, type TextMetrics } from './layout';

export interface BrowserTheme {
  css: string;
  fonts: readonly string[];
  images: readonly string[];
  metrics(document: Document, style: string): TextMetrics;
  decorate(element: HTMLElement, node: UiNode, close: () => void): void;
  update?(element: HTMLElement, node: UiNode): void;
}

export class DomTarget {
  private elements = new Map<bigint, HTMLElement>();
  private nodes: ReadonlyMap<bigint, UiNode> = new Map();
  private subscriptions = new AbortController();
  private capture: {
    id: bigint;
    pointer: number;
    x: number;
    y: number;
    button: number;
    modifiers: number;
  } | null = null;
  constructor(
    private stage: HTMLElement,
    private theme: BrowserTheme,
    private send: (bytes: Uint8Array) => void,
    private restoreFocus: () => void,
  ) {
    stage.ownerDocument.addEventListener('keydown', this.key, {
      signal: this.subscriptions.signal,
    });
    stage.ownerDocument.defaultView!.addEventListener(
      'blur',
      () => this.cancel(),
      { signal: this.subscriptions.signal },
    );
  }
  private modifiers(event: KeyboardEvent | PointerEvent) {
    return (
      (event.shiftKey ? 1 : 0) |
      (event.ctrlKey ? 2 : 0) |
      (event.altKey ? 4 : 0) |
      (event.metaKey ? 8 : 0)
    );
  }
  private key = (event: KeyboardEvent) => {
    const root = [...this.nodes.values()].find(
      (node) => !node.parent && node.events & 128,
    );
    if (!root) return;
    const front = [...this.nodes.values()]
      .filter((node) => node.kind === 1)
      .at(-1);
    if (event.key === 'Tab' && front) {
      const controls = Array.from(
        this.elements
          .get(front.id)!
          .querySelectorAll<HTMLElement>('button,input,select,[tabindex="0"]'),
      );
      const index = controls.indexOf(
        this.stage.ownerDocument.activeElement as HTMLElement,
      );
      const next =
        (index + (event.shiftKey ? -1 : 1) + controls.length) % controls.length;
      controls[next]?.focus();
      event.preventDefault();
    } else if (
      [
        'Escape',
        'ArrowUp',
        'ArrowDown',
        'Home',
        'End',
        'PageUp',
        'PageDown',
        'j',
        'J',
        'k',
        'K',
      ].includes(event.key)
    ) {
      if (
        /^[jk]$/i.test(event.key) &&
        (event.ctrlKey || event.altKey || event.metaKey)
      )
        return;
      const active = this.stage.ownerDocument.activeElement;
      if (
        event.key !== 'Escape' &&
        active &&
        ['INPUT', 'SELECT', 'TEXTAREA'].includes(active.tagName)
      )
        return;
      event.preventDefault();
      this.send(
        input(root.id, 128, [
          1,
          (
            {
              Escape: 1,
              ArrowUp: 7,
              ArrowDown: 8,
              Home: 12,
              End: 13,
              PageUp: 14,
              PageDown: 15,
              j: 16,
              J: 16,
              k: 17,
              K: 17,
            } as Record<string, number>
          )[event.key],
          1,
          this.modifiers(event),
          event.repeat ? 1 : 0,
        ]),
      );
    }
  };
  private pointer(node: UiNode, event: PointerEvent, phase: number) {
    if (
      phase !== 1 &&
      (this.capture?.id !== node.id || this.capture.pointer !== event.pointerId)
    )
      return;
    const bounds = this.stage.getBoundingClientRect();
    const x =
      ((event.clientX - bounds.left) * this.stage.offsetWidth) / bounds.width;
    const y =
      ((event.clientY - bounds.top) * this.stage.offsetHeight) / bounds.height;
    if (phase === 1) {
      if (event.button !== 0 && event.button !== 2) return;
      this.capture = {
        id: node.id,
        pointer: event.pointerId,
        x,
        y,
        button: event.button,
        modifiers: this.modifiers(event),
      };
      (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    }
    const bytes = input(node.id, 64, [
      1,
      phase,
      this.capture?.button ?? event.button,
      this.modifiers(event),
      ...new Array<number>(12).fill(0),
    ]);
    const view = new DataView(bytes.buffer);
    view.setInt32(20, event.pointerId, true);
    view.setInt32(24, Math.round(x * 64), true);
    view.setInt32(28, Math.round(y * 64), true);
    this.send(bytes);
    if (this.capture) {
      this.capture.x = x;
      this.capture.y = y;
    }
    if (phase >= 3) this.capture = null;
    event.preventDefault();
  }
  private cancel() {
    const capture = this.capture;
    if (!capture) return;
    this.capture = null;
    const bytes = input(capture.id, 64, [
      1,
      4,
      capture.button,
      capture.modifiers,
      ...new Array<number>(12).fill(0),
    ]);
    const view = new DataView(bytes.buffer);
    view.setInt32(20, capture.pointer, true);
    view.setInt32(24, Math.round(capture.x * 64), true);
    view.setInt32(28, Math.round(capture.y * 64), true);
    this.send(bytes);
  }
  private create(node: UiNode) {
    const document = this.stage.ownerDocument;
    const element = document.createElement(
      node.kind === 10
        ? 'button'
        : node.kind === 11
          ? 'input'
          : node.kind === 12
            ? 'select'
            : 'div',
    );
    element.dataset.handle = String(node.id);
    element.dataset.kind = String(node.kind);
    element.dataset.style = node.style;
    Object.assign(element.style, {
      position: 'absolute',
      boxSizing: 'border-box',
      padding: '0',
      margin: '0',
      pointerEvents:
        (node.kind === 8 && node.events === 0) ||
        (node.kind === 4 && node.events === 0)
          ? 'none'
          : 'auto',
    });
    this.theme.decorate(element, node, () => this.send(input(node.id, 2)));
    const options = { signal: this.subscriptions.signal };
    element.addEventListener(
      'focus',
      () => {
        if ((this.nodes.get(node.id)?.events ?? 0) & 32)
          this.send(input(node.id, 32));
      },
      options,
    );
    element.addEventListener(
      'wheel',
      (rawEvent) => {
        const event = rawEvent as WheelEvent;
        if (!((this.nodes.get(node.id)?.events ?? 0) & 512)) return;
        event.preventDefault();
        event.stopPropagation();
        const scale =
          event.deltaMode === 1
            ? 16
            : event.deltaMode === 2
              ? element.clientHeight
              : 1;
        const bytes = input(node.id, 512, new Array(12).fill(0));
        const view = new DataView(
          bytes.buffer,
          bytes.byteOffset,
          bytes.byteLength,
        );
        bytes[16] = 1;
        bytes[17] =
          (event.shiftKey ? 1 : 0) |
          (event.ctrlKey ? 2 : 0) |
          (event.altKey ? 4 : 0) |
          (event.metaKey ? 8 : 0);
        view.setInt32(
          20,
          Math.round(
            Math.max(-8192, Math.min(8192, event.deltaX * scale)) * 64,
          ),
          true,
        );
        view.setInt32(
          24,
          Math.round(
            Math.max(-8192, Math.min(8192, event.deltaY * scale)) * 64,
          ),
          true,
        );
        this.send(bytes);
      },
      { ...options, passive: false },
    );
    element.addEventListener(
      'contextmenu',
      (event) => {
        if ((this.nodes.get(node.id)?.events ?? 0) & 64) event.preventDefault();
      },
      options,
    );
    for (const [event, hovered] of [
      ['pointerenter', 1],
      ['pointerleave', 0],
    ] as const)
      element.addEventListener(
        event,
        () => {
          if ((this.nodes.get(node.id)?.events ?? 0) & 4)
            this.send(input(node.id, 4, [1, hovered]));
        },
        options,
      );
    if (node.kind === 10 || node.kind === 8 || node.kind === 9)
      element.addEventListener(
        'click',
        (event) => {
          if (!((this.nodes.get(node.id)?.events ?? 0) & 1)) return;
          event.stopPropagation();
          this.send(input(node.id, 1));
        },
        options,
      );
    if (node.kind === 8 || node.kind === 9)
      element.addEventListener(
        'keydown',
        (event) => {
          if (
            event.target !== element ||
            !((this.nodes.get(node.id)?.events ?? 0) & 1)
          )
            return;
          if (
            (event as KeyboardEvent).key !== 'Enter' &&
            (event as KeyboardEvent).key !== ' '
          )
            return;
          event.preventDefault();
          event.stopPropagation();
          if (!(event as KeyboardEvent).repeat) this.send(input(node.id, 1));
        },
        options,
      );
    element.addEventListener(
      'pointerdown',
      () => {
        if ((this.nodes.get(node.id)?.events ?? 0) & 32)
          this.send(input(node.id, 32));
      },
      options,
    );
    {
      element.style.touchAction = 'none';
      for (const [event, phase] of [
        ['pointerdown', 1],
        ['pointermove', 2],
        ['pointerup', 3],
        ['pointercancel', 4],
      ] as const)
        element.addEventListener(
          event,
          (event) => {
            if ((this.nodes.get(node.id)?.events ?? 0) & 64)
              this.pointer(node, event as PointerEvent, phase);
          },
          options,
        );
    }
    if (node.kind === 11 || node.kind === 12)
      element.addEventListener(
        node.kind === 11 ? 'input' : 'change',
        () => {
          const value = Number((element as HTMLInputElement).value);
          this.send(input(node.id, 256, [1, value & 255, value >> 8]));
        },
        options,
      );
    return element;
  }
  apply(nodes: ReadonlyMap<bigint, UiNode>) {
    if ([...nodes.values()].some((node) => node.kind === 13))
      throw Error('Live surface provider is not configured');
    const focusPath: bigint[] = [];
    for (
      let element = this.stage.ownerDocument
        .activeElement as HTMLElement | null;
      element && element !== this.stage;
      element = element.parentElement
    ) {
      const entry = [...this.elements].find(([, owned]) => owned === element);
      if (entry) focusPath.push(entry[0]);
    }
    const rectangles = layout(nodes, (style) =>
      this.theme.metrics(this.stage.ownerDocument, style),
    );
    const oldWindows = new Set(
      [...this.nodes.values()]
        .filter((node) => node.kind === 1)
        .map((node) => node.id),
    );
    for (const [id, element] of this.elements)
      if (!nodes.has(id)) {
        if (this.capture?.id === id) this.capture = null;
        element.remove();
        this.elements.delete(id);
      }
    this.nodes = nodes;
    function depth(node: UiNode): number {
      return node.parent ? depth(nodes.get(node.parent)!) + 1 : 0;
    }
    for (const node of [...nodes.values()].sort(
      (a, b) => depth(a) - depth(b),
    )) {
      const element = this.elements.get(node.id) ?? this.create(node);
      this.theme.update?.(element, node);
      this.elements.set(node.id, element);
      element.dataset.style = node.style;
      if (node.kind === 8 || node.kind === 9)
        element.tabIndex = node.events & 32 ? 0 : -1;
      element.style.pointerEvents =
        (node.kind === 8 && node.events === 0) ||
        (node.kind === 4 && node.events === 0)
          ? 'none'
          : 'auto';
      const parent = node.parent ? this.elements.get(node.parent)! : this.stage;
      if (element.parentElement !== parent) parent.append(element);
      const [x, y, width, height] = rectangles.get(node.id)!;
      Object.assign(element.style, {
        left: `${x}px`,
        top: `${y}px`,
        width: `${width}px`,
        height: `${height}px`,
        display: node.visible ? 'block' : 'none',
        filter: `brightness(${node.brightness / 10000})${element.dataset.themeFilter ?? ''}`,
      });
      if (node.kind === 3) element.textContent = node.text;
      if (node.kind === 11) {
        const range = element as HTMLInputElement;
        range.type = 'range';
        range.min = '0';
        range.max = '10000';
        range.step = node.text;
        range.value = String(node.value);
        range.style.setProperty('--value', `${node.value / 100}%`);
      }
      if (node.kind === 12) {
        const select = element as HTMLSelectElement;
        if (select.dataset.options !== node.text) {
          select.replaceChildren(
            ...node.text.split('\n').map((text, index) => {
              const option = select.ownerDocument.createElement('option');
              option.textContent = text;
              option.value = String(index);
              return option;
            }),
          );
          select.dataset.options = node.text;
        }
        select.value = String(node.value);
      }
      if (node.kind === 1)
        element.querySelector('[data-title]')!.textContent = node.text;
    }
    // Only move keyed elements whose actual sibling order changed; preserve focus.
    for (const parent of [this.stage, ...this.elements.values()]) {
      const children = [...nodes.values()].filter(
        (node) =>
          (node.parent ? this.elements.get(node.parent) : this.stage) ===
          parent,
      );
      let previous: HTMLElement | null = null;
      for (const child of children) {
        const element = this.elements.get(child.id)!;
        const next: Element | null = previous
          ? previous.nextElementSibling
          : null;
        if (previous && next !== element)
          parent.insertBefore(element, next ?? null);
        previous = element;
      }
    }
    const windows = [...nodes.values()].filter((node) => node.kind === 1);
    const fresh = windows.filter((node) => !oldWindows.has(node.id)).at(-1);
    if (
      !fresh &&
      this.stage.ownerDocument.activeElement === this.stage.ownerDocument.body
    ) {
      const front = windows.at(-1);
      const surviving = focusPath.find((id) => {
        const candidate = nodes.get(id);
        if (
          !candidate ||
          !(candidate.events & 32 || [10, 11, 12].includes(candidate.kind))
        )
          return false;
        let withinFront = !front;
        for (
          let node: UiNode | undefined = candidate;
          node;
          node = nodes.get(node.parent)
        ) {
          if (!node.visible) return false;
          if (node.id === front?.id) withinFront = true;
        }
        return withinFront;
      });
      if (surviving !== undefined) this.elements.get(surviving)?.focus();
    }
    if (fresh)
      this.elements
        .get(fresh.id)!
        .querySelector<HTMLElement>('button,input,select,[tabindex="0"]')
        ?.focus();
    if (oldWindows.size && !windows.length) this.restoreFocus();
    else if (
      windows.length &&
      this.stage.ownerDocument.activeElement === this.stage.ownerDocument.body
    )
      this.elements
        .get(windows.at(-1)!.id)!
        .querySelector<HTMLElement>('button,input,select,[tabindex="0"]')
        ?.focus();
  }
  dispose() {
    this.subscriptions.abort();
    this.capture = null;
    this.elements.clear();
    this.stage.replaceChildren();
    this.nodes = new Map();
  }
}
