export function roundMoney(value: number): number;
export function calculateTax(amountAfterDiscount: number, taxRate: number, taxBehavior: "Inclusive" | "Exclusive"): {
  taxableAmount: number;
  taxAmount: number;
};
export function calculateCartTotals(lines: Array<{
  price: number;
  quantity: number;
  discount: number;
  taxRate: number;
  taxBehavior: "Inclusive" | "Exclusive";
}>): {
  subtotal: number;
  discountTotal: number;
  taxTotal: number;
  grandTotal: number;
};
export function formatMoney(value: number): string;
