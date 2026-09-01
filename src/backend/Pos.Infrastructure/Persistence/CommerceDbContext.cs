using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Pos.Infrastructure.Persistence;

public sealed class CommerceDbContext : DbContext
{
    public CommerceDbContext(DbContextOptions<CommerceDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Terminal> Terminals => Set<Terminal>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<TaxCategory> TaxCategories => Set<TaxCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<InvoiceSequence> InvoiceSequences => Set<InvoiceSequence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        ConfigureTenantEntity<Organization>(modelBuilder, "organizations");
        ConfigureTenantEntity<Business>(modelBuilder, "businesses");
        ConfigureTenantEntity<Location>(modelBuilder, "locations");
        ConfigureTenantEntity<Terminal>(modelBuilder, "terminals");
        ConfigureTenantEntity<Warehouse>(modelBuilder, "warehouses");
        ConfigureTenantEntity<Role>(modelBuilder, "roles");
        ConfigureTenantEntity<User>(modelBuilder, "users");
        ConfigureTenantEntity<Category>(modelBuilder, "categories");
        ConfigureTenantEntity<TaxCategory>(modelBuilder, "tax_categories");
        ConfigureTenantEntity<Product>(modelBuilder, "products");
        ConfigureTenantEntity<Customer>(modelBuilder, "customers");
        ConfigureTenantEntity<Sale>(modelBuilder, "sales");
        ConfigureTenantEntity<SaleLine>(modelBuilder, "sale_lines");
        ConfigureTenantEntity<Payment>(modelBuilder, "payments");
        ConfigureTenantEntity<InventoryTransaction>(modelBuilder, "inventory_transactions");
        ConfigureTenantEntity<AuditLog>(modelBuilder, "audit_logs");
        ConfigureTenantEntity<OutboxEvent>(modelBuilder, "outbox_events");

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.LegalName).HasMaxLength(220).IsRequired();
            entity.Property(x => x.Gstin).HasMaxLength(15);
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Business>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.BusinessType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.OrganizationId, x.Name }).IsUnique();
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.StateCode).HasMaxLength(8).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(500);
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            entity.HasIndex(x => x.DefaultSalesWarehouseId);
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Terminal>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.DeviceCode).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.DeviceCode }).IsUnique();
            entity.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            entity.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(x => new { x.RoleId, x.Permission });
            entity.Property(x => x.Permission).HasConversion<string>().HasMaxLength(80).IsRequired();
            entity.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(240).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("user_roles");
            entity.HasKey(x => new { x.UserId, x.RoleId });
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.Name });
            entity.HasOne<Category>().WithMany().HasForeignKey(x => x.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TaxCategory>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
            entity.Property(x => x.Rate).HasPrecision(9, 6);
            entity.Property(x => x.TaxBehavior).HasConversion<string>().HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(240).IsRequired();
            entity.Property(x => x.Sku).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Barcode).HasMaxLength(80);
            entity.Property(x => x.HsnSac).HasMaxLength(16);
            entity.Property(x => x.UnitOfMeasure).HasMaxLength(24).IsRequired();
            entity.Property(x => x.PriceAmount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Barcode }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Name, x.Sku });
            entity.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TaxCategory>().WithMany().HasForeignKey(x => x.TaxCategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(32);
            entity.Property(x => x.Email).HasMaxLength(240);
            entity.Property(x => x.Gstin).HasMaxLength(15);
            entity.Property(x => x.CreditLimit).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.TenantId, x.Phone });
        });

        modelBuilder.Entity<Sale>(entity =>
        {
            entity.Property(x => x.InvoiceNumber).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.SyncStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Subtotal).HasPrecision(18, 2);
            entity.Property(x => x.DiscountTotal).HasPrecision(18, 2);
            entity.Property(x => x.TaxTotal).HasPrecision(18, 2);
            entity.Property(x => x.GrandTotal).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.TenantId, x.TerminalId, x.ClientGeneratedId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.LocationId, x.InvoiceNumber }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.LocationId, x.CompletedAtUtc });
            entity.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Terminal>().WithMany().HasForeignKey(x => x.TerminalId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SaleLine>(entity =>
        {
            entity.Property(x => x.ProductNameSnapshot).HasMaxLength(240).IsRequired();
            entity.Property(x => x.SkuSnapshot).HasMaxLength(80).IsRequired();
            entity.Property(x => x.BarcodeSnapshot).HasMaxLength(80);
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            entity.Property(x => x.TaxableAmount).HasPrecision(18, 2);
            entity.Property(x => x.TaxRate).HasPrecision(9, 6);
            entity.Property(x => x.TaxAmount).HasPrecision(18, 2);
            entity.Property(x => x.CgstAmount).HasPrecision(18, 2);
            entity.Property(x => x.SgstAmount).HasPrecision(18, 2);
            entity.Property(x => x.IgstAmount).HasPrecision(18, 2);
            entity.Property(x => x.LineTotal).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.TenantId, x.SaleId });
            entity.HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.Property(x => x.Method).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Provider).HasMaxLength(80);
            entity.Property(x => x.Reference).HasMaxLength(160);
            entity.HasOne<Sale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InventoryTransaction>(entity =>
        {
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(48).IsRequired();
            entity.Property(x => x.QuantityDelta).HasPrecision(18, 3);
            entity.Property(x => x.UnitCost).HasPrecision(18, 2);
            entity.Property(x => x.SourceDocumentType).HasMaxLength(80);
            entity.HasIndex(x => new { x.TenantId, x.WarehouseId, x.ProductId });
            entity.HasIndex(x => new { x.TenantId, x.SourceDocumentType, x.SourceDocumentId });
            entity.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.Property(x => x.Action).HasMaxLength(120).IsRequired();
            entity.Property(x => x.EntityName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.BeforeJson).HasColumnType("jsonb");
            entity.Property(x => x.AfterJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId });
        });

        modelBuilder.Entity<OutboxEvent>(entity =>
        {
            entity.Property(x => x.EventType).HasMaxLength(160).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.ProcessedAtUtc, x.OccurredAtUtc });
        });

        modelBuilder.Entity<InvoiceSequence>(entity =>
        {
            entity.ToTable("invoice_sequences");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FinancialPeriod).HasMaxLength(16).IsRequired();
            entity.Property(x => x.Prefix).HasMaxLength(40).IsRequired();
            entity.Property(x => x.xmin).IsRowVersion();
            entity.HasIndex(x => new { x.TenantId, x.LocationId, x.FinancialPeriod, x.Prefix }).IsUnique();
            entity.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override int SaveChanges()
    {
        TouchEntities();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        TouchEntities();
        return base.SaveChangesAsync(cancellationToken);
    }

    private static void ConfigureTenantEntity<TEntity>(ModelBuilder modelBuilder, string tableName)
        where TEntity : TenantEntity
    {
        modelBuilder.Entity<TEntity>(entity =>
        {
            entity.ToTable(tableName);
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private void TouchEntities()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<TenantEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(x => x.CreatedAtUtc).CurrentValue = now;
                entry.Property(x => x.UpdatedAtUtc).CurrentValue = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(x => x.UpdatedAtUtc).CurrentValue = now;
                entry.Property(x => x.Version).CurrentValue += 1;
            }
        }
    }
}
