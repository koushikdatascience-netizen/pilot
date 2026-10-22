"use client";

import { Minus, Plus, Printer, Trash2 } from "lucide-react";
import { useState } from "react";
import { ProductResponse, SaleResponse } from "../lib/api";
import { formatMoney } from "../lib/pos-calculations.mjs";
import { printReceipt } from "../lib/print-receipt";

export type CartLine = ProductResponse & { quantity: number; discount: number; taxRate: number; taxBehavior: "Inclusive" | "Exclusive" };
type Tender = { method: string; amount: string; tendered: string };

export function Cart({ lines, disabled, onChange }: { lines: CartLine[]; disabled: boolean; onChange: (lines: CartLine[]) => void }) {
  function update(id: string, change: Partial<CartLine>) {
    onChange(lines.map(line => {
      if (line.id !== id) return line;
      const next = { ...line, ...change };
      return { ...next, discount: Math.max(0, Math.min(next.discount, Math.round(next.price * next.quantity * 100) / 100)) };
    }));
  }
  return <div className="cart-lines">{lines.length === 0 ? <div className="empty-state">Ready for scanning</div> : lines.map(l => <div className="cart-line" key={l.id}>
    <div><strong>{l.name}</strong><span>{l.sku}</span><label>Discount<input aria-label={`Discount ${l.name}`} type="number" min="0" max={l.price * l.quantity} step="0.01" value={l.discount} disabled={disabled} onChange={e => update(l.id, { discount: Number(e.target.value) })}/></label></div>
    <div className="qty-stepper"><button disabled={disabled || l.quantity <= 1} title="Decrease quantity" aria-label={`Decrease ${l.name}`} onClick={() => update(l.id, { quantity: l.quantity - 1 })}><Minus size={16}/></button><span>{l.quantity}</span><button disabled={disabled} title="Increase quantity" aria-label={`Increase ${l.name}`} onClick={() => update(l.id, { quantity: l.quantity + 1 })}><Plus size={16}/></button></div>
    <span>{formatMoney(l.price * l.quantity - l.discount)}</span><button disabled={disabled} className="ghost-danger" title="Remove item" aria-label={`Remove ${l.name}`} onClick={() => onChange(lines.filter(x => x.id !== l.id))}><Trash2 size={16}/></button>
  </div>)}</div>;
}

export function PaymentPanel({ payments, total, disabled, onChange }: { payments: Tender[]; total: number; disabled: boolean; onChange: (payments: Tender[]) => void }) {
  function update(index: number, patch: Partial<Tender>) { onChange(payments.map((p, i) => i === index ? { ...p, ...patch } : p)); }
  return <fieldset disabled={disabled} className="payment-panel"><legend>Payment</legend>{payments.map((p, i) => <div className="payment-row" key={i}>
    <select aria-label={`Payment method ${i + 1}`} value={p.method} onChange={e => update(i, { method: e.target.value, tendered: "" })}>{["Cash", "Upi", "Card", "Wallet", "Credit"].map(m => <option key={m}>{m}</option>)}</select>
    <label>Amount<input aria-label={`Payment amount ${i + 1}`} type="number" min="0.01" step="0.01" placeholder={String(total)} value={p.amount} onChange={e => update(i, { amount: e.target.value })}/></label>
    {p.method === "Cash" && <label>Tendered<input aria-label={`Tendered ${i + 1}`} type="number" min="0" step="0.01" value={p.tendered} placeholder={p.amount || String(total)} onChange={e => update(i, { tendered: e.target.value })}/></label>}
    {payments.length > 1 && <button className="icon-button" title="Remove payment" aria-label="Remove payment" onClick={() => onChange(payments.filter((_, n) => n !== i))}><Trash2 size={16}/></button>}
  </div>)}<button className="secondary-link" onClick={() => {
    const allocated = payments.map(p => ({ ...p, amount: p.amount || String(total) }));
    const remaining = Math.max(0, Math.round((total - allocated.reduce((sum, p) => sum + Number(p.amount), 0)) * 100) / 100);
    onChange([...allocated, { method: "Upi", amount: String(remaining), tendered: "" }]);
  }}><Plus size={16}/>Split payment</button>
    <div className="summary-row"><span>Change</span><strong>{formatMoney(payments.reduce((sum, p) => sum + (p.method === "Cash" ? Math.max(0, Number(p.tendered || p.amount || total) - Number(p.amount || total)) : 0), 0))}</strong></div>
  </fieldset>;
}

export function ReceiptView({ sale }: { sale: SaleResponse }) {
  const [paper, setPaper] = useState("80");
  return <section className={`receipt paper-${paper}`} aria-label="Receipt"><style media="print">{`@page { size: ${paper}mm 297mm; margin: 0; }`}</style><div className="panel-title"><strong>Pilot POS</strong><select aria-label="Receipt paper width" value={paper} onChange={e => setPaper(e.target.value)}><option value="58">58mm</option><option value="80">80mm</option></select><button className="icon-button" title="Print receipt" aria-label="Print receipt" onClick={() => printReceipt("sale")}><Printer size={16}/></button></div>
    {sale.receipt && <div><strong>{sale.receipt.legalName}</strong><span> {sale.receipt.businessName} | {sale.receipt.locationName}</span><small>{sale.receipt.address} | GSTIN {sale.receipt.gstin}</small><small>{sale.receipt.terminalName} | {sale.receipt.cashierName} | {sale.receipt.customerName}</small></div>}
    <span>{sale.invoiceNumber}</span><time>{sale.completedAtUtc ? new Date(sale.completedAtUtc).toLocaleString() : ""}</time>
    {sale.lines.map(l => <div key={l.id}><strong>{l.productName}</strong><span> {l.quantity} x {formatMoney(l.unitPrice)} = {formatMoney(l.lineTotal)}</span><small> Discount {formatMoney(l.discountAmount)} | Taxable {formatMoney(l.taxableAmount)} | CGST {formatMoney(l.cgstAmount)} | SGST {formatMoney(l.sgstAmount)} | IGST {formatMoney(l.igstAmount)}</small></div>)}
    {sale.lines.filter(l => l.hsnSac).map(l => <small key={l.id}>{l.productName} | HSN/SAC {l.hsnSac}</small>)}
    <strong>Total {formatMoney(sale.grandTotal)}</strong><span>GST {formatMoney(sale.taxTotal)}</span>{sale.payments.map(p => <span key={p.id}>{p.method} {formatMoney(p.amount)} | Tendered {formatMoney(p.amountTendered)} | Change {formatMoney(p.changeDue)}</span>)}
  </section>;
}
