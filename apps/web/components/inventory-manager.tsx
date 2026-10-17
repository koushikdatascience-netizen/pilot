"use client";

import { FormEvent, useEffect, useState } from "react";
import { Plus, RefreshCw } from "lucide-react";
import { BootstrapResponse, ProductResponse, StockBalanceResponse, createClient } from "../lib/api";

export function InventoryManager({ client, context }: { client: ReturnType<typeof createClient> | null; context: BootstrapResponse | null }) {
  const [balances, setBalances] = useState<StockBalanceResponse[]>([]);
  const [products, setProducts] = useState<ProductResponse[]>([]);
  const [query, setQuery] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    if (!client) return;
    let cancelled = false;
    const timer = setTimeout(() => Promise.all([client.balances(), client.products(query)]).then(([stock, catalog]) => { if (!cancelled) { setBalances(stock); setProducts(catalog); } }).catch(e => { if (!cancelled) setError(e.message); }), 200);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [client, query, revision]);
  async function adjust(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!client || !context) return;
    const form = event.currentTarget;
    const data = new FormData(form);
    const body = { locationId: context.locationId, warehouseId: context.warehouseId, productId: String(data.get("productId")), quantity: Number(data.get("quantity")), unitCost: Number(data.get("unitCost") || 0), type: String(data.get("type")), reason: String(data.get("reason")) };
    setBusy(true); setError("");
    try { if (body.type === "OpeningStock") await client.openingStock(body); else await client.adjustStock(body); form.reset(); setRevision(r => r + 1); }
    catch (e) { setError(e instanceof Error ? e.message : "Stock adjustment failed"); }
    finally { setBusy(false); }
  }
  return <section id="inventory" className="history-section"><div className="panel-title"><h2>Inventory</h2><button className="icon-button" title="Refresh inventory" aria-label="Refresh inventory" onClick={() => setRevision(r => r + 1)}><RefreshCw size={18}/></button></div>
    {error && <p role="alert" className="sync-error">{error}</p>}
    <div className="table-scroll"><table><thead><tr><th>Product</th><th>SKU</th><th>Warehouse</th><th>On hand</th><th>State</th></tr></thead><tbody>{balances.filter(b => !query || (b.productName + b.sku).toLowerCase().includes(query.toLowerCase())).map(b => <tr key={b.warehouseId + b.productId}><td>{b.productName}</td><td>{b.sku}</td><td>{b.warehouseId === context?.warehouseId ? "Selected warehouse" : b.warehouseId}</td><td>{b.quantityOnHand}</td><td>{b.quantityOnHand <= 10 ? "Low stock" : "In stock"}</td></tr>)}</tbody></table></div>
    <form className="stock-form" onSubmit={adjust}><h3>Stock Movement</h3><input aria-label="Search stock products" placeholder="Search products" value={query} onChange={e => setQuery(e.target.value)}/>
      <label>Product<select name="productId" required>{products.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
      <label>Movement<select name="type"><option value="AdjustmentIncrease">Increase</option><option value="AdjustmentDecrease">Decrease</option><option value="Damage">Damage</option><option value="OpeningStock">Opening stock</option></select></label>
      <label>Quantity<input name="quantity" required type="number" min="0.001" step="0.001"/></label><label>Unit cost<input name="unitCost" type="number" min="0" step="0.01" defaultValue="0"/></label><label>Reason<input name="reason" maxLength={80} required/></label>
      <button className="primary-link" disabled={busy || !client || !products.length}><Plus size={18}/>{busy ? "Posting..." : "Post movement"}</button>
    </form>
  </section>;
}
