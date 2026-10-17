"use client";

import { FormEvent, useEffect, useState } from "react";
import { Plus, X } from "lucide-react";
import { CustomerResponse, createClient } from "../lib/api";

export function CustomerSelector({ client, value, onChange, disabled }: { client: ReturnType<typeof createClient> | null; value: string; onChange: (value: string) => void; disabled: boolean }) {
  const [query, setQuery] = useState("");
  const [customers, setCustomers] = useState<CustomerResponse[]>([]);
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    if (!client) return;
    let cancelled = false;
    const timer = setTimeout(() => client.customers(query).then(people => { if (!cancelled) setCustomers(people); }).catch(e => { if (!cancelled) setError(e.message); }), 200);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [client, query]);
  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!client) return;
    const data = new FormData(event.currentTarget);
    try {
      const customer = await client.createCustomer({ displayName: String(data.get("name")), phone: String(data.get("phone")), stateCode: String(data.get("state")), email: null, gstin: null, creditLimit: 0 });
      setCustomers(current => [...current, customer]); onChange(customer.id); setAdding(false); setError("");
    } catch (e) { setError(e instanceof Error ? e.message : "Customer creation failed"); }
  }
  return <div className="customer-picker"><input aria-label="Search customers" placeholder="Customer name or phone" value={query} disabled={disabled} onChange={e => setQuery(e.target.value)}/>
    <div className="toolbar"><label>Customer<select className="customer-select" aria-label="Customer" value={value} disabled={disabled} onChange={e => onChange(e.target.value)}><option value="">Walk-in</option>{customers.map(c => <option key={c.id} value={c.id}>{c.displayName}</option>)}</select></label>
      <button className="icon-button" title="Add customer" aria-label="Add customer" disabled={disabled} onClick={() => setAdding(!adding)}>{adding ? <X size={18}/> : <Plus size={18}/>}</button></div>
    {adding && <form className="form-panel" onSubmit={create}><input name="name" aria-label="Customer name" placeholder="Name" required/><input name="phone" aria-label="Customer phone" placeholder="Phone"/><input name="state" aria-label="Customer state code" placeholder="State code" defaultValue="19"/><button className="primary-link">Save customer</button></form>}
    {error && <p role="alert" className="sync-error">{error}</p>}
  </div>;
}
