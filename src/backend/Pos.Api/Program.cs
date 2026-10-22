using System.Text.Json.Serialization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Api.Security;
using Pos.Application.Auth;
using Pos.Application.Administration;
using Pos.Application.Catalog;
using Pos.Application.Common;
using Pos.Application.Customers;
using Pos.Application.Inventory;
using Pos.Application.Reporting;
using Pos.Application.Sales;
using Pos.Application.Tenancy;
using Pos.Application.Sync;
using Pos.Sync.Contracts;
using Pos.Domain.Catalog;
using Pos.Domain.Identity;
using Pos.Domain.Sales;
using Pos.Domain.Security;
using Pos.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddOpenApi("v1");
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevelopmentWeb", policy =>
        policy.WithOrigins("http://localhost:3000", "http://127.0.0.1:3000")
            .AllowAnyHeader()
            .AllowAnyMethod());
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("Postgres")
    ?? (builder.Environment.IsEnvironment("Testing") ? "Host=localhost;Database=testing" : null)
    ?? throw new InvalidOperationException("PostgreSQL connection string is required.");

builder.Services.AddDbContext<CommerceDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AdministrationService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<CompleteSaleService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddScoped<TenancyService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<OutboxPublisher>();
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<Pos.Api.OutboxWorker>();
builder.Services.AddScoped<DevelopmentSeeder>();
builder.Services.AddScoped<ICurrentUserAccessor, HttpCurrentUserAccessor>();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddSingleton<ITokenIssuer, HmacTokenIssuer>();
builder.Services.AddSingleton(new SaleCalculator(new IndiaGstTaxCalculator()));

builder.Services.AddAuthentication(SignedBearerAuthenticationHandler.SchemeName)
    .AddScheme<SignedBearerAuthenticationOptions, SignedBearerAuthenticationHandler>(SignedBearerAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Enum.GetValues<Permission>())
    {
        options.AddPolicy(permission.ToString(), policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireClaim("permission", permission.ToString());
        });
    }
});

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    var signingKey = builder.Configuration["Auth:SigningKey"] ?? builder.Configuration["AUTH_SIGNING_KEY"];
    if (string.IsNullOrWhiteSpace(signingKey) || System.Text.Encoding.UTF8.GetByteCount(signingKey) < 32)
        throw new InvalidOperationException("Configure an authentication signing key of at least 32 bytes.");
}

app.Use(async (context, next) =>
{
    var correlationId = Guid.NewGuid().ToString("N");
    context.TraceIdentifier = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    using var scope = app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
    await next(context);
});

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var error = feature?.Error;
        var (status, title, detail, errors) = error switch
        {
            AppValidationException validation => (StatusCodes.Status400BadRequest, "Validation failed", "One or more validation errors occurred.", validation.Errors),
            NotFoundException notFound => (StatusCodes.Status404NotFound, "Not found", notFound.Message, null),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden", "The requested operation is not allowed.", null),
            ConflictException conflict => (StatusCodes.Status409Conflict, "Conflict", conflict.Message, null),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflict", "Record changed. Refresh before retrying.", null),
            DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23505" } } => (StatusCodes.Status409Conflict, "Conflict", "A record with the same identifier already exists.", null),
            DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23503" or "22001" or "22003" } } => (StatusCodes.Status400BadRequest, "Invalid data", "A field or related record is invalid.", null),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized", "Authentication is required.", null),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "The request could not be completed.", null)
        };

        context.Response.StatusCode = status;
        if (errors is not null)
        {
            await context.Response.WriteAsJsonAsync(new HttpValidationProblemDetails(errors) { Status = status, Title = title, Detail = detail });
            return;
        }

        await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title, Detail = detail });
    });
});

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(CancellationToken.None);
}

if (app.Environment.IsDevelopment())
{
    app.UseCors("DevelopmentWeb");
}

app.UseAuthentication();
app.Use(async (context, next) =>
{
    using var scope = app.Logger.BeginScope(new Dictionary<string, object?>
    {
        ["TenantId"] = context.User.FindFirstValue("tenant_id"),
        ["UserId"] = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
    });
    await next(context);
});
app.UseRateLimiter();
app.UseAuthorization();
app.MapOpenApi("/openapi/{documentName}.json");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/health/ready", async (CommerceDbContext db, CancellationToken cancellationToken) =>
{
    var canConnect = await db.Database.CanConnectAsync(cancellationToken);
    return canConnect ? Results.Ok(new { status = "ready", database = "postgres" }) : Results.Problem("PostgreSQL is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
});

var api = app.MapGroup("/api/v1");

api.MapPost("/auth/login", async (AuthService auth, LoginRequest request, CancellationToken cancellationToken) =>
    Results.Ok(await auth.LoginAsync(request, cancellationToken))).AllowAnonymous().RequireRateLimiting("login");

