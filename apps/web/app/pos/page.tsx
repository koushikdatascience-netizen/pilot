"use client";

import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { Search, PauseCircle, ReceiptText } from "lucide-react";
import { BootstrapResponse, ProductResponse, SaleResponse, TaxCategoryResponse, TerminalContextResponse, createClient, login } from "../../lib/api";
import { calculateCartTotals, formatMoney } from "../../lib/pos-calculations.mjs";
import { Cart, CartLine, PaymentPanel, ReceiptView } from "../../components/checkout";
import { CustomerSelector } from "../../components/customer-selector";
import { RetailCorrections } from "../../components/retail-corrections";

type Client = ReturnType<typeof createClient>;

export default function PosPage() {
  const [client, setClient] = useState<Client | null>(null);
  const [context, setContext] = useState<BootstrapResponse | null>(null);
  const [products, setProducts] = useState<ProductResponse[]>([]);
  const [taxes, setTaxes] = useState<TaxCategoryResponse[]>([]);
  const [terminals, setTerminals] = useState<TerminalContextResponse[]>([]);
  const [customerId, setCustomerId] = useState("");
  const [cart, setCart] = useState<CartLine[]>([]);
  const [held, setHeld] = useState<SaleResponse[]>([]);
  const [voiding, setVoiding] = useState<SaleResponse | null>(null);
  const [canVoid, setCanVoid] = useState(false);
  const [activeHeld, setActiveHeld] = useState<SaleResponse | null>(null);
  const [receipt, setReceipt] = useState<SaleResponse | null>(null);
  const [query, setQuery] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const scanner = useRef<HTMLInputElement>(null);
  const submitting = useRef(false);
  const commandId = useRef("");
  const savedHeld = useRef(false);
  const [recovering, setRecovering] = useState(false);
  const pending = useRef<{ hold: boolean; body: Record<string, unknown>; tender: { method: string; amount: number; amountTendered: number; reference: null }[] } | null>(null);
  const [payments, setPayments] = useState([{ method: "Cash", amount: "", tendered: "" }]);
  const totals = useMemo(() => calculateCartTotals(cart), [cart]);
  const total = totals.grandTotal;

  useEffect(() => {
    let cancelled = false;
    login().then(async auth => {
      const api = createClient(auth.accessToken);
      const [boot, items, taxList, people, holds, available] = await Promise.all([api.bootstrap(), api.products(), api.taxCategories(), api.customers(), api.sales("Held"), api.terminals()]);
      if (cancelled) return;
      const savedId = localStorage.getItem("pilot-terminal:" + auth.tenantId);
      const selected = available.find(t => t.terminalId === savedId) ?? available[0];
      setClient(api); setCanVoid(auth.permissions.includes("VoidSales")); setContext(selected ? { ...boot, ...selected } : null); setTerminals(available); setProducts(items); setTaxes(taxList); setHeld(holds);
      setCustomerId(people.find(c => c.isWalkIn)?.id ?? "");
    }).catch(e => setError(e.message));
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    if (!client) return;
    let cancelled = false;
    const timer = setTimeout(() => client.products(query).then(items => { if (!cancelled) setProducts(items); }).catch(e => { if (!cancelled) setError(e.message); }), 200);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [client, query]);

  function add(product: ProductResponse) {
    if (busy || recovering) return;
    const tax = taxes.find(t => t.id === product.taxCategoryId);
    setCart(lines => {
      const found = lines.find(l => l.id === product.id);
      return found ? lines.map(l => l.id === product.id ? { ...l, quantity: l.quantity + 1 } : l)
        : [...lines, { ...product, quantity: 1, discount: 0, taxRate: tax?.rate ?? 0, taxBehavior: tax?.taxBehavior ?? "Inclusive" }];
    });
    setReceipt(null); setError(""); scanner.current?.focus();
  }

  async function scan(event: FormEvent) {
    event.preventDefault();
    if (!client || !query.trim()) return;
    try {
      const code = query.trim();
      const product = await client.productByBarcode(code) ?? await client.productBySku(code);
      if (product) { add(product); setQuery(""); }
      else setError("No matching barcode or SKU.");
    } catch (e) { setError(e instanceof Error ? e.message : "Search failed"); }
    finally { scanner.current?.focus(); }
  }

  function clear() { setCart([]); setActiveHeld(null); commandId.current = ""; savedHeld.current = false; pending.current = null; setRecovering(false); setPayments([{ method: "Cash", amount: "", tendered: "" }]); }

  async function submit(hold: boolean) {
    if (!client || !context || submitting.current || cart.length === 0) return;
    submitting.current = true; setBusy(true); setError("");
    commandId.current ||= crypto.randomUUID();
    hold = pending.current?.hold ?? hold;
    try {
      const body = pending.current?.body ?? { locationId: context.locationId, terminalId: context.terminalId, customerId: customerId || null, clientGeneratedId: activeHeld?.clientGeneratedId ?? commandId.current,
        lines: cart.map(l => ({ productId: l.id, quantity: l.quantity, discountAmount: l.discount })) };
      const tender = pending.current?.tender ?? payments.map(p => ({ method: p.method, amount: Number(p.amount || total), amountTendered: Number(p.tendered || p.amount || total), reference: null }));
      pending.current = { hold, body, tender };
      if (!hold && activeHeld && !savedHeld.current) { await client.updateHeldSale(activeHeld.id, body); savedHeld.current = true; }
      const result = hold ? activeHeld ? await client.updateHeldSale(activeHeld.id, body) : await client.holdSale(body) : activeHeld
        ? await client.completeHeldSale(activeHeld.id, { payments: tender })
        : await client.completeSale({ ...body, payments: tender });
      clear();
      if (!hold) setReceipt(result);
      try { setHeld(await client.sales("Held")); } catch { setError("Sale saved. Refresh to update held carts."); }
    } catch (e) {
      if (e instanceof Error && "status" in e && Number(e.status) < 500) { pending.current = null; setRecovering(false); savedHeld.current = false; }
      else setRecovering(true);
      setError(e instanceof Error ? e.message : "Checkout failed");
    }
    finally { submitting.current = false; setBusy(false); scanner.current?.focus(); }
  }

  function resume(sale: SaleResponse) {
    if (cart.length) { setError("Hold or clear the current cart first."); return; }
    setActiveHeld(sale); setCustomerId(sale.customerId ?? ""); setReceipt(null);
    const terminal = terminals.find(t => t.terminalId === sale.terminalId);
    if (context && terminal) setContext({ ...context, ...terminal });
    setCart(sale.lines.map(l => ({
      id: l.productId, name: l.productName, sku: l.sku, barcode: l.barcode, price: l.unitPrice,
      categoryId: "", taxCategoryId: "", hsnSac: null, unitOfMeasure: "", currency: sale.currency, isStockTracked: true, isActive: true,
      quantity: l.quantity, discount: l.discountAmount, taxRate: l.taxRate, taxBehavior: l.lineTotal > l.unitPrice * l.quantity - l.discountAmount ? "Exclusive" : "Inclusive"
    })));
  }

  return <main className="pos">
    <section className="pos-left">
      <header className="pos-header"><div><p className="eyebrow">Pilot POS</p><h1>Retail Checkout</h1><span className="sync-status">{context ? context.tenantName : "Connecting..."}</span></div><div className="toolbar"><a className="secondary-link" href="/cash-drawer">Cash drawer</a><a className="secondary-link" href="/">Back office</a></div></header>
      <label>Terminal<select className="customer-select" aria-label="Terminal" value={context?.terminalId ?? ""} disabled={busy || cart.length > 0} onChange={e => {
        const terminal = terminals.find(t => t.terminalId === e.target.value);
        if (terminal && context) { setContext({ ...context, ...terminal }); localStorage.setItem("pilot-terminal:" + terminal.tenantId, terminal.terminalId); }
      }}>{terminals.map(t => <option key={t.terminalId} value={t.terminalId}>{t.locationName} / {t.terminalName}</option>)}</select></label>
      {error && <p role="alert" className="sync-error">{error}</p>}
      <form className="scan-box" onSubmit={scan}><Search size={20}/><input ref={scanner} autoFocus value={query} onChange={e => setQuery(e.target.value)} aria-label="Scan barcode or search product" placeholder="Scan barcode or search product" disabled={busy}/></form>
      <div className="quick-grid">{products.map(p => <button className="product-tile" key={p.id} disabled={busy || recovering} onClick={() => add(p)}><span>{p.name}</span><small>{p.sku}</small><strong>{formatMoney(p.price)}</strong></button>)}</div>
      {products.length === 0 && <p className="empty-state">No matching products</p>}
      <section className="held-sales"><h2>Held Sales</h2>{held.length === 0 ? <span className="muted">No held carts</span> : held.map(s => <div className="mini-sale" key={s.id}><span>{s.invoiceNumber}</span><strong>{formatMoney(s.grandTotal)}</strong><button disabled={busy || recovering} onClick={() => resume(s)}>Resume</button>{canVoid && <button disabled={busy || recovering} onClick={() => setVoiding(s)}>Void</button>}</div>)}</section>
      {voiding && client && <section><button className="secondary-link" onClick={() => setVoiding(null)}>Close reversal</button><RetailCorrections key={voiding.id} client={client} sale={voiding} onRecorded={() => { setHeld(list => list.filter(s => s.id !== voiding.id)); if (activeHeld?.id === voiding.id) clear(); }}/></section>}
    </section>
    <aside className="cart-panel">
      <h2>{activeHeld ? "Resumed Sale" : "Current Cart"}</h2>
      <CustomerSelector client={client} value={customerId} disabled={busy || recovering} onChange={setCustomerId}/>
      <Cart lines={cart} disabled={busy || recovering} onChange={setCart}/>
      <div className="summary"><div className="summary-row"><span>GST estimate</span><strong>{formatMoney(activeHeld?.taxTotal ?? totals.taxTotal)}</strong></div><div className="summary-row total"><span>Total</span><strong>{formatMoney(total)}</strong></div></div>
      <PaymentPanel payments={payments} onChange={setPayments} total={total} disabled={busy || recovering}/>
      <button className="complete-sale" disabled={busy || !client || cart.length === 0} onClick={() => submit(false)}><ReceiptText size={20}/>{busy ? "Processing..." : "Complete Sale"}</button>
      <div className="cart-actions"><button disabled={busy || recovering || cart.length === 0} onClick={() => submit(true)}><PauseCircle size={18}/>Hold</button><button disabled={busy || recovering} onClick={clear}>Clear</button></div>
      {receipt && <ReceiptView sale={receipt}/>}
    </aside>
  </main>;
}
