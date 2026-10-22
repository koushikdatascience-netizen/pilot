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

export type PagedResponse<T> = { items: T[]; page: number; pageSize: number; totalCount: number };
export type AdminRole = { id: string; name: string; version: number; permissions: string[] };
export type AdminUser = { id: string; displayName: string; email: string; isActive: boolean; version: number; roleIds: string[] };
export type AuditEntry = { id: string; actorUserId: string | null; actorName: string | null; action: string; entityName: string; entityId: string; terminalId: string | null; occurredAtUtc: string; beforeJson: string | null; afterJson: string | null };
export type RefundAllocation = { method: string; amount: number; reference: string | null };
export type CorrectionLine = { saleLineId: string; quantity: number; restock: boolean; warehouseId: string | null; discountAmount: number; taxableAmount: number; cgstAmount: number; sgstAmount: number; igstAmount: number; total: number };
export type CorrectionQuote = { kind: string; total: number; lines: CorrectionLine[]; tenderAvailable: RefundAllocation[] };
export type Correction = { id: string; saleId: string; terminalId: string; clientGeneratedId: string; number: string; kind: string; reason: string; total: number; occurredAtUtc: string; lines: CorrectionLine[]; refunds: RefundAllocation[]; idempotentReplay: boolean };
export type CorrectionHistory = { effectiveStatus: string; corrections: Correction[] };
export type DrawerSession = { id: string; terminalId: string; openedAtUtc: string; closedAtUtc: string | null; openingFloat: number; expectedCash: number; countedCash: number | null; variance: number | null; closeNote: string | null; movements: { id: string; kind: string; sourceId: string; amountDelta: number; reason: string; actorUserId: string; occurredAtUtc: string }[] };
export type TerminalContextResponse = { tenantId: string; organizationId: string; businessId: string; locationId: string; locationName: string; terminalId: string; terminalName: string; warehouseId: string };
export type CategoryResponse = { id: string; name: string; parentCategoryId: string | null };
export type TaxCategoryResponse = { id: string; name: string; countryCode: string; rate: number; taxBehavior: "Inclusive" | "Exclusive" };
export type CustomerResponse = { id: string; displayName: string; phone: string | null; email: string | null; gstin: string | null; stateCode: string | null; creditLimit: number; isWalkIn: boolean };
export type StockBalanceResponse = { warehouseId: string; productId: string; productName: string; sku: string; quantityOnHand: number };
export type PaymentResponse = { id: string; method: string; amount: number; amountDue: number; amountTendered: number; changeDue: number; currency: string; status: string; reference: string | null };
export type ReceiptHeader = { businessName: string; legalName: string; locationName: string; address: string | null; gstin: string | null; terminalName: string; cashierName: string; customerName: string };
export type SaleLineResponse = { id: string; productId: string; productName: string; sku: string; barcode: string | null; quantity: number; unitPrice: number; discountAmount: number; taxableAmount: number; taxRate: number; taxAmount: number; cgstAmount: number; sgstAmount: number; igstAmount: number; lineTotal: number; hsnSac: string | null };
export type SaleResponse = { id: string; locationId: string; terminalId: string; customerId: string | null; invoiceNumber: string; status: string; currency: string; subtotal: number; discountTotal: number; taxTotal: number; grandTotal: number; clientGeneratedId: string; completedAtUtc: string | null; syncStatus: string; lines: SaleLineResponse[]; payments: PaymentResponse[]; idempotentReplay: boolean; receipt: ReceiptHeader | null };
export type TodayReportResponse = { revenue: number; grossRevenue: number; refundTotal: number; refundMix: { method: string; amount: number; count: number }[]; transactionCount: number; averageOrderValue: number; itemsSold: number; paymentMix: { method: string; amount: number; count: number }[]; topSellingProducts: { productId: string; productName: string; quantity: number; revenue: number }[]; lowStockProducts: { productId: string; productName: string; quantityOnHand: number }[] };

const baseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "";
export async function login(email?: string, password?: string): Promise<LoginResponse> {
  if (email === undefined) {
    const saved = sessionStorage.getItem("pilot-session");
    if (saved) {
      const session = JSON.parse(saved) as LoginResponse;
      const current = await request<Omit<LoginResponse, "accessToken">>("/api/v1/auth/me", {}, session.accessToken);
      const refreshed = { ...session, ...current };
      sessionStorage.setItem("pilot-session", JSON.stringify(refreshed));
      return refreshed;
    }
    window.location.assign("/login");
    throw new Error("Sign in to continue.");
  }
  const auth = await request<LoginResponse>("/api/v1/auth/login", {
    method: "POST",
    body: JSON.stringify({ email, password })
  });
  sessionStorage.setItem("pilot-session", JSON.stringify(auth));
  return auth;
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
    if (response.status === 401 && token && typeof window !== "undefined") {
      sessionStorage.removeItem("pilot-session");
      window.location.assign("/login");
    }
    const text = await response.text();
    let message = text || `Request failed with ${response.status}`;
    try {
      const problem = JSON.parse(text);
      message = problem.errors ? Object.values(problem.errors).flat().join(" ") : problem.detail || problem.title || message;
    } catch { /* Non-JSON error responses retain the server's text. */ }
    throw Object.assign(new Error(message), { status: response.status });
  }

  return response.json() as Promise<T>;
}

