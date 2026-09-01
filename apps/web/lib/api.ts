export type LoginResponse = {
  accessToken: string;
  tenantId: string;
  userId: string;
  displayName: string;
  email: string;
  permissions: string[];
};

export type BootstrapResponse = {
  tenantId: string;
  tenantName: string;
  organizationId: string;
  businessId: string;
  locationId: string;
  terminalId: string;
  warehouseId: string;
};

export type ProductResponse = {
  id: string;
  categoryId: string;
  taxCategoryId: string;
  name: string;
  sku: string;
  barcode: string | null;
  hsnSac: string | null;
  unitOfMeasure: string;
  price: number;
  currency: string;
  isStockTracked: boolean;
  isActive: boolean;
};

export type CategoryResponse = { id: string; name: string; parentCategoryId: string | null };
export type TaxCategoryResponse = { id: string; name: string; countryCode: string; rate: number; taxBehavior: "Inclusive" | "Exclusive" };
export type CustomerResponse = { id: string; displayName: string; phone: string | null; email: string | null; gstin: string | null; creditLimit: number; isWalkIn: boolean };
export type StockBalanceResponse = { warehouseId: string; productId: string; productName: string; sku: string; quantityOnHand: number };
export type TodayReportResponse = { revenue: number; transactionCount: number; averageOrderValue: number; topSellingProducts: unknown[]; lowStockProducts: unknown[] };
export type SaleResponse = { id: string; invoiceNumber: string; grandTotal: number; taxTotal: number; idempotentReplay: boolean };

const baseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5088";
const demoEmail = process.env.NEXT_PUBLIC_DEMO_EMAIL ?? "admin@example.test";
const demoPassword = process.env.NEXT_PUBLIC_DEMO_PASSWORD ?? "ChangeMe123!";

export async function login(): Promise<LoginResponse> {
  return request<LoginResponse>("/api/v1/auth/login", {
    method: "POST",
    body: JSON.stringify({ email: demoEmail, password: demoPassword })
  });
}

export async function request<T>(path: string, init: RequestInit = {}, token?: string): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...init.headers
    },
    cache: "no-store"
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `Request failed with ${response.status}`);
  }

  return response.json() as Promise<T>;
}

export function createClient(token: string) {
  return {
    bootstrap: () => request<BootstrapResponse>("/api/v1/tenancy/bootstrap", {}, token),
    products: (q = "") => request<ProductResponse[]>(`/api/v1/products${q ? `?q=${encodeURIComponent(q)}` : ""}`, {}, token),
    categories: () => request<CategoryResponse[]>("/api/v1/categories", {}, token),
    taxCategories: () => request<TaxCategoryResponse[]>("/api/v1/tax-categories", {}, token),
    customers: () => request<CustomerResponse[]>("/api/v1/customers", {}, token),
    balances: () => request<StockBalanceResponse[]>("/api/v1/inventory/balances", {}, token),
    report: () => request<TodayReportResponse>("/api/v1/reports/today", {}, token),
    createProduct: (body: unknown) => request<ProductResponse>("/api/v1/products", { method: "POST", body: JSON.stringify(body) }, token),
    createCustomer: (body: unknown) => request<CustomerResponse>("/api/v1/customers", { method: "POST", body: JSON.stringify(body) }, token),
    openingStock: (body: unknown) => request<unknown>("/api/v1/inventory/opening-stock", { method: "POST", body: JSON.stringify(body) }, token),
    completeSale: (body: unknown) => request<SaleResponse>("/api/v1/sales/complete", { method: "POST", body: JSON.stringify(body) }, token)
  };
}
