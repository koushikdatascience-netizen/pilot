"use client";

import { BarChart3, Boxes, CreditCard, PackagePlus, PackageSearch, Plus, RefreshCw, Settings, ShoppingCart, UserPlus, Users } from "lucide-react";
import Link from "next/link";
import { FormEvent, useEffect, useMemo, useState } from "react";
import { BootstrapResponse, CategoryResponse, CustomerResponse, ProductResponse, StockBalanceResponse, TaxCategoryResponse, createClient, login } from "../lib/api";
import { formatMoney } from "../lib/pos-calculations.mjs";

const nav = [
  ["Dashboard", BarChart3],
  ["Sales", ShoppingCart],
  ["Products", PackageSearch],
  ["Inventory", Boxes],
  ["Customers", Users],
  ["Payments", CreditCard],
  ["Settings", Settings]
] as const;

type Client = ReturnType<typeof createClient>;
type Report = Awaited<ReturnType<Client["report"]>>;

export default function Dashboard() {
  const [client, setClient] = useState<Client | null>(null);
  const [bootstrap, setBootstrap] = useState<BootstrapResponse | null>(null);
  const [products, setProducts] = useState<ProductResponse[]>([]);
  const [categories, setCategories] = useState<CategoryResponse[]>([]);
  const [taxCategories, setTaxCategories] = useState<TaxCategoryResponse[]>([]);
  const [customers, setCustomers] = useState<CustomerResponse[]>([]);
  const [balances, setBalances] = useState<StockBalanceResponse[]>([]);
  const [report, setReport] = useState<Report | null>(null);
  const [status, setStatus] = useState("Connecting to API...");
  const [error, setError] = useState<string | null>(null);

  async function refresh(activeClient = client) {
    if (!activeClient) return;
    const [boot, productList, categoryList, taxList, customerList, balanceList, today] = await Promise.all([
      activeClient.bootstrap(),
      activeClient.products(),
      activeClient.categories(),
      activeClient.taxCategories(),
      activeClient.customers(),
      activeClient.balances(),
      activeClient.report()
    ]);
    setBootstrap(boot);
    setProducts(productList);
    setCategories(categoryList);
    setTaxCategories(taxList);
    setCustomers(customerList);
    setBalances(balanceList);
    setReport(today);
    setStatus("Synced with API");
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

  const stockByProduct = useMemo(() => new Map(balances.map((balance) => [balance.productId, balance.quantityOnHand])), [balances]);
  const lowStock = balances.filter((balance) => balance.quantityOnHand <= 5).length;

  async function submitProduct(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!client || categories.length === 0 || taxCategories.length === 0) return;
    const data = new FormData(event.currentTarget);
    await client.createProduct({
      categoryId: String(data.get("categoryId") || categories[0].id),
      taxCategoryId: String(data.get("taxCategoryId") || taxCategories[0].id),
      name: String(data.get("name")),
      sku: String(data.get("sku")),
      barcode: String(data.get("barcode") || ""),
      hsnSac: String(data.get("hsnSac") || ""),
      unitOfMeasure: String(data.get("unitOfMeasure") || "pcs"),
      price: Number(data.get("price")),
      currency: "INR",
      isStockTracked: true
    });
    event.currentTarget.reset();
    await refresh();
  }

  async function submitCustomer(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!client) return;
    const data = new FormData(event.currentTarget);
    await client.createCustomer({
      displayName: String(data.get("displayName")),
      phone: String(data.get("phone") || ""),
      email: String(data.get("email") || ""),
      gstin: String(data.get("gstin") || ""),
      creditLimit: Number(data.get("creditLimit") || 0)
    });
    event.currentTarget.reset();
    await refresh();
  }

  async function submitStock(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!client || !bootstrap || products.length === 0) return;
    const data = new FormData(event.currentTarget);
    await client.openingStock({
      locationId: bootstrap.locationId,
      warehouseId: bootstrap.warehouseId,
      productId: String(data.get("productId") || products[0].id),
      quantity: Number(data.get("quantity")),
      unitCost: Number(data.get("unitCost") || 0)
    });
    event.currentTarget.reset();
    await refresh();
  }

  return (
    <main className="shell">
      <aside className="sidebar">
        <div className="brand">Universal Commerce OS</div>
        <nav>
          {nav.map(([label, Icon]) => (
            <a href="#" className={label === "Dashboard" ? "active" : ""} key={label}>
              <Icon size={18} />
              <span>{label}</span>
            </a>
          ))}
        </nav>
      </aside>

      <section className="workspace">
        <header className="topbar">
          <div>
            <p className="eyebrow">Demo Retail Pvt Ltd</p>
            <h1>Kalyani Main Store</h1>
            <span className={error ? "sync-error" : "sync-status"}>{error ?? status}</span>
          </div>
          <div className="toolbar">
            <button className="secondary-link" onClick={() => refresh()} disabled={!client} title="Refresh API data">
              <RefreshCw size={18} />
              Refresh
            </button>
            <Link className="primary-link" href="/pos">
              <ShoppingCart size={18} />
              Open POS
            </Link>
          </div>
        </header>

        <section className="metrics">
          <Metric label="Today revenue" value={formatMoney(report?.revenue ?? 0)} />
          <Metric label="Transactions" value={String(report?.transactionCount ?? 0)} />
          <Metric label="Average order" value={formatMoney(report?.averageOrderValue ?? 0)} />
          <Metric label="Low stock" value={String(lowStock)} />
        </section>

        <section className="content-grid">
          <div className="panel wide">
            <div className="panel-title">
              <h2>Products</h2>
              <button className="icon-button" aria-label="Search products" title="Search products">
                <PackageSearch size={18} />
              </button>
            </div>
            <table>
              <thead>
                <tr>
                  <th>Product</th>
                  <th>SKU</th>
                  <th>Stock</th>
                  <th>Price</th>
                </tr>
              </thead>
              <tbody>
                {products.map((product) => (
                  <tr key={product.id}>
                    <td>{product.name}</td>
                    <td>{product.sku}</td>
                    <td>{stockByProduct.get(product.id) ?? 0}</td>
                    <td>{formatMoney(product.price)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="panel">
            <div className="panel-title">
              <h2>Customers</h2>
              <Users size={18} />
            </div>
            <div className="status-list">
              {customers.slice(0, 6).map((customer) => (
                <span key={customer.id}>{customer.displayName}</span>
              ))}
            </div>
          </div>
        </section>

        <section className="form-grid">
          <form className="panel form-panel" onSubmit={submitProduct}>
            <div className="panel-title">
              <h2>Add Product</h2>
              <PackagePlus size={18} />
            </div>
            <input name="name" placeholder="Name" required />
            <input name="sku" placeholder="SKU" required />
            <input name="barcode" placeholder="Barcode" />
            <input name="hsnSac" placeholder="HSN/SAC" />
            <select name="categoryId" required>
              {categories.map((category) => <option key={category.id} value={category.id}>{category.name}</option>)}
            </select>
            <select name="taxCategoryId" required>
              {taxCategories.map((tax) => <option key={tax.id} value={tax.id}>{tax.name}</option>)}
            </select>
            <input name="unitOfMeasure" placeholder="Unit" defaultValue="pcs" required />
            <input name="price" placeholder="Price" type="number" min="0" step="0.01" required />
            <button className="primary-link" type="submit" disabled={!client || categories.length === 0 || taxCategories.length === 0}>
              <Plus size={18} />
              Save
            </button>
          </form>

          <form className="panel form-panel" onSubmit={submitCustomer}>
            <div className="panel-title">
              <h2>Add Customer</h2>
              <UserPlus size={18} />
            </div>
            <input name="displayName" placeholder="Display name" required />
            <input name="phone" placeholder="Phone" />
            <input name="email" placeholder="Email" type="email" />
            <input name="gstin" placeholder="GSTIN" />
            <input name="creditLimit" placeholder="Credit limit" type="number" min="0" step="0.01" defaultValue="0" />
            <button className="primary-link" type="submit" disabled={!client}>
              <Plus size={18} />
              Save
            </button>
          </form>

          <form className="panel form-panel" onSubmit={submitStock}>
            <div className="panel-title">
              <h2>Add Stock</h2>
              <Boxes size={18} />
            </div>
            <select name="productId" required>
              {products.map((product) => <option key={product.id} value={product.id}>{product.name}</option>)}
            </select>
            <input name="quantity" placeholder="Quantity" type="number" min="0.001" step="0.001" required />
            <input name="unitCost" placeholder="Unit cost" type="number" min="0" step="0.01" defaultValue="0" />
            <button className="primary-link" type="submit" disabled={!client || !bootstrap || products.length === 0}>
              <Plus size={18} />
              Post
            </button>
          </form>
        </section>
      </section>
    </main>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="metric">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}
