"use client";

import { FormEvent, useEffect, useState } from "react";
import { Check, Printer, RefreshCw, RotateCcw } from "lucide-react";
import { Correction, CorrectionHistory, CorrectionQuote, RefundAllocation, SaleResponse, TerminalContextResponse, createClient } from "../lib/api";
import { formatMoney } from "../lib/pos-calculations.mjs";
import { printReceipt } from "../lib/print-receipt";
import { RetailCommand, useRetailCommand } from "../lib/use-retail-command";

export function RetailCorrections({ client, sale, onRecorded }: { client: ReturnType<typeof createClient>; sale: SaleResponse; onRecorded?: () => void }) {
  const [history, setHistory] = useState<CorrectionHistory | null>(null);
  const [terminals, setTerminals] = useState<TerminalContextResponse[]>([]);
  const [terminal, setTerminal] = useState(sale.terminalId);
  const [permissions, setPermissions] = useState<string[]>([]);
  const [operation, setOperation] = useState(sale.status === "Completed" ? "returns" : "void");
  const [reason, setReason] = useState("");
  const [quantities, setQuantities] = useState<Record<string, string>>({});
  const [restock, setRestock] = useState<Record<string, boolean>>({});
  const [quote, setQuote] = useState<CorrectionQuote | null>(null);
  const [refunds, setRefunds] = useState<RefundAllocation[]>([]);
  const [recorded, setRecorded] = useState<Correction | null>(null);
  const [error, setError] = useState("");
  const [quoting, setQuoting] = useState(false);
  const command = useRetailCommand(`pilot-correction:${sale.id}`);
  useEffect(() => {
    let active = true;
    Promise.all([client.me(), client.terminals(), client.corrections(sale.id)]).then(([me, available, data]) => {
      if (!active) return;
      setPermissions(me.permissions); setTerminals(available); setHistory(data);
      setTerminal(available.find(t => t.terminalId === sale.terminalId)?.terminalId ?? available[0]?.terminalId ?? "");
      if (!me.permissions.includes("ProcessReturns")) setOperation("void");
    }).catch(e => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [client, sale.id, sale.terminalId]);
  const canReturn = sale.status === "Completed" && permissions.includes("ProcessReturns");
  const canVoid = permissions.includes("VoidSales");
  const locked = command.busy || !!command.pending || quoting;
  function body() {
    return { terminalId: terminal, clientGeneratedId: crypto.randomUUID(), reason, expectedStatus: sale.status,
      lines: operation === "void" ? [] : sale.lines.filter(l => Number(quantities[l.id]) > 0).map(l => ({ saleLineId: l.id, quantity: Number(quantities[l.id]), restock: restock[l.id] ?? true })), refunds: [] as RefundAllocation[] };
  }
  async function getQuote(event: FormEvent) {
    event.preventDefault(); setQuoting(true); setError("");
    try {
      const result = await client.correctionQuote(sale.id, operation, body());
      let remaining = Math.round(result.total * 100);
      const allocations = result.tenderAvailable.map(t => { const cents = Math.min(remaining, Math.round(t.amount * 100)); remaining -= cents; return { ...t, amount: cents / 100, reference: "" }; });
      setQuote(result); setRefunds(allocations);
    } catch (e) { setError(e instanceof Error ? e.message : "Quote failed."); } finally { setQuoting(false); }
  }
  async function record() {
    if (!command.pending && !window.confirm(`Record ${operation === "void" ? "void" : "return"} for ${sale.invoiceNumber}?`)) return;
    const request = body(); request.refunds = refunds.filter(r => r.amount > 0);
    const result = await command.run<Correction>({ operation, targetId: sale.id, body: request }, (pending: RetailCommand) => client.recordCorrection(pending.targetId, pending.operation, pending.body));
    if (result) {
      setRecorded(result); setQuote(null); setQuantities({}); setReason("");
      try { setHistory(await client.corrections(sale.id)); } catch (e) { setError(e instanceof Error ? e.message : "Refresh required."); }
      onRecorded?.();
    }
  }
  const returned = (id: string) => history?.corrections.flatMap(c => c.lines).filter(l => l.saleLineId === id).reduce((sum, l) => sum + l.quantity, 0) ?? 0;
  return <section className="history-section retail-corrections" aria-label="Sale corrections"><div className="panel-title"><h2>Returns and Voids</h2><span>{history?.effectiveStatus ?? "Loading..."}</span></div>
    {(error || command.error) && <p className="sync-error" role="alert">{error || command.error}</p>}
    {command.pending && <div className="toolbar"><span role="status">Pending command {String(command.pending.body.clientGeneratedId)}</span><button className="secondary-link" disabled={command.busy || !command.ready} onClick={record}><RefreshCw size={16}/>Retry pending command</button></div>}
    {(canReturn || canVoid) && !["Voided", "Returned"].includes(history?.effectiveStatus ?? "") && <>
      <form onSubmit={getQuote} className="retail-operation-form"><fieldset disabled={locked || !command.ready || !history || !!quote}>
        <div className="stock-form"><label>Operation<select value={operation} onChange={e => { setOperation(e.target.value); setQuote(null); }}>{canReturn && <option value="returns">Return</option>}{canVoid && <option value="void">Void sale</option>}</select></label>
          <label>Processing terminal<select value={terminal} onChange={e => setTerminal(e.target.value)} required>{terminals.map(t => <option key={t.terminalId} value={t.terminalId}>{t.locationName} / {t.terminalName}</option>)}</select></label>
          <label>Reason<input value={reason} onChange={e => setReason(e.target.value)} required maxLength={300}/></label></div>
        {operation === "returns" && <div className="table-scroll"><table><thead><tr><th>Item</th><th>Sold</th><th>Returned</th><th>Return quantity</th><th>Restock</th></tr></thead><tbody>{sale.lines.map(line => <tr key={line.id}><td>{line.productName}</td><td>{line.quantity}</td><td>{returned(line.id)}</td><td><input aria-label={`Return quantity ${line.productName}`} type="number" min="0" step="0.001" max={Math.max(0, line.quantity - returned(line.id))} value={quantities[line.id] ?? "0"} onChange={e => setQuantities(q => ({ ...q, [line.id]: e.target.value }))}/></td><td><input type="checkbox" aria-label={`Restock ${line.productName}`} checked={restock[line.id] ?? true} onChange={e => setRestock(r => ({ ...r, [line.id]: e.target.checked }))}/></td></tr>)}</tbody></table></div>}
        <button className="secondary-link" disabled={!terminal}><RotateCcw size={16}/>Calculate reversal</button>
      </fieldset></form>
      {quote && <section aria-label="Refund allocation"><h3>Refund {formatMoney(quote.total)}</h3><fieldset disabled={locked}>{refunds.map((refund, i) => <div className="refund-row" key={refund.method}><label>{refund.method}<input aria-label={`Refund ${refund.method}`} type="number" min="0" max={quote.tenderAvailable[i].amount} step="0.01" value={refund.amount} onChange={e => setRefunds(list => list.map((r, n) => n === i ? { ...r, amount: Number(e.target.value) } : r))}/></label>{!["Cash", "Credit"].includes(refund.method) && <label>Operator refund reference<input aria-label={`Refund reference ${refund.method}`} maxLength={160} value={refund.reference ?? ""} onChange={e => setRefunds(list => list.map((r, n) => n === i ? { ...r, reference: e.target.value } : r))}/></label>}</div>)}</fieldset>
        <div className="toolbar"><button className="primary-link" onClick={record} disabled={locked || Math.round(refunds.reduce((sum, r) => sum + r.amount, 0) * 100) !== Math.round(quote.total * 100) || refunds.some(r => r.amount > 0 && !["Cash", "Credit"].includes(r.method) && !r.reference?.trim())}><Check size={16}/>{operation === "void" ? "Void sale" : "Record return"}</button><button className="secondary-link" disabled={locked} onClick={() => setQuote(null)}>Edit reversal</button></div>
      </section>}
    </>}
    {recorded && <CorrectionReceipt correction={recorded} sale={sale}/>}
    {history && <div className="table-scroll"><table><thead><tr><th>Document</th><th>Kind</th><th>Amount</th><th>Reason</th><th>Receipt</th></tr></thead><tbody>{history.corrections.map(c => <tr key={c.id}><td>{c.number}</td><td>{c.kind}</td><td>{formatMoney(c.total)}</td><td>{c.reason}</td><td><button className="icon-button" title="Review correction" aria-label={`Review correction ${c.number}`} onClick={() => setRecorded(c)}><Printer size={16}/></button></td></tr>)}</tbody></table></div>}
  </section>;
}

function CorrectionReceipt({ correction: c, sale }: { correction: Correction; sale: SaleResponse }) {
  return <section className="receipt correction-receipt" aria-label="Correction receipt"><div className="panel-title"><strong>{c.kind} {c.number}</strong><button className="icon-button" title="Print correction" aria-label="Print correction" onClick={() => printReceipt("correction")}><Printer size={16}/></button></div><span>Original invoice {sale.invoiceNumber}</span><time>{new Date(c.occurredAtUtc).toLocaleString()}</time><span>{c.reason}</span>{c.lines.map(l => <div key={l.saleLineId}>{sale.lines.find(s => s.id === l.saleLineId)?.productName} | {l.quantity} | {formatMoney(l.total)}<small>Taxable {formatMoney(l.taxableAmount)} | CGST {formatMoney(l.cgstAmount)} | SGST {formatMoney(l.sgstAmount)} | IGST {formatMoney(l.igstAmount)} | {l.restock ? "Restock" : "No restock"}</small></div>)}<strong>Refund {formatMoney(c.total)}</strong>{c.refunds.map(r => <span key={r.method}>{r.method} {formatMoney(r.amount)} {r.reference}</span>)}</section>;
}
