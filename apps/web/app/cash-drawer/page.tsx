"use client";

import Link from "next/link";
import { ArrowLeft, Banknote, Check, Plus, RefreshCw } from "lucide-react";
import { FormEvent, useEffect, useState } from "react";
import { DrawerSession, TerminalContextResponse, createClient, login } from "../../lib/api";
import { formatMoney } from "../../lib/pos-calculations.mjs";
import { RetailCommand, useRetailCommand } from "../../lib/use-retail-command";

export default function CashDrawer() {
  const [client, setClient] = useState<ReturnType<typeof createClient> | null>(null);
  const [tenantId, setTenantId] = useState("");
  const [allowed, setAllowed] = useState<boolean | null>(null);
  const [terminals, setTerminals] = useState<TerminalContextResponse[]>([]);
  const [terminal, setTerminal] = useState("");
  const [sessions, setSessions] = useState<DrawerSession[]>([]);
  const [selectedId, setSelectedId] = useState("");
  const [revision, setRevision] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const command = useRetailCommand(`pilot-drawer:${tenantId}:${terminal}`);
  useEffect(() => {
    let active = true;
    login().then(async auth => {
      if (!active) return;
      const api = createClient(auth.accessToken); setClient(api); setTenantId(auth.tenantId);
      const access = auth.permissions.includes("ManageCashDrawer"); setAllowed(access);
      if (!access) { setLoading(false); return; }
      const available = await api.terminals();
      if (active) { setTerminals(available); setTerminal(available[0]?.terminalId ?? ""); if (!available.length) setLoading(false); }
    }).catch(e => { if (active) { setError(e.message); setLoading(false); } });
    return () => { active = false; };
  }, []);
  useEffect(() => {
    if (!client || !terminal || !allowed) return;
    let active = true; setLoading(true); setError("");
    client.drawers(terminal).then(data => { if (active) { setSessions(data); setSelectedId(id => data.some(s => s.id === id) ? id : data[0]?.id ?? ""); } })
      .catch(e => { if (active) setError(e.message); }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [client, terminal, allowed, revision]);
  const selected = sessions.find(s => s.id === selectedId);
  const open = sessions.find(s => !s.closedAtUtc);
  const locked = loading || command.busy || !!command.pending || !command.ready;
  async function send(pending: RetailCommand) {
    if (!client) throw new Error("Sign in to continue.");
    return pending.operation === "open" ? client.openDrawer(pending.targetId, pending.body) : pending.operation === "close" ? client.closeDrawer(pending.targetId, pending.body) : client.drawerMovement(pending.targetId, pending.body);
  }
  async function execute(pending: RetailCommand) {
    const response = await command.run<DrawerSession>(pending, send);
    if (response) { setSelectedId(response.id); setRevision(r => r + 1); }
  }
  function mutate(event: FormEvent<HTMLFormElement>, operation: string) {
    event.preventDefault();
    if (operation === "close" && !window.confirm("Close and reconcile this drawer session?")) return;
    const form = new FormData(event.currentTarget);
    const body: Record<string, unknown> = { clientGeneratedId: crypto.randomUUID() };
    if (operation === "open") body.openingFloat = Number(form.get("float"));
    if (operation === "close") { body.countedCash = Number(form.get("counted")); body.note = String(form.get("note") ?? ""); }
    if (operation === "move") { body.kind = form.get("kind"); body.amount = Number(form.get("amount")); body.reason = form.get("reason"); }
    void execute({ operation, targetId: operation === "open" ? terminal : selectedId, body });
  }
  return <main className="admin-workspace cash-workspace"><header className="topbar"><div><Link className="secondary-link" href="/"><ArrowLeft size={16}/>Back office</Link><h1>Cash Drawer</h1></div><button className="icon-button" aria-label="Refresh drawer" title="Refresh drawer" onClick={() => setRevision(r => r + 1)} disabled={loading || command.busy}><RefreshCw size={18}/></button></header>
    {(error || command.error) && <p role="alert" className="sync-error">{error || command.error}</p>}
    {loading && <p role="status">Loading drawer...</p>}{allowed === false && <p role="alert">Access denied. Cash drawer permission is required.</p>}
    {allowed && <><div className="stock-form"><label>Drawer terminal<select disabled={command.busy || !!command.pending} value={terminal} onChange={e => { setSelectedId(""); setTerminal(e.target.value); }}>{terminals.map(t => <option key={t.terminalId} value={t.terminalId}>{t.locationName} / {t.terminalName}</option>)}</select></label><label>Session<select aria-label="Drawer session" value={selectedId} disabled={locked} onChange={e => setSelectedId(e.target.value)}><option value="">No session</option>{sessions.map(s => <option key={s.id} value={s.id}>{s.closedAtUtc ? "Closed" : "Open"} | {new Date(s.openedAtUtc).toLocaleString()}</option>)}</select></label></div>
      {command.pending && <div className="toolbar"><span role="status">Pending command {String(command.pending.body.clientGeneratedId)}</span><button className="secondary-link" disabled={command.busy} onClick={() => execute(command.pending!)}><RefreshCw size={16}/>Retry pending command</button></div>}
      {!open && terminal && <form className="stock-form" onSubmit={e => mutate(e, "open")}><h2>Open Drawer</h2><label>Opening float<input name="float" type="number" min="0" step="0.01" defaultValue="0" required disabled={locked}/></label><button className="primary-link" disabled={locked}><Plus size={16}/>Open drawer</button></form>}
      {selected && <section aria-label="Drawer reconciliation"><div className="metrics">{[["Status", selected.closedAtUtc ? "Closed" : "Open"], ["Opening float", formatMoney(selected.openingFloat)], ["Expected cash", formatMoney(selected.expectedCash)], ["Counted cash", selected.countedCash == null ? "Not counted" : formatMoney(selected.countedCash)], ["Variance", selected.variance == null ? "Not closed" : formatMoney(selected.variance)]].map(([label, value]) => <div className="metric" key={label}><span>{label}</span><strong>{value}</strong></div>)}</div>
        {!selected.closedAtUtc && <div className="drawer-forms"><form className="stock-form" onSubmit={e => mutate(e, "move")}><h2>Cash Movement</h2><label>Movement<select name="kind" disabled={locked}><option value="PaidIn">Paid in</option><option value="PaidOut">Paid out</option></select></label><label>Movement amount<input name="amount" type="number" min="0.01" step="0.01" required disabled={locked}/></label><label>Movement reason<input name="reason" required maxLength={300} disabled={locked}/></label><button className="secondary-link" disabled={locked}><Banknote size={16}/>Record movement</button></form>
          <form className="stock-form" onSubmit={e => mutate(e, "close")}><h2>Close Drawer</h2><label>Counted cash<input name="counted" type="number" min="0" step="0.01" required disabled={locked}/></label><label>Close note<input name="note" maxLength={300} disabled={locked}/></label><button className="primary-link" disabled={locked}><Check size={16}/>Close drawer</button></form></div>}
        {selected.closeNote && <p>{selected.closeNote}</p>}<h2>Cash Movements</h2><div className="table-scroll"><table><thead><tr><th>Time</th><th>Kind</th><th>Cash delta</th><th>Reason</th><th>Source UUID</th></tr></thead><tbody>{selected.movements.map(m => <tr key={m.id}><td>{new Date(m.occurredAtUtc).toLocaleString()}</td><td>{m.kind}</td><td>{formatMoney(m.amountDelta)}</td><td>{m.reason}</td><td>{m.sourceId}</td></tr>)}</tbody></table></div>{selected.movements.length === 0 && <p>No cash movements.</p>}
      </section>}{!loading && !sessions.length && <p>No drawer sessions.</p>}</>}
  </main>;
}
