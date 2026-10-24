import { BrowserGeneration } from '@capsule-sdk/workshop/browser';
import { createBrowserTheme } from '@capsule-sdk/workshop/browser-theme';
import { parseTheme, type FontMetrics } from '@capsule-sdk/workshop/theme';
import { StarshipGame } from './mock-game';

const status = document.querySelector<HTMLOutputElement>('#status')!;
let active: BrowserGeneration | null = null;
let revision = 0;
const game = new StarshipGame(() => active?.enqueue(new Uint8Array()));

async function reload() {
  const current = ++revision;
  try {
    const response = await fetch('/portable-ui/package', { cache: 'no-store' });
    const data = (await response.json()) as {
      sdk: string;
      bundle: string;
      hash: string;
      theme: unknown;
      metrics: FontMetrics;
      error?: string;
    };
    if (!response.ok) throw Error(data.error ?? 'Package unavailable');
    const theme = createBrowserTheme(
      parseTheme(data.theme),
      data.metrics,
      (key) => '/portable-ui/art/' + data.hash + '/' + key,
    );
    const candidate = await BrowserGeneration.create(
      data.sdk,
      data.bundle,
      theme,
      game,
      () => document.body.focus(),
      (error) => {
        status.textContent = error.message;
      },
    );
    if (current !== revision) {
      candidate.dispose();
      return;
    }
    const previous = active;
    active = candidate;
    candidate.show();
    previous?.dispose();
    game.publish();
    status.textContent = 'Starship mock ready';
  } catch (error) {
    if (current === revision)
      status.textContent =
        error instanceof Error ? error.message : String(error);
  }
}

// Only the small HMR surface used here; no application dependency on Vite types.
const hot = (
  import.meta as ImportMeta & {
    hot?: {
      on(
        event: string,
        callback: (result: { success?: boolean; error?: string }) => void,
      ): void;
      dispose(callback: () => void): void;
    };
  }
).hot;
hot?.on('portable-ui:publication', (result) => {
  if (result.success) void reload();
  else if (result.error) status.textContent = result.error;
});
hot?.dispose(() => {
  revision++;
  active?.dispose();
});
await reload();
