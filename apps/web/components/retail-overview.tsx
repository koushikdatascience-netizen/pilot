import { TodayReportResponse } from "../lib/api";
import { formatMoney } from "../lib/pos-calculations.mjs";

export function RetailOverview({ report }: { report: TodayReportResponse | null }) {
  const metrics = [["Net revenue today", formatMoney(report?.revenue ?? 0)], ["Gross sales today", formatMoney(report?.grossRevenue ?? 0)], ["Refunds today", formatMoney(report?.refundTotal ?? 0)], ["Transactions", String(report?.transactionCount ?? 0)], ["Average gross order", formatMoney(report?.averageOrderValue ?? 0)], ["Gross items sold", String(report?.itemsSold ?? 0)]];
  return <section id="dashboard"><div className="metrics">{metrics.map(([label, value]) => <div className="metric" key={label}><span>{label}</span><strong>{value}</strong></div>)}</div>
    <div className="content-grid"><section><h2>Top Products (Gross)</h2>{report?.topSellingProducts.length ? <table><thead><tr><th>Product</th><th>Quantity</th><th>Revenue</th></tr></thead><tbody>{report.topSellingProducts.map(p => <tr key={p.productId}><td>{p.productName}</td><td>{p.quantity}</td><td>{formatMoney(p.revenue)}</td></tr>)}</tbody></table> : <p>No sales today</p>}</section>
      <section id="payments"><h2>Gross Payment Mix</h2>{report?.paymentMix.length ? <table><thead><tr><th>Method</th><th>Amount</th><th>Count</th></tr></thead><tbody>{report.paymentMix.map(p => <tr key={p.method}><td>{p.method}</td><td>{formatMoney(p.amount)}</td><td>{p.count}</td></tr>)}</tbody></table> : <p>No payments today</p>}<h2>Refund Mix</h2>{report?.refundMix?.length ? <table><thead><tr><th>Method</th><th>Amount</th><th>Count</th></tr></thead><tbody>{report.refundMix.map(p => <tr key={p.method}><td>{p.method}</td><td>{formatMoney(p.amount)}</td><td>{p.count}</td></tr>)}</tbody></table> : <p>No refunds today</p>}</section></div>
  </section>;
}
