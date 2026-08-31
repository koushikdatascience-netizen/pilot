create extension if not exists "uuid-ossp";

create table tenants (
    id uuid primary key,
    name text not null,
    status text not null,
    created_at_utc timestamptz not null
);

create table organizations (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    name text not null,
    legal_name text not null,
    gstin text null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    version bigint not null default 1
);

create table locations (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    organization_id uuid not null references organizations(id),
    name text not null,
    code text not null,
    state_code text not null,
    address text null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    version bigint not null default 1,
    unique (tenant_id, code)
);

create table terminals (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    location_id uuid not null references locations(id),
    name text not null,
    device_code text not null,
    status text not null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    version bigint not null default 1,
    unique (tenant_id, device_code)
);

create table warehouses (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    location_id uuid not null references locations(id),
    name text not null,
    code text not null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    version bigint not null default 1,
    unique (tenant_id, code)
);

create table categories (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    name text not null,
    parent_category_id uuid null references categories(id),
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    version bigint not null default 1
);

create table tax_categories (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    name text not null,
    country_code text not null,
    rate numeric(9,6) not null,
    tax_behavior text not null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    version bigint not null default 1
);

create table products (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    category_id uuid not null references categories(id),
    tax_category_id uuid not null references tax_categories(id),
    name text not null,
    sku text not null,
    barcode text null,
    hsn_sac text null,
    unit_of_measure text not null,
    price numeric(18,2) not null,
    currency char(3) not null,
    is_stock_tracked boolean not null,
    is_active boolean not null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    version bigint not null default 1,
    unique (tenant_id, sku),
    unique (tenant_id, barcode)
);

create table customers (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    display_name text not null,
    phone text null,
    email text null,
    gstin text null,
    credit_limit numeric(18,2) not null default 0,
    is_walk_in boolean not null default false,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    version bigint not null default 1
);

create table sales (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    location_id uuid not null references locations(id),
    terminal_id uuid not null references terminals(id),
    customer_id uuid null references customers(id),
    invoice_number text not null,
    status text not null,
    currency char(3) not null,
    subtotal numeric(18,2) not null,
    discount_total numeric(18,2) not null,
    tax_total numeric(18,2) not null,
    grand_total numeric(18,2) not null,
    client_generated_id uuid not null,
    completed_at_utc timestamptz null,
    sync_status text not null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    version bigint not null default 1,
    unique (tenant_id, location_id, invoice_number),
    unique (tenant_id, terminal_id, client_generated_id)
);

create table sale_lines (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    sale_id uuid not null references sales(id),
    product_id uuid not null references products(id),
    product_name_snapshot text not null,
    sku_snapshot text not null,
    barcode_snapshot text null,
    quantity numeric(18,3) not null,
    unit_price numeric(18,2) not null,
    discount_amount numeric(18,2) not null,
    tax_rate numeric(9,6) not null,
    tax_amount numeric(18,2) not null,
    line_total numeric(18,2) not null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    version bigint not null default 1
);

create table payments (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    sale_id uuid not null references sales(id),
    method text not null,
    amount numeric(18,2) not null,
    currency char(3) not null,
    status text not null,
    provider text null,
    reference text null,
    captured_at_utc timestamptz null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    version bigint not null default 1
);

create table inventory_transactions (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    location_id uuid not null references locations(id),
    warehouse_id uuid not null references warehouses(id),
    product_id uuid not null references products(id),
    type text not null,
    quantity_delta numeric(18,3) not null,
    unit_cost numeric(18,2) null,
    source_document_type text null,
    source_document_id uuid null,
    occurred_at_utc timestamptz not null,
    created_by_user_id uuid null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    version bigint not null default 1
);

create table outbox_events (
    id uuid primary key,
    tenant_id uuid not null references tenants(id),
    event_type text not null,
    payload_json jsonb not null,
    occurred_at_utc timestamptz not null,
    processed_at_utc timestamptz null,
    attempts int not null default 0,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    version bigint not null default 1
);

create index idx_products_tenant_search on products (tenant_id, name, sku);
create index idx_customers_tenant_phone on customers (tenant_id, phone);
create index idx_sales_tenant_location_completed on sales (tenant_id, location_id, completed_at_utc);
create index idx_inventory_lookup on inventory_transactions (tenant_id, warehouse_id, product_id);
create index idx_outbox_pending on outbox_events (processed_at_utc, occurred_at_utc);
