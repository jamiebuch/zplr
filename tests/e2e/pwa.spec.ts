import { expect, test } from "@playwright/test";

// These tests exercise the PWA shell itself, so the service worker is left
// enabled here (unlike the rest of the suite, which disables it via
// helpers.disableServiceWorker).

test("offers an installable PWA with a valid web manifest", async ({ page }) => {
  await page.goto("/editor");
  await expect(page.locator('link[rel="manifest"]')).toHaveAttribute("href", "/manifest.webmanifest");

  const response = await page.request.get("/manifest.webmanifest");
  expect(response.status()).toBe(200);
  expect(response.headers()["content-type"]).toContain("application/manifest+json");
  const manifest = JSON.parse(await response.text()) as {
    start_url: string;
    scope: string;
    display: string;
    icons: { src: string; sizes: string; purpose: string }[];
  };
  expect(manifest.start_url).toBe("/editor");
  expect(manifest.scope).toBe("/");
  expect(manifest.display).toBe("standalone");
  expect(manifest.icons).toEqual(expect.arrayContaining([
    expect.objectContaining({ sizes: "192x192", purpose: "any" }),
    expect.objectContaining({ sizes: "512x512", purpose: "any" }),
    expect.objectContaining({ sizes: "512x512", purpose: "maskable" }),
  ]));
  for (const icon of manifest.icons) {
    const iconResponse = await page.request.get(icon.src);
    expect(iconResponse.status(), `manifest icon ${icon.src} is missing`).toBe(200);
  }
});

test("registers a service worker and serves the editor offline", async ({ page, context, browserName }) => {
  test.slow();
  await page.goto("/editor");
  await expect(page.getByTestId("zpl-editor")).toBeVisible({ timeout: 30_000 });
  await page.waitForFunction(
    async () => {
      const registration = await navigator.serviceWorker.getRegistration();
      return registration?.active?.state === "activated";
    },
    undefined,
    { timeout: 60_000, polling: 250 },
  );

  // Navigate again so the document is loaded under the active service
  // worker's control. clients.claim() does not reliably set the controller
  // for the already-loaded first-load document on WebKit, so a fresh
  // navigation guarantees navigator.serviceWorker.controller is set before
  // going offline. A goto (not reload) is used because reload with an active
  // service worker nondeterministically crashes headless Firefox.
  await page.goto("/editor");
  await expect(page.getByTestId("zpl-editor")).toBeVisible({ timeout: 30_000 });
  await page.waitForFunction(
    () => navigator.serviceWorker.controller !== null,
    undefined,
    { timeout: 30_000, polling: 250 },
  );

  // Playwright's offline emulation cannot commit a navigation on WebKit:
  // goto and reload both throw "WebKit encountered an internal error" even
  // on pages with no service worker, so offline serving is only asserted
  // on Chromium and Firefox.
  if (browserName === "webkit") return;

  await context.setOffline(true);

  // This page was never loaded before going offline, so it can only be
  // served from the service worker's precache.
  await page.goto("/zpl-commands/caret-a");
  await expect(page.getByRole("heading", { name: "Scalable/Bitmapped Font" })).toBeVisible({ timeout: 30_000 });

  await page.goto("/editor");
  await expect(page.getByTestId("zpl-editor")).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole("button", { name: "Add box", exact: true })).toBeVisible();
});
