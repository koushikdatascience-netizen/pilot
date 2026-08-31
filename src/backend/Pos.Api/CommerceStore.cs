using Pos.Domain.Catalog;
using Pos.Domain.Common;
using Pos.Domain.Customers;
using Pos.Domain.Identity;
using Pos.Domain.Inventory;
using Pos.Domain.Operations;
using Pos.Domain.Sales;
using Pos.Domain.Tenancy;

namespace Pos.Api;

public sealed class CommerceStore
{
    private int _invoiceSequence = 1;

    public CommerceStore()
    {
        var seed = DemoSeed.Create();
        Tenant = seed.Tenant;
        Organization = seed.Organization;
        Business = seed.Business;
        Location = seed.Location;
        Terminal = seed.Terminal;
        Warehouse = seed.Warehouse;
        Roles = seed.Roles;
        Users = seed.Users;
        Categories = seed.Categories;
        TaxCategories = seed.TaxCategories;
        Products = seed.Products;
        Customers = seed.Customers;
        InventoryTransactions = seed.InventoryTransactions;
    }

    public Tenant Tenant { get; }
    public Organization Organization { get; }
    public Business Business { get; }
    public Location Location { get; }
    public Terminal Terminal { get; }
    public Warehouse Warehouse { get; }
    public List<Role> Roles { get; }
    public List<User> Users { get; }
    public List<Category> Categories { get; }
    public List<TaxCategory> TaxCategories { get; }
    public List<Product> Products { get; }
    public List<Customer> Customers { get; }
    public List<InventoryTransaction> InventoryTransactions { get; }
    public List<Sale> Sales { get; } = [];
    public List<OutboxEvent> OutboxEvents { get; } = [];

    public string NextInvoiceNumber() => $"KAL-{DateTimeOffset.UtcNow:yyyyMMdd}-{_invoiceSequence++:0000}";
}