export function createClient(token: string) {
  return {
    corrections: (saleId: string) => request<CorrectionHistory>(`/api/v1/sales/${saleId}/corrections`, {}, token),
    correctionQuote: (saleId: string, operation: string, body: unknown) => request<CorrectionQuote>(`/api/v1/sales/${saleId}/${operation}/quote`, { method: "POST", body: JSON.stringify(body) }, token),
    recordCorrection: (saleId: string, operation: string, body: unknown) => request<Correction>(`/api/v1/sales/${saleId}/${operation}`, { method: "POST", body: JSON.stringify(body) }, token),
    drawers: (terminalId: string) => request<DrawerSession[]>(`/api/v1/cash-drawers/terminals/${terminalId}`, {}, token),
    openDrawer: (terminalId: string, body: unknown) => request<DrawerSession>(`/api/v1/cash-drawers/terminals/${terminalId}/open`, { method: "POST", body: JSON.stringify(body) }, token),
    closeDrawer: (id: string, body: unknown) => request<DrawerSession>(`/api/v1/cash-drawers/${id}/close`, { method: "POST", body: JSON.stringify(body) }, token),
    drawerMovement: (id: string, body: unknown) => request<DrawerSession>(`/api/v1/cash-drawers/${id}/movements`, { method: "POST", body: JSON.stringify(body) }, token),
    me: () => request<Omit<LoginResponse, "accessToken">>("/api/v1/auth/me", {}, token),
    adminPermissions: () => request<string[]>("/api/v1/administration/permissions", {}, token),
    adminRoles: () => request<AdminRole[]>("/api/v1/administration/roles", {}, token),
    adminUsers: (q: string, page: number) => request<PagedResponse<AdminUser>>(`/api/v1/administration/users?q=${encodeURIComponent(q)}&page=${page}`, {}, token),
    saveRole: (id: string | null, body: unknown) => request<AdminRole>(`/api/v1/administration/roles${id ? `/${id}` : ""}`, { method: id ? "PUT" : "POST", body: JSON.stringify(body) }, token),
    saveUser: (id: string | null, body: unknown) => request<AdminUser>(`/api/v1/administration/users${id ? `/${id}` : ""}`, { method: id ? "PUT" : "POST", body: JSON.stringify(body) }, token),
    resetPassword: (id: string, body: unknown) => request<AdminUser>(`/api/v1/administration/users/${id}/password`, { method: "POST", body: JSON.stringify(body) }, token),
    audit: (query: string) => request<PagedResponse<AuditEntry>>(`/api/v1/audit?${query}`, {}, token),
    bootstrap: () => request<BootstrapResponse>("/api/v1/tenancy/bootstrap", {}, token),
    terminals: () => request<TerminalContextResponse[]>("/api/v1/tenancy/terminals", {}, token),
    productPage: (q = "", page = 1, pageSize = 50, includeInactive = false) => request<PagedResponse<ProductResponse>>(`/api/v1/products?page=${page}&pageSize=${pageSize}&includeInactive=${includeInactive}${q ? `&q=${encodeURIComponent(q)}` : ""}`, {}, token),
    products: async (q = "") => (await request<PagedResponse<ProductResponse>>(`/api/v1/products?page=1&pageSize=50${q ? `&q=${encodeURIComponent(q)}` : ""}`, {}, token)).items,
    productByBarcode: async (barcode: string) => (await request<PagedResponse<ProductResponse>>(`/api/v1/products?barcode=${encodeURIComponent(barcode)}&pageSize=1`, {}, token)).items[0] ?? null,
    productBySku: async (sku: string) => (await request<PagedResponse<ProductResponse>>(`/api/v1/products?sku=${encodeURIComponent(sku)}&pageSize=1`, {}, token)).items[0] ?? null,
    categories: () => request<CategoryResponse[]>("/api/v1/categories", {}, token),
    taxCategories: () => request<TaxCategoryResponse[]>("/api/v1/tax-categories", {}, token),
    customers: (q = "") => request<CustomerResponse[]>(`/api/v1/customers?q=${encodeURIComponent(q)}`, {}, token),
    balances: () => request<StockBalanceResponse[]>("/api/v1/inventory/balances", {}, token),
    report: () => request<TodayReportResponse>("/api/v1/reports/today", {}, token),
    createProduct: (body: unknown) => request<ProductResponse>("/api/v1/products", { method: "POST", body: JSON.stringify(body) }, token),
    updateProduct: (id: string, body: unknown) => request<ProductResponse>(`/api/v1/products/${id}`, { method: "PUT", body: JSON.stringify(body) }, token),
    activateProduct: (id: string) => request<ProductResponse>(`/api/v1/products/${id}/activate`, { method: "POST" }, token),
    deactivateProduct: (id: string) => request<ProductResponse>(`/api/v1/products/${id}/deactivate`, { method: "POST" }, token),
    createCustomer: (body: unknown) => request<CustomerResponse>("/api/v1/customers", { method: "POST", body: JSON.stringify(body) }, token),
    openingStock: (body: unknown) => request<unknown>("/api/v1/inventory/opening-stock", { method: "POST", body: JSON.stringify(body) }, token),
    adjustStock: (body: unknown) => request<unknown>("/api/v1/inventory/adjustments", { method: "POST", body: JSON.stringify(body) }, token),
    completeSale: (body: unknown) => request<SaleResponse>("/api/v1/sales/complete", { method: "POST", body: JSON.stringify(body) }, token),
    holdSale: (body: unknown) => request<SaleResponse>("/api/v1/sales/hold", { method: "POST", body: JSON.stringify(body) }, token),
    updateHeldSale: (id: string, body: unknown) => request<SaleResponse>(`/api/v1/sales/${id}/cart`, { method: "PUT", body: JSON.stringify(body) }, token),
    completeHeldSale: (id: string, body: unknown) => request<SaleResponse>(`/api/v1/sales/${id}/complete-held`, { method: "POST", body: JSON.stringify(body) }, token),
    sales: (status = "") => request<SaleResponse[]>(`/api/v1/sales${status ? `?status=${encodeURIComponent(status)}` : ""}`, {}, token),
    saleHistory: (query: string) => request<PagedResponse<SaleResponse>>(`/api/v1/sales/history?${query}`, {}, token)
  };
}
