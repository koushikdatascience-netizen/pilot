import { expect, test, Page } from "@playwright/test";

async function signIn(page: Page, email: string, password: string) {
  await page.goto("/login");
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByLabel("Password", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page).toHaveURL(/\/pos$/);
}

test("administration persists users and roles, reviews audit and immediately revokes access", async ({ page, browser }) => {
  test.setTimeout(120000);
  const suffix = crypto.randomUUID().slice(0, 8);
  const roleName = `Audit reviewer ${suffix}`;
  const userName = `Reviewer ${suffix}`;
  const email = `reviewer-${suffix}@example.test`;
  const password = "E2e-reviewer-password-123!";
  await signIn(page, "admin@example.test", "ChangeMe123!");
  await page.goto("/administration");
  await page.getByRole("tab", { name: "Roles", exact: true }).click();
  await page.getByRole("button", { name: "New role", exact: true }).click();
  let dialog = page.getByRole("dialog");
  await dialog.getByLabel("Name", { exact: true }).fill(roleName);
  await dialog.getByRole("checkbox", { name: "ViewAudit", exact: true }).check();
  await dialog.getByRole("button", { name: "Save", exact: true }).click();
  await expect(dialog).not.toBeVisible();
  await expect(page.getByRole("cell", { name: roleName, exact: true })).toBeVisible({ timeout: 15000 });
  await page.getByRole("tab", { name: "Users", exact: true }).click();
  await page.getByRole("button", { name: "New user", exact: true }).click();
  dialog = page.getByRole("dialog");
  await dialog.getByLabel("Name", { exact: true }).fill(userName);
  await dialog.getByLabel("Email", { exact: true }).fill(email);
  await dialog.getByLabel("Initial password", { exact: true }).fill(password);
  await dialog.getByRole("checkbox", { name: roleName, exact: true }).check();
  await dialog.getByRole("button", { name: "Save", exact: true }).click();
  await expect(dialog).not.toBeVisible();
  await page.getByLabel("Search users").fill(email);
  await page.getByRole("button", { name: "Apply filters", exact: true }).click();
  await expect(page.getByRole("cell", { name: email, exact: true })).toBeVisible();
  await page.getByRole("button", { name: `Edit ${userName}`, exact: true }).click();
  dialog = page.getByRole("dialog");
  await dialog.getByLabel("Name", { exact: true }).fill(`${userName} revised`);
  await dialog.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.getByRole("cell", { name: `${userName} revised`, exact: true })).toBeVisible();
  await page.screenshot({ path: "test-results/administration-desktop.png", fullPage: true });
  await page.getByRole("tab", { name: "Audit", exact: true }).click();
  await page.getByLabel("Action", { exact: true }).fill("UserCreated");
  await page.getByLabel("Entity", { exact: true }).fill("User");
  const filteredAudit = page.waitForResponse(response => response.url().includes("/api/v1/audit?") && new URL(response.url()).searchParams.get("action") === "UserCreated");
  await page.getByRole("button", { name: "Apply filters", exact: true }).click();
  await filteredAudit;
  await expect(page.getByRole("tabpanel")).toHaveAttribute("aria-busy", "false");
  await expect(page.getByRole("cell", { name: "UserCreated", exact: true }).first()).toBeVisible();
  await page.getByRole("button", { name: /^Review UserCreated/ }).first().click();
  await expect(page.getByRole("dialog")).toContainText(email);
  await page.screenshot({ path: "test-results/audit-details-desktop.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 900 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  await page.screenshot({ path: "test-results/audit-details-mobile.png", fullPage: true });
  await page.getByRole("button", { name: "Close audit event", exact: true }).click();
  for (const width of [1366, 768, 390]) {
    await page.setViewportSize({ width, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
    await page.screenshot({ path: `test-results/audit-${width}.png`, fullPage: true });
  }

  const colleagueContext = await browser.newContext();
  const colleague = await colleagueContext.newPage();
  try {
    await signIn(colleague, email, password);
    await colleague.goto("/administration");
    await expect(colleague.getByRole("tab", { name: "Audit", exact: true })).toBeVisible();
    await expect(colleague.getByRole("tab", { name: "Users", exact: true })).toHaveCount(0);
    await expect(colleague.getByRole("cell", { name: "UserCreated", exact: true }).first()).toBeVisible();
    await page.getByRole("tab", { name: "Roles", exact: true }).click();
    await page.getByRole("button", { name: `Edit ${roleName}`, exact: true }).click();
    dialog = page.getByRole("dialog");
    await dialog.getByRole("checkbox", { name: "ViewAudit", exact: true }).uncheck();
    await dialog.getByRole("button", { name: "Save", exact: true }).click();
    await expect(dialog).not.toBeVisible();
    await colleague.reload();
    await expect(colleague.getByRole("alert").filter({ hasText: "Access denied" })).toBeVisible();
    const token = await colleague.evaluate(() => JSON.parse(sessionStorage.getItem("pilot-session")!).accessToken);
    const denied = await colleague.request.get("/api/v1/audit", { headers: { Authorization: `Bearer ${token}` } });
    expect(denied.status()).toBe(403);
  } finally { await colleagueContext.close(); }
});

test("cashier cannot open administration or audit APIs", async ({ page }) => {
  await signIn(page, "cashier@example.test", "ChangeMe123!");
  await page.goto("/administration");
  await expect(page.getByRole("alert").filter({ hasText: "Access denied" })).toBeVisible();
  await expect(page.getByRole("button", { name: "New user", exact: true })).toHaveCount(0);
  const token = await page.evaluate(() => JSON.parse(sessionStorage.getItem("pilot-session")!).accessToken);
  for (const path of ["/api/v1/administration/users", "/api/v1/administration/roles", "/api/v1/audit"]) {
    const response = await page.request.get(path, { headers: { Authorization: `Bearer ${token}` } });
    expect(response.status()).toBe(403);
  }
});
