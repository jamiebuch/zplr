import type { BrowserContext } from "@playwright/test";

// The default e2e run exercises the application over the network. Service
// workers are disabled so that tests do not depend on the PWA shell. Leaving a
// large precaching service worker active also triggers a nondeterministic
// Firefox crash when a page is reloaded, so keeping it out of the general run
// keeps the suite stable. PWA-specific tests in pwa.spec.ts opt back in.
export async function disableServiceWorker(context: BrowserContext): Promise<void> {
  await context.addInitScript(() => {
    Object.defineProperty(navigator, "serviceWorker", {
      configurable: true,
      get: () => undefined,
    });
  });
}
