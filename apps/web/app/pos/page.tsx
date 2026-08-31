"use client";

import { CreditCard, Minus, Plus, ReceiptText, Search, Trash2, UserRound, Wallet } from "lucide-react";
import { FormEvent, useMemo, useState } from "react";
import { calculateCartTotals, formatMoney } from "../../lib/pos-calculations.mjs";

type Product = {
  id: string;
  name: string;
  sku: string;
  barcode: string;
  price: number;
  taxRate: number;
  taxBehavior: "Inclusive" | "Exclusive";
  stock: number;
};

type CartLine = Product & { quantity: number; discount: number };

const products: Product[] = [
  { id: "rice", name: "Rice 5kg", sku: "RICE-5KG", barcode: "8901000000011", price: 349, taxRate: 0.05, taxBehavior: "Inclusive", stock: 50 },
  { id: "milk", name: "Milk 1L", sku: "MILK-1L", barcode: "8901000000028", price: 68, taxRate: 0.05, taxBehavior: "Inclusive", stock: 80 },
  { id: "shirt", name: "Premium T-Shirt", sku: "TSHIRT-PREM", barcode: "8901000000035", price: 799, taxRate: 0.12, taxBehavior: "Inclusive", stock: 25 },
  { id: "shampoo", name: "Shampoo 180ml", sku: "SHAMPOO-180", barcode: "8901000000042", price: 185, taxRate: 0.18, taxBehavior: "Inclusive", stock: 50 }
];

export default function PosPage() {
  const [query, setQuery] = useState("");
  const [cart, setCart] = useState<CartLine[]>([]);
  const [paymentMethod, setPaymentMethod] = useState("Cash");
  const [receipt, setReceipt] = useState<string | null>(null);

  const visibleProducts = products.filter((product) => {
    const needle = query.trim().toLowerCase();
    return !needle || product.name.toLowerCase().includes(needle) || product.sku.toLowerCase().includes(needle) || product.barcode.includes(needle);
  });

  const totals = useMemo(() => calculateCartTotals(cart), [cart]);

  function addProduct(product: Product) {
    setCart((current) => {
      const existing = current.find((line) => line.id === product.id);
      if (existing) {
        return current.map((line) => (line.id === product.id ? { ...line, quantity: line.quantity + 1 } : line));
      }

      return [...current, { ...product, quantity: 1, discount: 0 }];
    });
  }

  function submitSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const exact = products.find((product) => product.barcode === query.trim() || product.sku.toLowerCase() === query.trim().toLowerCase());
    if (exact) {
      addProduct(exact);
      setQuery("");
    }
  }

  function updateQuantity(id: string, delta: number) {
    setCart((current) =>
      current
        .map((line) => (line.id === id ? { ...line, quantity: Math.max(1, line.quantity + delta) } : line))
        .filter((line) => line.quantity > 0)
    );
  }

  function completeSale() {
    if (cart.length === 0) return;
    setReceipt(`KAL-${new Date().toISOString().slice(0, 10).replaceAll("-", "")}-${String(Date.now()).slice(-4)}`);
  }

  return (
    <main className="pos">
      <section className="pos-left">
        <header className="pos-header">
          <div>
            <p className="eyebrow">Retail POS</p>
            <h1>Kalyani Main Store</h1>
          </div>
          <a className="secondary-link" href="/">Back office</a>
        </header>

        <form className="scan-box" onSubmit={submitSearch}>
          <Search size={20} />
          <input
            autoFocus
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Scan barcode or search product"
            aria-label="Scan barcode or search product"
          />
        </form>

        <div className="quick-grid">
          {visibleProducts.map((product) => (
            <button className="product-tile" key={product.id} onClick={() => addProduct(product)}>
              <span>{product.name}</span>
              <small>{product.sku}</small>
              <strong>{formatMoney(product.price)}</strong>
            </button>
          ))}
        </div>
      </section>

      <aside className="cart-panel">
        <div className="panel-title">
          <h2>Current Cart</h2>
          <button className="icon-button" aria-label="Select customer" title="Select customer">
            <UserRound size={18} />
          </button>
        </div>

        <div className="cart-lines">
          {cart.length === 0 ? (
            <div className="empty-state">Ready for scanning</div>
          ) : (
            cart.map((line) => (
              <div className="cart-line" key={line.id}>
                <div>
                  <strong>{line.name}</strong>
                  <span>{line.sku}</span>
                </div>
                <div className="qty-stepper">
                  <button onClick={() => updateQuantity(line.id, -1)} aria-label={`Decrease ${line.name}`} title="Decrease quantity">
                    <Minus size={16} />
                  </button>
                  <span>{line.quantity}</span>
                  <button onClick={() => updateQuantity(line.id, 1)} aria-label={`Increase ${line.name}`} title="Increase quantity">
                    <Plus size={16} />
                  </button>
                </div>
                <span>{formatMoney(line.price * line.quantity - line.discount)}</span>
                <button className="ghost-danger" onClick={() => setCart((current) => current.filter((item) => item.id !== line.id))} aria-label={`Remove ${line.name}`} title="Remove item">
                  <Trash2 size={16} />
                </button>
              </div>
            ))
          )}
        </div>

        <div className="summary">
          <Row label="Subtotal" value={formatMoney(totals.subtotal)} />
          <Row label="Discount" value={formatMoney(totals.discountTotal)} />
          <Row label="GST" value={formatMoney(totals.taxTotal)} />
          <Row label="Total" value={formatMoney(totals.grandTotal)} strong />
        </div>

        <div className="tenders">
          {["Cash", "UPI", "Card"].map((method) => (
            <button className={paymentMethod === method ? "selected" : ""} key={method} onClick={() => setPaymentMethod(method)}>
              {method === "Card" ? <CreditCard size={18} /> : <Wallet size={18} />}
              {method}
            </button>
          ))}
        </div>

        <button className="complete-sale" onClick={completeSale} disabled={cart.length === 0}>
          <ReceiptText size={20} />
          Complete {paymentMethod} Sale
        </button>

        {receipt ? <div className="receipt">Receipt {receipt} completed for {formatMoney(totals.grandTotal)}</div> : null}
      </aside>
    </main>
  );
}

function Row({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className={strong ? "summary-row total" : "summary-row"}>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}
