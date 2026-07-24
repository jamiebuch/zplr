import { devices, expect, test } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    window.localStorage.clear();
    window.sessionStorage.clear();
  });
});

test("browses and filters the complete command catalog", async ({ page }) => {
  const browserErrors: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error") browserErrors.push(message.text());
  });
  page.on("pageerror", (error) => browserErrors.push(error.message));

  await page.goto("/zpl-commands");
  await expect(page.getByRole("heading", { name: "Every ZPL command, explained and ready to render." })).toBeVisible();
  await expect(page.locator(".command-card")).toHaveCount(60);
  await expect(page.getByText("1,457", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Show all 223 commands" }).click();
  await expect(page.locator(".command-card")).toHaveCount(223);

  const search = page.getByRole("searchbox", { name: "Search commands and parameters" });
  await search.fill("field origin");
  await expect(page.locator(".command-card")).toHaveCount(2);
  await expect(page.locator(".command-card").getByText("^FO", { exact: true })).toBeVisible();

  await search.fill("");
  await page.getByLabel("Category").selectOption("barcode");
  await expect(page.locator(".command-card")).toHaveCount(31);
  await page.getByRole("button", { name: "Clear filters" }).click();
  await expect(page.locator(".command-card")).toHaveCount(60);
  expect(browserErrors).toEqual([]);
});

test("renders visual examples lazily and keeps device commands code-only", async ({ page }) => {
  await page.goto("/zpl-commands");
  await expect(page.getByRole("heading", { name: "Every ZPL command, explained and ready to render." })).toBeVisible();
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  await page.goto("/zpl-commands/caret-fo");
  await expect(page.getByRole("heading", { name: "Field Origin" })).toBeVisible();
  await expect(page.getByText("^FOx,y,z", { exact: true })).toBeVisible();
  await page.locator(".example-card").first().scrollIntoViewIfNeeded();
  const firstPreview = page.getByAltText(/Rendered label for \^FO/).first();
  await expect(firstPreview).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole("link", { name: /Edit in editor/ }).first()).toHaveAttribute(
    "href",
    /\/editor\?example=caret-fo-/,
  );

  await page.goto("/zpl-commands/caret-rw");
  await expect(page.getByRole("heading", { name: "Set RF Power Levels for Read and Write" })).toBeVisible();
  await expect(page.getByText(/These are code examples only/)).toBeVisible();
  await expect(page.getByText("Renderer", { exact: true })).toHaveCount(0);

  await page.goto("/zpl-commands/caret-jm");
  await expect(page.getByText(/recognized but not implemented/)).toBeVisible();
  await expect(page.getByText("Renderer", { exact: true })).toHaveCount(0);
});

test("opens a documentation example in a new editor workspace tab", async ({ page }) => {
  await page.goto("/editor");
  await expect(page.getByTestId("zpl-editor")).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText("shipping-label.zpl", { exact: true }).first()).toBeVisible();

  await page.goto("/zpl-commands/caret-fo");
  await page.getByRole("link", { name: /Edit in editor/ }).first().click();
  await expect(page).toHaveURL(/\/editor$/);
  await expect(page.getByText(/\^FO example opened as caret-fo-x-1\.zpl/)).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText("shipping-label.zpl", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("caret-fo-x-1.zpl", { exact: true }).first()).toBeVisible();
  await expect(page.locator(".editor-tab")).toHaveCount(2);
});

test("keeps the command index accessible", async ({ page }) => {
  await page.goto("/zpl-commands");
  const results = await new AxeBuilder({ page }).analyze();
  expect(results.violations).toEqual([]);
});

test("keeps command documentation usable on a narrow dark viewport", async ({ browser }) => {
  const consoleErrors: string[] = [];
  const context = await browser.newContext({
    ...devices["Pixel 5"],
    baseURL: test.info().project.use.baseURL as string,
    colorScheme: "dark",
    locale: "de-DE",
  });
  const page = await context.newPage();
  page.on("console", (message) => {
    if (message.type() === "error" || message.type() === "warning") {
      consoleErrors.push(message.text());
    }
  });
  page.on("pageerror", (error) => consoleErrors.push(error.message));

  await page.goto("/zpl-commands/caret-fo");
  await expect(page.getByRole("heading", { name: "Field Origin" })).toBeVisible();
  await page.locator(".example-card").first().scrollIntoViewIfNeeded();
  await expect(page.getByAltText(/Rendered label for \^FO/).first()).toBeVisible({ timeout: 30_000 });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(consoleErrors).toEqual([]);
  await context.close();
});
