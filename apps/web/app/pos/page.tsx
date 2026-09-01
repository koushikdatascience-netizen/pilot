"use client";

import { CreditCard, Minus, Plus, ReceiptText, Search, Trash2, UserRound, Wallet } from "lucide-react";
import { FormEvent, useEffect, useMemo, useState } from "react";
import { BootstrapResponse, CustomerResponse, ProductResponse, StockBalanceResponse, TaxCategoryResponse, createClient, login } from "../../lib/api";
import { calculateCartTotals, formatMoney } from "../../lib/pos-calculations.mjs";

type Client = ReturnType<typeof createClient>;
type ProductTile = ProductResponse & { taxRate: number; taxBehavior: "Inclusive" | "Exclusive"; stock: number };
type CartLine = ProductTile & { quantity: number; discount: number };

export default function PosPage() {
  const [client, setClient] = useState<Client | null>(null);
  const [bootstrap, setBootstrap] = useState<BootstrapResponse | null>(null);
  const [products, setProducts] = useState<ProductResponse[]>([]);
  const [taxCategories, setTaxCategories] = useState<TaxCategoryResponse[]>([]);
  const [customers, setCustomers] = useState<CustomerResponse[]>([]);
  const [balances, setBalances] = useState<StockBalanceResponse[]>([]);
  const [query, setQuery] = useState("");
  const [cart, setCart] = useState<CartLine[]>([]);
  const [customerId, setCustomerId] = useState("");
  const [paymentMethod, setPaymentMethod] = useState("Cash");
  const [receipt, setReceipt] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function refresh(activeClient = client) {
    if (!activeClient) return;
    const [boot, productList, taxList, customerList, balanceList] = await Promise.all([
      activeClient.bootstrap(),
      activeClient.products(),
      activeClient.taxCategories(),
      activeClient.customers(),
      activeClient.balances()
    ]);
    setBootstrap(boot);
    setProducts(productList);
    setTaxCategories(taxList);
    setCustomers(customerList);
    setBalances(balanceList);
    setCustomerId(customerList.find((customer) => customer.isWalkIn)?.id ?? customerList[0]?.id ?? "");
    setError(null);
  }

  useEffect(() => {
    let mounted = true;
    login()
      .then((auth) => {
        if (!mounted) return null;
        const api = createClient(auth.accessToken);
        setClient(api);
        return refresh(api);
      })
      .catch((ex: unknown) => setError(ex instanceof Error ? ex.message : "Could not connect to API"));
    return () => {
      mounted = false;
    };
  }, []);

  const taxById = useMemo(() => new Map(taxCategories.map((tax) => [tax.id, tax])), [taxCategories]);
  const stockByProduct = useMemo(() => new Map(balances.map((balance) => [balance.productId, balance.quantityOnHand])), [balances]);
  const productTiles = useMemo<ProductTile[]>(
    () =>
      products.map((product) => {
        const tax = taxById.get(product.taxCategoryId);
        return {
          ...product,
          barcode: product.barcode ?? "",
          taxRate: tax?.rate ?? 0,
          taxBehavior: tax?.taxBehavior ?? "Inclusive",
          stock: stockByProduct.get(product.id) ?? 0
        };
      }),
    [products, stockByProduct, taxById]
  );

  const visibleProducts = productTiles.filter((product) => {
    const needle = query.trim().toLowerCase();
    return !needle || product.name.toLowerCase().includes(needle) || product.sku.toLowerCase().includes(needle) || (product.barcode ?? "").includes(needle);
  });

  const totals = useMemo(() => calculateCartTotals(cart), [cart]);

  function addProduct(product: ProductTile) {
    setReceipt(null);
    setError(null);
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
    const exact = productTiles.find((product) => product.barcode === query.trim() || product.sku.toLowerCase() === query.trim().toLowerCase());
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

  async function completeSale() {
    if (!client || !bootstrap || cart.length === 0) return;
    setError(null);
    try {
      const response = await client.completeSale({
        locationId: bootstrap.locationId,
        terminalId: bootstrap.terminalId,
        customerId: customerId || null,
        clientGeneratedId: crypto.randomUUID(),
        lines: cart.map((line) => ({ productId: line.id, quantity: line.quantity, discountAmount: line.discount })),
        payments: [{ method: paymentMethod, amount: totals.grandTotal, reference: "WEB-POS" }]
      });
      setReceipt(`${response.invoiceNumber} completed for ${formatMoney(response.grandTotal)}`);
      setCart([]);
      await refresh();
    } catch (ex: unknown) {
      setError(ex instanceof Error ? ex.message : "Sale failed");
    }
  }

  return (
    <main className="pos">
      <section className="pos-left">
        <header className="pos-header">
          <div>
            <p className="eyebrow">Retail POS</p>
            <h1>Kalyani Main Store</h1>
            {error ? <span className="sync-error">{error}</span> : <span className="sync-status">{client ? "API connected" : "Connecting to API..."}</span>}
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
            <button className="product-tile" key={product.id} onClick={() => addProduct(product)} disabled={!client || product.stock <= 0}>
              <span>{product.name}</span>
              <small>{product.sku} - {product.stock} on hand</small>
              <strong>{formatMoney(product.price)}</strong>
            </button>
          ))}
        </div>
      </section>

      <aside className="cart-panel">
        <div className="panel-title">
          <h2>Current Cart</h2>
          <UserRound size={18} />
        </div>

        <select className="customer-select" value={customerId} onChange={(event) => setCustomerId(event.target.value)}>
          {customers.map((customer) => <option key={customer.id} value={customer.id}>{customer.displayName}</option>)}
        </select>

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
          <Row label="GST estimate" value={formatMoney(totals.taxTotal)} />
          <Row label="Server tender" value={formatMoney(totals.grandTotal)} strong />
        </div>

        <div className="tenders">
          {["Cash", "Upi", "Card"].map((method) => (
            <button className={paymentMethod === method ? "selected" : ""} key={method} onClick={() => setPaymentMethod(method)}>
              {method === "Card" ? <CreditCard size={18} /> : <Wallet size={18} />}
              {method}
            </button>
          ))}
        </div>

        <button className="complete-sale" onClick={completeSale} disabled={!client || !bootstrap || cart.length === 0}>
          <ReceiptText size={20} />
          Complete {paymentMethod} Sale
        </button>

        {receipt ? <div className="receipt">Receipt {receipt}</div> : null}
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
