"use client";

import { BarChart3, Boxes, CreditCard, LogOut, PackageSearch, RefreshCw, ShoppingCart, Users } from "lucide-react";
import Link from "next/link";
import { useEffect, useState } from "react";
import { BootstrapResponse, CategoryResponse, TaxCategoryResponse, TodayReportResponse, createClient, login } from "../lib/api";
import { SaleHistory } from "../components/sale-history";
import { ProductManager } from "../components/product-manager";
import { RetailOverview } from "../components/retail-overview";
import { InventoryManager } from "../components/inventory-manager";
import { CustomerSelector } from "../components/customer-selector";

const nav = [["Dashboard", "dashboard", BarChart3], ["Sales", "sales", ShoppingCart], ["Products", "products", PackageSearch], ["Inventory", "inventory", Boxes], ["Customers", "customers", Users], ["Payments", "payments", CreditCard]] as const;
type Client = ReturnType<typeof createClient>;

export default function Dashboard() {
  const [client, setClient] = useState<Client | null>(null);
  const [context, setContext] = useState<BootstrapResponse | null>(null);
  const [categories, setCategories] = useState<CategoryResponse[]>([]);
  const [taxes, setTaxes] = useState<TaxCategoryResponse[]>([]);
  const [report, setReport] = useState<TodayReportResponse | null>(null);
  const [customer, setCustomer] = useState("");
  const [error, setError] = useState("");

  async function refresh(api = client) {
    if (!api) return;
    try {
      const [boot, categoryList, taxList, today] = await Promise.all([api.bootstrap(), api.categories(), api.taxCategories(), api.report()]);
      setContext(boot); setCategories(categoryList); setTaxes(taxList); setReport(today); setError("");
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to refresh"); }
  }
  useEffect(() => {
    let cancelled = false;
    login().then(auth => {
      if (cancelled) return;
      const api = createClient(auth.accessToken);
      setClient(api); return refresh(api);
    }).catch(e => setError(e.message));
    return () => { cancelled = true; };
  }, []);
  return <main className="shell"><aside className="sidebar"><div className="brand">Pilot POS</div><nav>{nav.map(([label, id, Icon]) => <a href={"#" + id} key={id}><Icon size={18}/><span>{label}</span></a>)}</nav></aside>
    <section className="workspace"><header className="topbar"><div><p className="eyebrow">{context?.tenantName ?? "Pilot POS"}</p><h1>Retail Operations</h1>{error && <p role="alert" className="sync-error">{error}</p>}</div>
      <div className="toolbar"><button className="icon-button" title="Refresh dashboard" aria-label="Refresh dashboard" onClick={() => refresh()}><RefreshCw size={18}/></button><Link className="primary-link" href="/pos"><ShoppingCart size={18}/>Open POS</Link><button className="icon-button" title="Sign out" aria-label="Sign out" onClick={() => { sessionStorage.removeItem("pilot-session"); window.location.assign("/login"); }}><LogOut size={18}/></button></div></header>
      <RetailOverview report={report}/>
      <ProductManager client={client} categories={categories} taxes={taxes}/>
      <SaleHistory client={client}/>
      <InventoryManager client={client} context={context}/>
      <section id="customers" className="history-section"><h2>Customers</h2><CustomerSelector client={client} value={customer} onChange={setCustomer} disabled={!client}/></section>
    </section>
  </main>;
}