var protectedApi = api.MapGroup("").RequireAuthorization();
protectedApi.MapGet("/auth/me", async (CommerceDbContext db, ICurrentUserAccessor accessor, CancellationToken ct) =>
{
    var current = accessor.Current;
    var user = await db.Users.AsNoTracking().SingleAsync(u => u.TenantId == current.TenantId && u.Id == current.UserId, ct);
    return Results.Ok(new { current.TenantId, current.UserId, user.DisplayName, user.Email, Permissions = current.Permissions.Order().ToArray() });
});
var administration = protectedApi.MapGroup("/administration").RequireAuthorization(nameof(Permission.ManageUsers));
administration.MapGet("/permissions", () => Results.Ok(Enum.GetNames<Permission>()));
administration.MapGet("/roles", async (AdministrationService service, CancellationToken ct) => Results.Ok(await service.RolesAsync(ct)));
administration.MapPost("/roles", async (AdministrationService service, SaveRoleRequest request, CancellationToken ct) => Results.Ok(await service.SaveRoleAsync(null, request, ct)));
administration.MapPut("/roles/{id:guid}", async (AdministrationService service, Guid id, SaveRoleRequest request, CancellationToken ct) => Results.Ok(await service.SaveRoleAsync(id, request, ct)));
administration.MapGet("/users", async (AdministrationService service, string? q, int? page, int? pageSize, CancellationToken ct) => Results.Ok(await service.UsersAsync(q, page ?? 1, pageSize ?? 20, ct)));
administration.MapPost("/users", async (AdministrationService service, CreateUserRequest request, CancellationToken ct) => Results.Ok(await service.CreateUserAsync(request, ct)));
administration.MapPut("/users/{id:guid}", async (AdministrationService service, Guid id, UpdateUserRequest request, CancellationToken ct) => Results.Ok(await service.UpdateUserAsync(id, request, ct)));
administration.MapPost("/users/{id:guid}/password", async (AdministrationService service, Guid id, ResetPasswordRequest request, CancellationToken ct) => Results.Ok(await service.ResetPasswordAsync(id, request, ct)));
protectedApi.MapGet("/audit", [Authorize(Policy = nameof(Permission.ViewAudit))] async (AuditService service, string? action, string? entity, Guid? actorId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? page, int? pageSize, CancellationToken ct) =>
    Results.Ok(await service.ListAsync(action, entity, actorId, fromUtc, toUtc, page ?? 1, pageSize ?? 20, ct)));
protectedApi.MapPost("/sync/devices", [Authorize(Policy = nameof(Permission.ManageSettings))] async (SyncService service, RegisterDeviceRequest request, CancellationToken ct) =>
    Results.Ok(await service.RegisterAsync(request, ct)));
var sync = protectedApi.MapGroup("/sync/devices/{deviceId:guid}").RequireAuthorization(nameof(Permission.CompleteSale));
sync.MapGet("/snapshot", async (SyncService service, Guid deviceId, CancellationToken ct) => Results.Ok(await service.SnapshotAsync(deviceId, ct)));
sync.MapGet("/changes", async (SyncService service, Guid deviceId, long? after, int? limit, CancellationToken ct) => Results.Ok(await service.ChangesAsync(deviceId, after ?? 0, limit ?? 100, ct)));
sync.MapPost("/ack", async (SyncService service, Guid deviceId, AcknowledgeRequest request, CancellationToken ct) => Results.Ok(await service.AcknowledgeAsync(deviceId, request, ct)));
sync.MapPost("/sales", async (SyncService service, Guid deviceId, CompleteSaleRequest request, CancellationToken ct) => Results.Ok(await service.UploadAsync(deviceId, request, ct)));
protectedApi.MapGet("/tenancy/bootstrap", async (TenancyService service, CancellationToken cancellationToken) => Results.Ok(await service.BootstrapAsync(cancellationToken)));
protectedApi.MapGet("/tenancy/terminals", async (TenancyService service, CancellationToken cancellationToken) => Results.Ok(await service.TerminalsAsync(cancellationToken)));

protectedApi.MapGet("/categories", async (CommerceDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
    Results.Ok(await db.Categories.AsNoTracking().Where(c => c.TenantId == tenant.TenantId).OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.ParentCategoryId }).ToArrayAsync(cancellationToken)));

