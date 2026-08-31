import { BarChart3, Boxes, CreditCard, PackageSearch, Settings, ShoppingCart, Users } from "lucide-react";
import Link from "next/link";

const products = [
  { name: "Rice 5kg", sku: "RICE-5KG", stock: 50, price: 349 },
  { name: "Milk 1L", sku: "MILK-1L", stock: 80, price: 68 },
  { name: "Premium T-Shirt", sku: "TSHIRT-PREM", stock: 25, price: 799 },
  { name: "Shampoo 180ml", sku: "SHAMPOO-180", stock: 50, price: 185 }
];

const nav = [
  ["Dashboard", BarChart3],
  ["Sales", ShoppingCart],
  ["Products", PackageSearch],
  ["Inventory", Boxes],
  ["Customers", Users],
  ["Payments", CreditCard],
  ["Settings", Settings]
] as const;

export default function Dashboard() {
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
          </div>
          <Link className="primary-link" href="/pos">
            <ShoppingCart size={18} />
            Open POS
          </Link>
        </header>

        <section className="metrics">
          <Metric label="Today revenue" value="Rs 0.00" />
          <Metric label="Transactions" value="0" />
          <Metric label="Average order" value="Rs 0.00" />
          <Metric label="Low stock" value="0" />
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
                  <tr key={product.sku}>
                    <td>{product.name}</td>
                    <td>{product.sku}</td>
                    <td>{product.stock}</td>
                    <td>Rs {product.price.toFixed(2)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="panel">
            <div className="panel-title">
              <h2>Phase 1 Status</h2>
            </div>
            <div className="status-list">
              <span>Tenant hierarchy seeded</span>
              <span>GST tax foundation ready</span>
              <span>Inventory ledger active</span>
              <span>POS shell available</span>
            </div>
          </div>
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
