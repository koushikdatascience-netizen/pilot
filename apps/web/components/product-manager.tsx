"use client";

import { FormEvent, useEffect, useState } from "react";
import { ChevronLeft, ChevronRight, Pencil, Plus, Save, ToggleLeft, ToggleRight, X } from "lucide-react";
import { CategoryResponse, PagedResponse, ProductResponse, TaxCategoryResponse, createClient } from "../lib/api";
import { formatMoney } from "../lib/pos-calculations.mjs";

export function ProductManager({ client, categories, taxes }: { client: ReturnType<typeof createClient> | null; categories: CategoryResponse[]; taxes: TaxCategoryResponse[] }) {
  const [result, setResult] = useState<PagedResponse<ProductResponse> | null>(null);
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [editing, setEditing] = useState<ProductResponse | null>(null);
  const [open, setOpen] = useState(false);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    if (!client) return;
    let cancelled = false;
    const timer = setTimeout(() => client.productPage(query, page, 20, true).then(data => { if (!cancelled) setResult(data); }).catch(e => { if (!cancelled) setError(e.message); }), 200);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [client, query, page, revision]);
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!client) return;
    const data = new FormData(event.currentTarget);
    const body = { name: String(data.get("name")), sku: String(data.get("sku")), barcode: String(data.get("barcode")) || null,
      categoryId: String(data.get("categoryId")), taxCategoryId: String(data.get("taxCategoryId")), hsnSac: String(data.get("hsnSac")) || null,
      unitOfMeasure: String(data.get("unitOfMeasure")), price: Number(data.get("price")), currency: "INR", isStockTracked: data.has("tracked"), isActive: editing?.isActive ?? true };
    setBusy(true); setError("");
    try { if (editing) await client.updateProduct(editing.id, body); else await client.createProduct(body); setOpen(false); setRevision(r => r + 1); }
    catch (e) { setError(e instanceof Error ? e.message : "Product save failed"); }
    finally { setBusy(false); }
  }
  async function toggle(product: ProductResponse) {
    if (!client) return;
    try { if (product.isActive) await client.deactivateProduct(product.id); else await client.activateProduct(product.id); setRevision(r => r + 1); }
    catch (e) { setError(e instanceof Error ? e.message : "Status change failed"); }
  }
  return <section id="products" className="history-section"><div className="panel-title"><h2>Product Catalog</h2><button className="primary-link" disabled={!client} onClick={() => { setEditing(null); setOpen(true); }}><Plus size={18}/>Product</button></div>
    <input aria-label="Search catalog" placeholder="Name, SKU or barcode" value={query} onChange={e => { setPage(1); setQuery(e.target.value); }}/>
    {error && <p role="alert" className="sync-error">{error}</p>}
    <div className="table-scroll"><table><thead><tr><th>Product</th><th>SKU</th><th>Price</th><th>Status</th><th>Edit</th></tr></thead><tbody>{result?.items.map(p => <tr key={p.id}><td>{p.name}</td><td>{p.sku}</td><td>{formatMoney(p.price)}</td><td><button className="table-action" title={p.isActive ? "Deactivate" : "Activate"} onClick={() => toggle(p)}>{p.isActive ? <ToggleRight size={18}/> : <ToggleLeft size={18}/>} {p.isActive ? "Active" : "Inactive"}</button></td><td><button className="icon-button" aria-label={`Edit ${p.name}`} title="Edit product" onClick={() => { setEditing(p); setOpen(true); }}><Pencil size={18}/></button></td></tr>)}</tbody></table></div>
    <div className="toolbar"><button className="icon-button" title="Previous page" aria-label="Previous products page" disabled={page <= 1} onClick={() => setPage(page - 1)}><ChevronLeft size={18}/></button><span>Page {page} | {result?.totalCount ?? 0} products</span><button className="icon-button" title="Next page" aria-label="Next products page" disabled={!result || page * result.pageSize >= result.totalCount} onClick={() => setPage(page + 1)}><ChevronRight size={18}/></button></div>
    {open && <div className="modal-shade"><section role="dialog" aria-modal="true" aria-label="Product editor" className="product-editor"><div className="panel-title"><h2>{editing ? "Edit Product" : "Create Product"}</h2><button className="icon-button" title="Close editor" aria-label="Close editor" disabled={busy} onClick={() => setOpen(false)}><X size={18}/></button></div><form className="form-panel" key={editing?.id ?? "new"} onSubmit={save}>
      <label>Name<input name="name" required defaultValue={editing?.name}/></label><label>SKU<input name="sku" required defaultValue={editing?.sku}/></label><label>Barcode<input name="barcode" defaultValue={editing?.barcode ?? ""}/></label>
      <label>Category<select name="categoryId" defaultValue={editing?.categoryId}>{categories.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label>
      <label>Tax<select name="taxCategoryId" defaultValue={editing?.taxCategoryId}>{taxes.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
      <label>HSN/SAC<input name="hsnSac" defaultValue={editing?.hsnSac ?? ""}/></label><label>Unit<input name="unitOfMeasure" required defaultValue={editing?.unitOfMeasure ?? "pc"}/></label><label>Price<input name="price" type="number" min="0" step="0.01" required defaultValue={editing?.price}/></label><label><input name="tracked" type="checkbox" defaultChecked={editing?.isStockTracked ?? true}/>Stock tracked</label>
      <button className="primary-link" disabled={busy || !categories.length || !taxes.length}><Save size={18}/>Save</button>
    </form></section></div>}
  </section>;
}
