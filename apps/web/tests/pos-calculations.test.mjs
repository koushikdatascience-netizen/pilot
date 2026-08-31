import assert from "node:assert/strict";
import { calculateCartTotals, calculateTax, formatMoney } from "../lib/pos-calculations.mjs";

run("inclusive GST extracts tax from the displayed price", () => {
  assert.deepEqual(calculateTax(118, 0.18, "Inclusive"), {
    taxableAmount: 100,
    taxAmount: 18
  });
});

run("cart totals keep subtotal, discount, tax and grand total distinct", () => {
  const totals = calculateCartTotals([
    { price: 68, quantity: 2, discount: 6, taxRate: 0.05, taxBehavior: "Inclusive" },
    { price: 799, quantity: 1, discount: 0, taxRate: 0.12, taxBehavior: "Inclusive" }
  ]);

  assert.equal(totals.subtotal, 935);
  assert.equal(totals.discountTotal, 6);
  assert.equal(totals.taxTotal, 91.8);
  assert.equal(totals.grandTotal, 929);
});

run("money display uses stable two-decimal INR-compatible formatting", () => {
  assert.equal(formatMoney(12.5), "Rs 12.50");
});

function run(name, fn) {
  try {
    fn();
    console.log(`PASS ${name}`);
  } catch (error) {
    console.error(`FAIL ${name}`);
    throw error;
  }
}
