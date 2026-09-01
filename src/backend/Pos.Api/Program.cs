using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Api.Security;
using Pos.Application.Auth;
using Pos.Application.Catalog;
using Pos.Application.Common;
using Pos.Application.Customers;
using Pos.Application.Inventory;
using Pos.Application.Reporting;
using Pos.Application.Sales;
using Pos.Application.Tenancy;
using Pos.Domain.Catalog;
using Pos.Domain.Identity;
using Pos.Domain.Sales;
using Pos.Domain.Security;
using Pos.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddProblemDetails();
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
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<CompleteSaleService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddScoped<TenancyService>();
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
    Results.Ok(await auth.LoginAsync(request, cancellationToken))).AllowAnonymous();

var protectedApi = api.MapGroup("").RequireAuthorization();
protectedApi.MapGet("/tenancy/bootstrap", async (TenancyService service, CancellationToken cancellationToken) => Results.Ok(await service.BootstrapAsync(cancellationToken)));

protectedApi.MapGet("/categories", async (CommerceDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
    Results.Ok(await db.Categories.AsNoTracking().Where(c => c.TenantId == tenant.TenantId).OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.ParentCategoryId }).ToArrayAsync(cancellationToken)));

protectedApi.MapGet("/tax-categories", async (CommerceDbContext db, ITenantContext tenant, CancellationToken cancellationToken) =>
    Results.Ok(await db.TaxCategories.AsNoTracking().Where(t => t.TenantId == tenant.TenantId).OrderBy(t => t.Name).Select(t => new { t.Id, t.Name, t.CountryCode, t.Rate, t.TaxBehavior }).ToArrayAsync(cancellationToken)));

protectedApi.MapGet("/products", async (ProductService service, string? q, CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(q, cancellationToken)));
protectedApi.MapPost("/products", [Authorize(Policy = nameof(Permission.ManageCatalog))] async (ProductService service, CreateProductRequest request, CancellationToken cancellationToken) =>
    Results.Created("/api/v1/products", await service.CreateAsync(request, cancellationToken)));

protectedApi.MapGet("/customers", async (CustomerService service, string? q, CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(q, cancellationToken)));
protectedApi.MapPost("/customers", async (CustomerService service, CreateCustomerRequest request, CancellationToken cancellationToken) =>
    Results.Created("/api/v1/customers", await service.CreateAsync(request, cancellationToken)));

protectedApi.MapGet("/inventory/balances", async (InventoryService service, CancellationToken cancellationToken) => Results.Ok(await service.BalancesAsync(cancellationToken)));
protectedApi.MapPost("/inventory/opening-stock", [Authorize(Policy = nameof(Permission.ManageInventory))] async (InventoryService service, OpeningStockRequest request, CancellationToken cancellationToken) =>
    Results.Created("/api/v1/inventory/transactions", await service.AddOpeningStockAsync(request, cancellationToken)));

protectedApi.MapPost("/sales/complete", [Authorize(Policy = nameof(Permission.CompleteSale))] async (CompleteSaleService service, CompleteSaleRequest request, CancellationToken cancellationToken) =>
    Results.Created("/api/v1/sales", await service.CompleteAsync(request, cancellationToken)));
protectedApi.MapGet("/sales", async (CompleteSaleService service, CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(cancellationToken)));

protectedApi.MapGet("/reports/today", [Authorize(Policy = nameof(Permission.ViewReports))] async (ReportingService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.TodayAsync(cancellationToken)));

app.Run();

public partial class Program;