public sealed record DemoSeed(
    Tenant Tenant,
    Organization Organization,
    Business Business,
    Location Location,
    Terminal Terminal,
    Warehouse Warehouse,
    List<Role> Roles,
    List<User> Users,
    List<Category> Categories,
    List<TaxCategory> TaxCategories,
    List<Product> Products,
    List<Customer> Customers,
    List<InventoryTransaction> InventoryTransactions)
{
    public static DemoSeed Create()
    {
        var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var orgId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var businessId = Guid.Parse("23232323-2323-2323-2323-232323232323");
        var locationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var terminalId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var warehouseId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var adminRoleId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var cashierRoleId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var now = DateTimeOffset.UtcNow;

        var tenant = new Tenant(tenantId, "Demo Tenant", TenantStatus.Active, now);
        var organization = new Organization { Id = orgId, TenantId = tenantId, Name = "Demo Retail", LegalName = "Demo Retail Pvt Ltd", Gstin = "19ABCDE1234F1Z5" };
        var business = new Business { Id = businessId, TenantId = tenantId, OrganizationId = orgId, Name = "Demo Retail", BusinessType = BusinessType.Retail };
        var location = new Location { Id = locationId, TenantId = tenantId, OrganizationId = orgId, Name = "Kalyani Main Store", Code = "KAL-01", StateCode = "WB", Address = "Kalyani, West Bengal" };
        var terminal = new Terminal { Id = terminalId, TenantId = tenantId, LocationId = locationId, Name = "Front Counter 1", DeviceCode = "KAL-POS-01", Status = TerminalStatus.Active };
        var warehouse = new Warehouse { Id = warehouseId, TenantId = tenantId, LocationId = locationId, Name = "Main Store Warehouse", Code = "KAL-MAIN" };

        var roles = new List<Role>
        {
            new() { Id = adminRoleId, TenantId = tenantId, Name = "Admin", Permissions = Enum.GetValues<Permission>().ToHashSet() },
            new() { Id = cashierRoleId, TenantId = tenantId, Name = "Cashier", Permissions = new HashSet<Permission> { Permission.CompleteSale, Permission.ApplyDiscount } }
        };

        var users = new List<User>
        {
            new() { Id = Guid.Parse("88888888-8888-8888-8888-888888888888"), TenantId = tenantId, DisplayName = "Demo Admin", Email = "admin@example.test", PasswordHash = "DEV-ONLY-NOT-A-REAL-HASH", RoleIds = new HashSet<Guid> { adminRoleId } },
            new() { Id = Guid.Parse("99999999-9999-9999-9999-999999999999"), TenantId = tenantId, DisplayName = "Demo Cashier", Email = "cashier@example.test", PasswordHash = "DEV-ONLY-NOT-A-REAL-HASH", RoleIds = new HashSet<Guid> { cashierRoleId } }
        };

        var groceryCategory = new Category { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), TenantId = tenantId, Name = "Grocery" };
        var apparelCategory = new Category { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), TenantId = tenantId, Name = "Apparel" };
        var personalCareCategory = new Category { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3"), TenantId = tenantId, Name = "Personal Care" };
        var categories = new List<Category> { groceryCategory, apparelCategory, personalCareCategory };

        var gst5 = new TaxCategory { Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb5"), TenantId = tenantId, Name = "GST 5%", CountryCode = "IN", Rate = 0.05m, TaxBehavior = TaxBehavior.Inclusive };
        var gst12 = new TaxCategory { Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbc12"), TenantId = tenantId, Name = "GST 12%", CountryCode = "IN", Rate = 0.12m, TaxBehavior = TaxBehavior.Inclusive };
        var gst18 = new TaxCategory { Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbc18"), TenantId = tenantId, Name = "GST 18%", CountryCode = "IN", Rate = 0.18m, TaxBehavior = TaxBehavior.Inclusive };
        var taxes = new List<TaxCategory> { gst5, gst12, gst18 };

        var products = new List<Product>
        {
            Product(tenantId, groceryCategory.Id, gst5.Id, "Rice 5kg", "RICE-5KG", "8901000000011", "1006", "bag", 349),
            Product(tenantId, groceryCategory.Id, gst5.Id, "Milk 1L", "MILK-1L", "8901000000028", "0401", "pc", 68),
            Product(tenantId, apparelCategory.Id, gst12.Id, "Premium T-Shirt", "TSHIRT-PREM", "8901000000035", "6109", "pc", 799),
            Product(tenantId, personalCareCategory.Id, gst18.Id, "Shampoo 180ml", "SHAMPOO-180", "8901000000042", "3305", "pc", 185)
        };

        var customers = new List<Customer>
        {
            new() { Id = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1"), TenantId = tenantId, DisplayName = "Walk-in Customer", IsWalkIn = true },
            new() { Id = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc2"), TenantId = tenantId, DisplayName = "Ananya Sen", Phone = "+919830000001", Email = "ananya@example.test", Gstin = null, CreditLimit = 5000 }
        };

        var inventory = products.Select(product => new InventoryTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LocationId = locationId,
            WarehouseId = warehouseId,
            ProductId = product.Id,
            Type = InventoryTransactionType.OpeningStock,
            QuantityDelta = product.Sku switch
            {
                "MILK-1L" => 80,
                "TSHIRT-PREM" => 25,
                _ => 50
            },
            UnitCost = Money.Round(product.Price.Amount * 0.7m),
            SourceDocumentType = "DevelopmentSeed",
            SourceDocumentId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            OccurredAtUtc = now
        }).ToList();

        return new DemoSeed(tenant, organization, business, location, terminal, warehouse, roles, users, categories, taxes, products, customers, inventory);
    }

    private static Product Product(Guid tenantId, Guid categoryId, Guid taxCategoryId, string name, string sku, string barcode, string hsn, string uom, decimal price)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CategoryId = categoryId,
            TaxCategoryId = taxCategoryId,
            Name = name,
            Sku = sku,
            Barcode = barcode,
            HsnSac = hsn,
            UnitOfMeasure = uom,
            Price = Money.Inr(price),
            IsStockTracked = true,
            IsActive = true
        };
    }
}
