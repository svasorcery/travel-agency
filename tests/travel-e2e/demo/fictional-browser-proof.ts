import type { Page, Request } from '@playwright/test';
import { fetchFictionalApi } from './fictional-route-guard';

/** Wait for both route delivery/cancellation and the browser's request terminal event. */
export async function delayFictionalRead(page: Page, id: string) {
  let release!: () => void;
  const gate = new Promise<void>((resolve) => (release = resolve));
  let started!: () => void;
  const incoming = new Promise<void>((resolve) => (started = resolve));
  let delivered!: () => void;
  const handlerDone = new Promise<void>((resolve) => (delivered = resolve));
  let terminated!: () => void;
  const networkDone = new Promise<void>((resolve) => (terminated = resolve));
  let observed: Request | null = null;
  const terminal = (request: Request) => {
    if (request === observed) terminated();
  };
  page.on('requestfinished', terminal);
  page.on('requestfailed', terminal);
  await page.route(`**/api/flights/travelers/${id}`, async (route) => {
    observed = route.request();
    const response = await fetchFictionalApi(route);
    started();
    await gate;
    try {
      await route.fulfill({ response });
    } finally {
      delivered();
    }
  });
  return {
    incoming,
    release,
    async settled() {
      await Promise.all([handlerDone, networkDone]);
      page.off('requestfinished', terminal);
      page.off('requestfailed', terminal);
    },
  };
}

/** Observe the existing async component callback without adding production code or retaining fields. */
export async function observeClientCompletion(page: Page, selector: string, method: 'fillTraveler' | 'edit') {
  await page.evaluate(
    ({ selector, method }) => {
      const element = document.querySelector(selector);
      const browser = window as unknown as {
        ng: { getComponent(element: Element): Record<string, (...args: unknown[]) => unknown> };
        __fictionalCallbackDone?: Promise<void>;
      };
      if (element === null || browser.ng === undefined) throw new Error('Fictional development component unavailable');
      const component = browser.ng.getComponent(element);
      const original = component[method];
      if (typeof original !== 'function') throw new Error('Fictional callback unavailable');
      let complete!: () => void;
      browser.__fictionalCallbackDone = new Promise<void>((resolve) => (complete = resolve));
      component[method] = function (...args: unknown[]) {
        const result = original.apply(this, args);
        void Promise.resolve(result).then(complete, complete);
        return result;
      };
    },
    { selector, method },
  );
  return async () => {
    await page.evaluate(async () => {
      const browser = window as unknown as { __fictionalCallbackDone?: Promise<void> };
      if (browser.__fictionalCallbackDone === undefined) throw new Error('Fictional callback observer unavailable');
      await browser.__fictionalCallbackDone;
      // A rendering checkpoint follows the completed real callback, not a fixed time delay.
      await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
      delete browser.__fictionalCallbackDone;
    });
  };
}