protectedApi.MapGet("/tax-categories", async (CommerceDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
    Results.Ok(await db.TaxCategories.AsNoTracking().Where(t => t.TenantId == tenant.TenantId).OrderBy(t => t.Name).Select(t => new { t.Id, t.Name, t.CountryCode, t.Rate, t.TaxBehavior }).ToArrayAsync(cancellationToken)));

protectedApi.MapGet("/products", async (ProductService service, string? q, string? barcode, string? sku, int? page, int? pageSize, bool? includeInactive, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListAsync(q, barcode, sku, page ?? 1, pageSize ?? 50, includeInactive ?? false, cancellationToken)));
protectedApi.MapPost("/products", [Authorize(Policy = nameof(Permission.ManageCatalog))] async (ProductService service, CreateProductRequest request, CancellationToken cancellationToken) =>
    Results.Created("/api/v1/products", await service.CreateAsync(request, cancellationToken)));
protectedApi.MapPut("/products/{id:guid}", [Authorize(Policy = nameof(Permission.ManageCatalog))] async (ProductService service, Guid id, UpdateProductRequest request, CancellationToken cancellationToken) =>
    Results.Ok(await service.UpdateAsync(id, request, cancellationToken)));
protectedApi.MapPost("/products/{id:guid}/activate", [Authorize(Policy = nameof(Permission.ManageCatalog))] async (ProductService service, Guid id, CancellationToken cancellationToken) =>
    Results.Ok(await service.SetActiveAsync(id, true, cancellationToken)));
protectedApi.MapPost("/products/{id:guid}/deactivate", [Authorize(Policy = nameof(Permission.ManageCatalog))] async (ProductService service, Guid id, CancellationToken cancellationToken) =>
    Results.Ok(await service.SetActiveAsync(id, false, cancellationToken)));

protectedApi.MapGet("/customers", async (CustomerService service, string? q, CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(q, cancellationToken)));
protectedApi.MapPost("/customers", async (CustomerService service, CreateCustomerRequest request, CancellationToken cancellationToken) =>
    Results.Created("/api/v1/customers", await service.CreateAsync(request, cancellationToken)));

protectedApi.MapGet("/inventory/balances", async (InventoryService service, CancellationToken cancellationToken) => Results.Ok(await service.BalancesAsync(cancellationToken)));
protectedApi.MapPost("/inventory/opening-stock", [Authorize(Policy = nameof(Permission.ManageInventory))] async (InventoryService service, OpeningStockRequest request, CancellationToken cancellationToken) =>
    Results.Created("/api/v1/inventory/transactions", await service.AddOpeningStockAsync(request, cancellationToken)));
protectedApi.MapPost("/inventory/adjustments", [Authorize(Policy = nameof(Permission.ManageInventory))] async (InventoryService service, StockAdjustmentRequest request, CancellationToken cancellationToken) =>
    Results.Created("/api/v1/inventory/transactions", await service.AdjustAsync(request, cancellationToken)));

protectedApi.MapPost("/sales/complete", [Authorize(Policy = nameof(Permission.CompleteSale))] async (CompleteSaleService service, CompleteSaleRequest request, CancellationToken cancellationToken) =>
    Results.Created("/api/v1/sales", await service.CompleteAsync(request, cancellationToken)));
protectedApi.MapPost("/sales/hold", [Authorize(Policy = nameof(Permission.CompleteSale))] async (CompleteSaleService service, HoldSaleRequest request, CancellationToken cancellationToken) =>
    Results.Created("/api/v1/sales/held", await service.HoldAsync(request, cancellationToken)));
protectedApi.MapPut("/sales/{id:guid}/cart", [Authorize(Policy = nameof(Permission.CompleteSale))] async (CompleteSaleService service, Guid id, HoldSaleRequest request, CancellationToken cancellationToken) =>
    Results.Ok(await service.HoldAsync(request, cancellationToken, id)));
protectedApi.MapPost("/sales/{id:guid}/complete-held", [Authorize(Policy = nameof(Permission.CompleteSale))] async (CompleteSaleService service, Guid id, CompleteHeldSaleRequest request, CancellationToken cancellationToken) =>
    Results.Created($"/api/v1/sales/{id}", await service.CompleteHeldAsync(id, request, cancellationToken)));
protectedApi.MapGet("/sales", async (CompleteSaleService service, SaleStatus? status, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken) =>
    Results.Ok(await service.ListAsync(status, fromUtc, toUtc, cancellationToken)));
protectedApi.MapGet("/sales/history", async (CompleteSaleService service, string? invoice, Guid? customerId, Guid? terminalId, PaymentMethod? method, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? page, int? pageSize, CancellationToken cancellationToken) =>
    Results.Ok(await service.HistoryAsync(invoice, customerId, terminalId, method, fromUtc, toUtc, page ?? 1, pageSize ?? 20, cancellationToken)));

protectedApi.MapGet("/reports/today", [Authorize(Policy = nameof(Permission.ViewReports))] async (ReportingService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.TodayAsync(cancellationToken)));

app.Run();

public partial class Program;
