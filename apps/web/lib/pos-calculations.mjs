export function roundMoney(value) {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

export function calculateTax(amountAfterDiscount, taxRate, taxBehavior) {
  if (amountAfterDiscount < 0) throw new Error("Amount cannot be negative.");
  if (taxRate < 0) throw new Error("Tax rate cannot be negative.");

  if (taxBehavior === "Inclusive") {
    const taxableAmount = roundMoney(amountAfterDiscount / (1 + taxRate));
    return {
      taxableAmount,
      taxAmount: roundMoney(amountAfterDiscount - taxableAmount)
    };
  }

  const taxAmount = roundMoney(amountAfterDiscount * taxRate);
  return {
    taxableAmount: roundMoney(amountAfterDiscount),
    taxAmount
  };
}

export function calculateCartTotals(lines) {
  return lines.reduce(
    (totals, line) => {
      const gross = roundMoney(line.price * line.quantity);
      const afterDiscount = roundMoney(gross - line.discount);
      const tax = calculateTax(afterDiscount, line.taxRate, line.taxBehavior);
      const lineTotal = line.taxBehavior === "Inclusive" ? afterDiscount : roundMoney(afterDiscount + tax.taxAmount);

      return {
        subtotal: roundMoney(totals.subtotal + gross),
        discountTotal: roundMoney(totals.discountTotal + line.discount),
        taxTotal: roundMoney(totals.taxTotal + tax.taxAmount),
        grandTotal: roundMoney(totals.grandTotal + lineTotal)
      };
    },
    { subtotal: 0, discountTotal: 0, taxTotal: 0, grandTotal: 0 }
  );
}

export function formatMoney(value) {
  return `Rs ${roundMoney(value).toFixed(2)}`;
}
