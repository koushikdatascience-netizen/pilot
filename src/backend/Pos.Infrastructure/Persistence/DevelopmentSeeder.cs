using Microsoft.EntityFrameworkCore;
using Pos.Domain.Catalog;
using Pos.Domain.Identity;
using Pos.Domain.Inventory;
using Pos.Domain.Security;
using Pos.Domain.Tenancy;

namespace Pos.Infrastructure.Persistence;

public sealed class DevelopmentSeeder
{
    public static readonly Guid DemoTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid DemoOrganizationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid DemoBusinessId = Guid.Parse("23232323-2323-2323-2323-232323232323");
    public static readonly Guid DemoLocationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid DemoTerminalId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid DemoWarehouseId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid DemoAdminUserId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    public static readonly Guid DemoCashierUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly CommerceDbContext _db;
    private readonly IPasswordHasher _passwordHasher;

    public DevelopmentSeeder(CommerceDbContext db, IPasswordHasher passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (!await _db.Tenants.AnyAsync(t => t.Id == DemoTenantId, cancellationToken))
        {
            _db.Tenants.Add(new Tenant { Id = DemoTenantId, Name = "Demo Tenant", Status = TenantStatus.Active });
        }

        if (!await _db.Organizations.AnyAsync(o => o.Id == DemoOrganizationId, cancellationToken))
        {
            _db.Organizations.Add(new Organization { Id = DemoOrganizationId, TenantId = DemoTenantId, Name = "Demo Retail", LegalName = "Demo Retail Pvt Ltd", Gstin = "19ABCDE1234F1Z5" });
        }

        if (!await _db.Businesses.AnyAsync(b => b.Id == DemoBusinessId, cancellationToken))
        {
            _db.Businesses.Add(new Business { Id = DemoBusinessId, TenantId = DemoTenantId, OrganizationId = DemoOrganizationId, Name = "Demo Retail", BusinessType = BusinessType.Retail });
        }

        if (!await _db.Warehouses.AnyAsync(w => w.Id == DemoWarehouseId, cancellationToken))
        {
            _db.Warehouses.Add(new Warehouse { Id = DemoWarehouseId, TenantId = DemoTenantId, LocationId = DemoLocationId, Name = "Main Store Warehouse", Code = "KAL-MAIN" });
        }

        if (!await _db.Locations.AnyAsync(l => l.Id == DemoLocationId, cancellationToken))
        {
            _db.Locations.Add(new Location { Id = DemoLocationId, TenantId = DemoTenantId, OrganizationId = DemoOrganizationId, BusinessId = DemoBusinessId, Name = "Kalyani Main Store", Code = "KAL-01", StateCode = "19", Address = "Kalyani, West Bengal", DefaultSalesWarehouseId = DemoWarehouseId, CashierMaxDiscountPercent = 0.10m, ManagerMaxDiscountPercent = 1.00m });
        }

        if (!await _db.Terminals.AnyAsync(t => t.Id == DemoTerminalId, cancellationToken))
        {
            _db.Terminals.Add(new Terminal { Id = DemoTerminalId, TenantId = DemoTenantId, LocationId = DemoLocationId, Name = "Front Counter 1", DeviceCode = "KAL-POS-01", Status = TerminalStatus.Active });
        }

        await SeedIdentityAsync(cancellationToken);
        var categories = await SeedCategoriesAsync(cancellationToken);
        var taxes = await SeedTaxesAsync(cancellationToken);
        var products = await SeedProductsAsync(categories, taxes, cancellationToken);
        await SeedCustomersAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await SeedInventoryAsync(products, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedIdentityAsync(CancellationToken cancellationToken)
    {
        var adminRoleId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var cashierRoleId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        if (!await _db.Roles.AnyAsync(r => r.Id == adminRoleId, cancellationToken))
        {
            _db.Roles.Add(new Role { Id = adminRoleId, TenantId = DemoTenantId, Name = "Admin" });
            foreach (var permission in Enum.GetValues<Permission>())
            {
                _db.RolePermissions.Add(new RolePermission { RoleId = adminRoleId, Permission = permission });
            }
        }

        if (!await _db.Roles.AnyAsync(r => r.Id == cashierRoleId, cancellationToken))
        {
            _db.Roles.Add(new Role { Id = cashierRoleId, TenantId = DemoTenantId, Name = "Cashier" });
            foreach (var permission in new[] { Permission.CompleteSale })
            {
                _db.RolePermissions.Add(new RolePermission { RoleId = cashierRoleId, Permission = permission });
            }
        }

        var password = Environment.GetEnvironmentVariable("DEMO_ADMIN_PASSWORD") ?? "ChangeMe123!";
        if (!await _db.Users.AnyAsync(u => u.Id == DemoAdminUserId, cancellationToken))
        {
            _db.Users.Add(new User { Id = DemoAdminUserId, TenantId = DemoTenantId, DisplayName = "Demo Admin", Email = "admin@example.test", PasswordHash = _passwordHasher.Hash(password), IsActive = true });
            _db.UserRoles.Add(new UserRole { UserId = DemoAdminUserId, RoleId = adminRoleId });
        }

        if (!await _db.Users.AnyAsync(u => u.Id == DemoCashierUserId, cancellationToken))
        {
            _db.Users.Add(new User { Id = DemoCashierUserId, TenantId = DemoTenantId, DisplayName = "Demo Cashier", Email = "cashier@example.test", PasswordHash = _passwordHasher.Hash(password), IsActive = true });
            _db.UserRoles.Add(new UserRole { UserId = DemoCashierUserId, RoleId = cashierRoleId });
        }
    }

    private async Task<Dictionary<string, Guid>> SeedCategoriesAsync(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, Guid>
        {
            ["Grocery"] = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"),
            ["Apparel"] = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"),
            ["Personal Care"] = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3")
        };

        foreach (var item in values)
        {
            if (!await _db.Categories.AnyAsync(c => c.Id == item.Value, cancellationToken))
            {
                _db.Categories.Add(new Category { Id = item.Value, TenantId = DemoTenantId, Name = item.Key });
            }
        }

        return values;
    }

    private async Task<Dictionary<string, Guid>> SeedTaxesAsync(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, (Guid Id, decimal Rate)>
        {
            ["GST 5%"] = (Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb5"), 0.05m),
            ["GST 12%"] = (Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbc12"), 0.12m),
            ["GST 18%"] = (Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbc18"), 0.18m)
        };

        foreach (var item in values)
        {
            if (!await _db.TaxCategories.AnyAsync(t => t.Id == item.Value.Id, cancellationToken))
            {
                _db.TaxCategories.Add(new TaxCategory { Id = item.Value.Id, TenantId = DemoTenantId, Name = item.Key, CountryCode = "IN", Rate = item.Value.Rate, TaxBehavior = TaxBehavior.Inclusive });
            }
        }

        return values.ToDictionary(x => x.Key, x => x.Value.Id);
    }

    private async Task<IReadOnlyList<Product>> SeedProductsAsync(Dictionary<string, Guid> categories, Dictionary<string, Guid> taxes, CancellationToken cancellationToken)
    {
        var products = new[]
        {
            Product(categories["Grocery"], taxes["GST 5%"], "Rice 5kg", "RICE-5KG", "8901000000011", "1006", "bag", 349m),
            Product(categories["Grocery"], taxes["GST 5%"], "Milk 1L", "MILK-1L", "8901000000028", "0401", "pc", 68m),
            Product(categories["Apparel"], taxes["GST 12%"], "Premium T-Shirt", "TSHIRT-PREM", "8901000000035", "6109", "pc", 799m),
            Product(categories["Personal Care"], taxes["GST 18%"], "Shampoo 180ml", "SHAMPOO-180", "8901000000042", "3305", "pc", 185m)
        };

        foreach (var product in products)
        {
            if (!await _db.Products.AnyAsync(p => p.TenantId == DemoTenantId && p.Sku == product.Sku, cancellationToken))
            {
                _db.Products.Add(product);
            }
        }

        return products;
    }

    private async Task SeedCustomersAsync(CancellationToken cancellationToken)
    {
        if (!await _db.Customers.AnyAsync(c => c.Id == Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1"), cancellationToken))
        {
            _db.Customers.Add(new Customer { Id = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1"), TenantId = DemoTenantId, DisplayName = "Walk-in Customer", StateCode = "19", IsWalkIn = true });
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc2"), cancellationToken))
        {
            _db.Customers.Add(new Customer { Id = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc2"), TenantId = DemoTenantId, DisplayName = "Ananya Sen", Phone = "+919830000001", Email = "ananya@example.test", StateCode = "19", CreditLimit = 5000 });
        }
    }

    private async Task SeedInventoryAsync(IReadOnlyList<Product> seedProducts, CancellationToken cancellationToken)
    {
        foreach (var product in seedProducts)
        {
            var persistedProduct = await _db.Products.SingleAsync(p => p.TenantId == DemoTenantId && p.Sku == product.Sku, cancellationToken);
            var exists = await _db.InventoryTransactions.AnyAsync(t => t.TenantId == DemoTenantId && t.ProductId == persistedProduct.Id && t.SourceDocumentType == "DevelopmentSeed", cancellationToken);
            if (exists) continue;

            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = DemoTenantId,
                LocationId = DemoLocationId,
                WarehouseId = DemoWarehouseId,
                ProductId = persistedProduct.Id,
                Type = InventoryTransactionType.OpeningStock,
                QuantityDelta = persistedProduct.Sku switch
                {
                    "MILK-1L" => 80,
                    "TSHIRT-PREM" => 25,
                    _ => 50
                },
                UnitCost = decimal.Round(persistedProduct.PriceAmount * 0.7m, 2, MidpointRounding.AwayFromZero),
                SourceDocumentType = "DevelopmentSeed",
                SourceDocumentId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                OccurredAtUtc = DateTimeOffset.UtcNow,
                CreatedByUserId = DemoAdminUserId
            });
        }
    }

    private static Product Product(Guid categoryId, Guid taxCategoryId, string name, string sku, string barcode, string hsn, string uom, decimal price)
    {
        return new Product
        {
            Id = Guid.NewGuid(),
            TenantId = DemoTenantId,
            CategoryId = categoryId,
            TaxCategoryId = taxCategoryId,
            Name = name,
            Sku = sku,
            Barcode = barcode,
            HsnSac = hsn,
            UnitOfMeasure = uom,
            PriceAmount = price,
            Currency = "INR",
            IsStockTracked = true,
            IsActive = true
        };
    }
}
