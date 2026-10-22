import { expect, test } from "@playwright/test";

test("return replay, completed void, held void and drawer reconciliation preserve retail history", async ({ page, request }) => {
  test.setTimeout(150000);
  page.on("dialog", dialog => dialog.accept());
  const login = await request.post("http://localhost:5088/api/v1/auth/login", { data: { email: "admin@example.test", password: "ChangeMe123!" } });
  expect(login.ok()).toBeTruthy();
  const { accessToken } = await login.json();
  const headers = { Authorization: `Bearer ${accessToken}` };
  const api = "http://localhost:5088/api/v1";
  const boot = await (await request.get(`${api}/tenancy/bootstrap`, { headers })).json();
  const drawerPath = `${api}/cash-drawers/terminals/${boot.terminalId}`;
  const previous = await (await request.get(drawerPath, { headers })).json();
  for (const session of previous.filter((s: { closedAtUtc: string | null }) => !s.closedAtUtc)) {
    const closed = await request.post(`${api}/cash-drawers/${session.id}/close`, { headers, data: { clientGeneratedId: crypto.randomUUID(), countedCash: session.expectedCash, note: "E2E preflight" } });
    expect(closed.ok()).toBeTruthy();
  }
  const milk = (await (await request.get(`${api}/products?sku=MILK-1L`, { headers })).json()).items[0];
  const suffix = crypto.randomUUID().slice(0, 8);
  const productResponse = await request.post(`${api}/products`, { headers, data: { categoryId: milk.categoryId, taxCategoryId: milk.taxCategoryId, name: `Return item ${suffix}`, sku: `RET-${suffix}`, barcode: null, hsnSac: "0000", unitOfMeasure: "pc", price: 10, currency: "INR", isStockTracked: true } });
  expect(productResponse.ok()).toBeTruthy();
  const product = await productResponse.json();
  expect((await request.post(`${api}/inventory/opening-stock`, { headers, data: { locationId: boot.locationId, warehouseId: boot.warehouseId, productId: product.id, quantity: 5, unitCost: 5 } })).ok()).toBeTruthy();
  await page.goto("/login");
  await page.getByLabel("Email", { exact: true }).fill("admin@example.test");
  await page.getByLabel("Password", { exact: true }).fill("ChangeMe123!");
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page).toHaveURL(/\/pos$/);
  await page.goto("/cash-drawer");
  await page.getByLabel("Opening float", { exact: true }).fill("100");
  await page.getByRole("button", { name: "Open drawer", exact: true }).click();
  const reconciliation = page.getByRole("region", { name: "Drawer reconciliation", exact: true });
  await expect(reconciliation).toBeVisible();
  await page.getByLabel("Movement amount", { exact: true }).fill("5");
  await page.getByLabel("Movement reason", { exact: true }).fill("E2E float top-up");
  await page.getByRole("button", { name: "Record movement", exact: true }).click();
  await expect(reconciliation.getByRole("cell", { name: "PaidIn", exact: true })).toBeVisible();

  const makeSale = async (quantity: number) => {
    const response = await request.post(`${api}/sales/complete`, { headers, data: { locationId: boot.locationId, terminalId: boot.terminalId, customerId: null, clientGeneratedId: crypto.randomUUID(), lines: [{ productId: product.id, quantity, discountAmount: 0 }], payments: [{ method: "Cash", amount: quantity * 10, amountTendered: quantity * 10 + 20 }] } });
    expect(response.ok()).toBeTruthy(); return response.json();
  };
  const sale = await makeSale(2);
  await page.goto("/");
  await page.getByLabel("Invoice search").fill(sale.invoiceNumber);
  await page.getByRole("button", { name: "Search", exact: true }).click();
  await page.getByRole("button", { name: `View receipt ${sale.invoiceNumber}`, exact: true }).click();
  const corrections = page.getByRole("region", { name: "Sale corrections", exact: true });
  await corrections.getByLabel("Reason", { exact: true }).fill("Customer returned one item");
  await corrections.getByLabel(`Return quantity ${product.name}`, { exact: true }).fill("1");
  await corrections.getByRole("button", { name: "Calculate reversal", exact: true }).click();
  await expect(corrections.getByLabel("Refund Cash", { exact: true })).toHaveValue("10");
  let lost = false;
  let originalId = "";
  let replayId = "";
  await page.route(`**/api/v1/sales/${sale.id}/returns`, async route => {
    const command = route.request().postDataJSON();
    if (!lost) {
      originalId = command.clientGeneratedId;
      const accepted = await route.fetch(); expect(accepted.ok()).toBeTruthy();
      lost = true;
      await route.abort("failed");
    } else { replayId = command.clientGeneratedId; await route.continue(); }
  });
  await corrections.getByRole("button", { name: "Record return", exact: true }).click();
  await expect(corrections.getByRole("button", { name: "Retry pending command", exact: true })).toBeEnabled();
  expect(lost).toBeTruthy();
  await page.reload();
  await page.getByLabel("Invoice search").fill(sale.invoiceNumber);
  await page.getByRole("button", { name: "Search", exact: true }).click();
  await page.getByRole("button", { name: `View receipt ${sale.invoiceNumber}`, exact: true }).click();
  await corrections.getByRole("button", { name: "Retry pending command", exact: true }).click();
  await expect(page.getByRole("region", { name: "Correction receipt", exact: true })).toBeVisible();
  expect(replayId).toBe(originalId);
  const history = await (await request.get(`${api}/sales/${sale.id}/corrections`, { headers })).json();
  expect(history.corrections).toHaveLength(1);
  expect(history.effectiveStatus).toBe("PartiallyReturned");
  await page.evaluate(() => { document.body.dataset.printTarget = "correction"; });
  await page.pdf({ path: "test-results/return-receipt.pdf", preferCSSPageSize: true });
  await page.evaluate(() => { delete document.body.dataset.printTarget; });
  for (const width of [1366, 768, 390]) {
    await page.setViewportSize({ width, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
    await page.screenshot({ path: `test-results/returns-${width}.png`, fullPage: true });
  }
  const second = await makeSale(1);
  await page.setViewportSize({ width: 1366, height: 900 });
  await page.goto("/");
  await page.getByLabel("Invoice search").fill(second.invoiceNumber);
  await page.getByRole("button", { name: "Search", exact: true }).click();
  await page.getByRole("button", { name: `View receipt ${second.invoiceNumber}`, exact: true }).click();
  await corrections.getByRole("combobox", { name: "Operation", exact: true }).selectOption("void");
  await corrections.getByLabel("Reason", { exact: true }).fill("Full sale cancelled");
  await corrections.getByRole("button", { name: "Calculate reversal", exact: true }).click();
  await corrections.getByRole("button", { name: "Void sale", exact: true }).click();
  await expect(page.getByRole("region", { name: "Correction receipt", exact: true })).toContainText("Void");
  const heldResponse = await request.post(`${api}/sales/hold`, { headers, data: { locationId: boot.locationId, terminalId: boot.terminalId, clientGeneratedId: crypto.randomUUID(), lines: [{ productId: product.id, quantity: 1, discountAmount: 0 }] } });
  expect(heldResponse.ok()).toBeTruthy(); const held = await heldResponse.json();
  await page.goto("/pos");
  const heldRow = page.locator(".mini-sale").filter({ hasText: held.invoiceNumber });
  await heldRow.getByRole("button", { name: "Void", exact: true }).click();
  await corrections.getByLabel("Reason", { exact: true }).fill("Abandoned cart");
  await corrections.getByRole("button", { name: "Calculate reversal", exact: true }).click();
  await corrections.getByRole("button", { name: "Void sale", exact: true }).click();
  await expect(heldRow).toHaveCount(0);
  await expect(page.getByRole("region", { name: "Correction receipt", exact: true })).toContainText("OpenVoid");
  await page.goto("/cash-drawer");
  await expect(reconciliation).toBeVisible();
  await expect(reconciliation.locator(".metric").filter({ hasText: "Expected cash" })).toContainText("115.00");
  await page.getByLabel("Counted cash", { exact: true }).fill("114");
  await page.getByLabel("Close note", { exact: true }).fill("E2E one rupee variance");
  await page.getByRole("button", { name: "Close drawer", exact: true }).click();
  await expect(reconciliation.locator(".metric").filter({ hasText: "Status" })).toContainText("Closed");
  await expect(reconciliation.locator(".metric").filter({ hasText: "Variance" })).toContainText("-1.00");
  for (const width of [1366, 768, 390]) {
    await page.setViewportSize({ width, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
    await page.screenshot({ path: `test-results/drawer-${width}.png`, fullPage: true });
  }
  const balances = await (await request.get(`${api}/inventory/balances`, { headers })).json();
  expect(balances.find((b: { productId: string }) => b.productId === product.id).quantityOnHand).toBe(4);
});
