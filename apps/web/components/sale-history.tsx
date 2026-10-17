"use client";

import { FormEvent, useEffect, useState } from "react";
import { ChevronLeft, ChevronRight, ReceiptText } from "lucide-react";
import { CustomerResponse, PagedResponse, SaleResponse, TerminalContextResponse, createClient } from "../lib/api";
import { formatMoney } from "../lib/pos-calculations.mjs";
import { ReceiptView } from "./checkout";

export function SaleHistory({ client }: { client: ReturnType<typeof createClient> | null }) {
  const [result, setResult] = useState<PagedResponse<SaleResponse> | null>(null);
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<SaleResponse | null>(null);
  const [error, setError] = useState("");
  const [customers, setCustomers] = useState<CustomerResponse[]>([]);
  const [terminals, setTerminals] = useState<TerminalContextResponse[]>([]);
  useEffect(() => {
    if (!client) return;
    let cancelled = false;
    Promise.all([client.customers(), client.terminals()]).then(([people, devices]) => {
      if (!cancelled) { setCustomers(people); setTerminals(devices); }
    }).catch(e => { if (!cancelled) setError(e.message); });
    return () => { cancelled = true; };
  }, [client]);
  useEffect(() => {
    if (!client) return;
    let cancelled = false;
    client.saleHistory(query + "&page=" + page).then(data => { if (!cancelled) { setResult(data); setError(""); } }).catch(e => { if (!cancelled) setError(e.message); });
    return () => { cancelled = true; };
  }, [client, query, page]);
  function search(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const params = new URLSearchParams();
    for (const [key, value] of new FormData(event.currentTarget)) {
      if (!value) continue;
      params.set(key, key === "fromUtc" || key === "toUtc" ? new Date(String(value)).toISOString() : String(value));
    }
    setPage(1); setQuery(params.toString());
  }
  return <section className="history-section" id="sales"><h2>Sales History</h2>
    <form className="history-filters" onSubmit={search}><input name="invoice" aria-label="Invoice search" placeholder="Invoice"/>
      <input name="fromUtc" aria-label="From date" type="datetime-local"/><input name="toUtc" aria-label="To date" type="datetime-local"/>
      <select name="method" aria-label="Payment method filter"><option value="">All payments</option>{["Cash", "Upi", "Card", "Wallet", "Credit"].map(m => <option key={m}>{m}</option>)}</select>
      <select name="customerId" aria-label="Customer filter"><option value="">All customers</option>{customers.map(c => <option value={c.id} key={c.id}>{c.displayName}</option>)}</select>
      <select name="terminalId" aria-label="Terminal filter"><option value="">All terminals</option>{terminals.map(t => <option value={t.terminalId} key={t.terminalId}>{t.terminalName}</option>)}</select>
      <button className="secondary-link">Search</button></form>
    {error && <p role="alert">{error}</p>}
    <div className="table-scroll"><table><thead><tr><th>Invoice</th><th>Date</th><th>Total</th><th>Receipt</th></tr></thead><tbody>{result?.items.map(s => <tr key={s.id}><td>{s.invoiceNumber}</td><td>{s.completedAtUtc && new Date(s.completedAtUtc).toLocaleString()}</td><td>{formatMoney(s.grandTotal)}</td><td><button className="icon-button" title="View receipt" aria-label={`View receipt ${s.invoiceNumber}`} onClick={() => setSelected(s)}><ReceiptText size={18}/></button></td></tr>)}</tbody></table></div>
    {result?.totalCount === 0 && <p>No sales found</p>}
    <div className="toolbar"><button className="icon-button" aria-label="Previous sales page" title="Previous page" disabled={page <= 1} onClick={() => setPage(page - 1)}><ChevronLeft size={18}/></button><span>Page {page} | {result?.totalCount ?? 0} sales</span><button className="icon-button" aria-label="Next sales page" title="Next page" disabled={!result || page * result.pageSize >= result.totalCount} onClick={() => setPage(page + 1)}><ChevronRight size={18}/></button></div>
    {selected && <><button className="secondary-link" onClick={() => setSelected(null)}>Close receipt</button><ReceiptView sale={selected}/></>}
  </section>;
}
