using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Administration;
using Pos.Application.Auth;
using Pos.Application.Common;
using Pos.Domain.Identity;
using Pos.Infrastructure.Persistence;
using Xunit;

namespace Pos.Postgres.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AdministrationFlowTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Role_grants_and_revocations_apply_to_existing_tokens_and_stale_edits_conflict()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var admin = await Login(factory);
        using var cashier = await Login(factory, "cashier@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/administration/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/audit")).StatusCode);
        var roles = await admin.GetFromJsonAsync<RoleResponse[]>("/api/v1/administration/roles", Json);
        var role = roles!.Single(r => r.Name == "Cashier");
        var changed = await Put<RoleResponse>(admin, $"/api/v1/administration/roles/{role.Id}", new SaveRoleRequest(role.Name, [Permission.CompleteSale, Permission.ViewAudit], role.Version));
        Assert.True(changed.Version > role.Version);
        (await cashier.GetAsync("/api/v1/audit")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/v1/administration/roles/{role.Id}", new SaveRoleRequest(role.Name, [], role.Version), Json)).StatusCode);
        await Put<RoleResponse>(admin, $"/api/v1/administration/roles/{role.Id}", new SaveRoleRequest(role.Name, [Permission.CompleteSale], changed.Version));
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/audit")).StatusCode);
        (await cashier.GetAsync("/api/v1/products")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task User_creation_reset_disable_and_reenable_never_expose_or_resurrect_credentials()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var admin = await Login(factory);
        var roles = await admin.GetFromJsonAsync<RoleResponse[]>("/api/v1/administration/roles", Json);
        var cashierRole = roles!.Single(r => r.Name == "Cashier").Id;
        const string password = "Initial-password-123!";
        var user = await Post<UserResponse>(admin, "/api/v1/administration/users", new CreateUserRequest("New colleague", " NEW@example.test ", password, [cashierRole]));
        Assert.Equal("new@example.test", user.Email);
        using var colleague = await Login(factory, user.Email, password);
        (await colleague.GetAsync("/api/v1/tenancy/bootstrap")).EnsureSuccessStatusCode();
        var updated = await Put<UserResponse>(admin, $"/api/v1/administration/users/{user.Id}", new UpdateUserRequest(user.DisplayName, false, [cashierRole], user.Version));
        Assert.Equal(HttpStatusCode.Unauthorized, (await colleague.GetAsync("/api/v1/auth/me")).StatusCode);
        updated = await Put<UserResponse>(admin, $"/api/v1/administration/users/{user.Id}", new UpdateUserRequest(user.DisplayName, true, [cashierRole], updated.Version));
        Assert.Equal(HttpStatusCode.Unauthorized, (await colleague.GetAsync("/api/v1/auth/me")).StatusCode);
        using var active = await Login(factory, user.Email, password);
        const string replacement = "Replacement-password-456!";
        await Post<UserResponse>(admin, $"/api/v1/administration/users/{user.Id}/password", new ResetPasswordRequest(replacement, updated.Version));
        Assert.Equal(HttpStatusCode.Unauthorized, (await active.GetAsync("/api/v1/auth/me")).StatusCode);
        using var fresh = await Login(factory, user.Email, replacement);
        (await fresh.GetAsync("/api/v1/auth/me")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(user.Email, password))).StatusCode);
        var audit = await admin.GetStringAsync("/api/v1/audit?entity=User");
        Assert.DoesNotContain(password, audit);
        Assert.DoesNotContain(replacement, audit);
        Assert.DoesNotContain("PasswordHash", audit, StringComparison.OrdinalIgnoreCase);
        var users = await admin.GetStringAsync("/api/v1/administration/users");
        Assert.DoesNotContain("PasswordHash", users, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Last_administrator_and_invalid_changes_roll_back_business_and_audit_writes()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var admin = await Login(factory);
        var roles = (await admin.GetFromJsonAsync<RoleResponse[]>("/api/v1/administration/roles", Json))!;
        var role = roles.Single(r => r.Name == "Admin");
        var auditBefore = (await admin.GetFromJsonAsync<PagedResponse<AuditResponse>>("/api/v1/audit", Json))!.TotalCount;
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/v1/administration/roles/{role.Id}", new SaveRoleRequest(role.Name, [Permission.ViewAudit], role.Version), Json)).StatusCode);
        var users = (await admin.GetFromJsonAsync<PagedResponse<UserResponse>>("/api/v1/administration/users", Json))!;
        var user = users.Items.Single(u => u.Email == "admin@example.test");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/v1/administration/users/{user.Id}", new UpdateUserRequest(user.DisplayName, false, user.RoleIds.ToArray(), user.Version), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/administration/users", new CreateUserRequest("Invalid", "invalid@example.test", "short", []), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/administration/roles", new SaveRoleRequest("Invalid", [(Permission)999], null), Json)).StatusCode);
        var current = (await admin.GetFromJsonAsync<PagedResponse<AuditResponse>>("/api/v1/audit", Json))!;
        Assert.Equal(auditBefore, current.TotalCount);
        Assert.Contains(Permission.ManageUsers, (await admin.GetFromJsonAsync<RoleResponse[]>("/api/v1/administration/roles", Json))!.Single(r => r.Id == role.Id).Permissions);
    }

    [Fact]
    public async Task Concurrent_self_demotions_leave_one_active_administrator()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var admin = await Login(factory);
        var roles = (await admin.GetFromJsonAsync<RoleResponse[]>("/api/v1/administration/roles", Json))!;
        var second = await Post<UserResponse>(admin, "/api/v1/administration/users", new CreateUserRequest("Second admin", "second@example.test", "Second-password-123!", [roles.Single(r => r.Name == "Admin").Id]));
        using var other = await Login(factory, second.Email, "Second-password-123!");
        var first = (await admin.GetFromJsonAsync<PagedResponse<UserResponse>>("/api/v1/administration/users", Json))!.Items.Single(u => u.Email == "admin@example.test");
        var responses = await Task.WhenAll(
            admin.PutAsJsonAsync($"/api/v1/administration/users/{first.Id}", new UpdateUserRequest(first.DisplayName, true, [], first.Version), Json),
            other.PutAsJsonAsync($"/api/v1/administration/users/{second.Id}", new UpdateUserRequest(second.DisplayName, true, [], second.Version), Json));
        Assert.Single(responses, r => r.IsSuccessStatusCode);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        Assert.Equal(1, await (from user in db.Users join assignment in db.UserRoles on user.Id equals assignment.UserId
                               join permission in db.RolePermissions on assignment.RoleId equals permission.RoleId
                               where user.IsActive && permission.Permission == Permission.ManageUsers select user.Id).Distinct().CountAsync());
    }

    [Fact]
    public async Task Administration_and_audit_cannot_cross_tenant_boundaries()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var admin = await Login(factory);
        var foreignRole = Guid.NewGuid(); var foreignUser = Guid.NewGuid(); var foreignTenant = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            db.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Status = Pos.Domain.Tenancy.TenantStatus.Active });
            db.Roles.Add(new Pos.Infrastructure.Persistence.Role { Id = foreignRole, TenantId = foreignTenant, Name = "Other role" });
            db.Users.Add(new Pos.Infrastructure.Persistence.User { Id = foreignUser, TenantId = foreignTenant, DisplayName = "Foreign user", Email = "foreign@example.test" });
            db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), TenantId = foreignTenant, ActorUserId = foreignUser, Action = "ForeignAction", EntityName = "User", EntityId = foreignUser, OccurredAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/v1/administration/roles/{foreignRole}", new SaveRoleRequest("Stolen", [], 1), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/v1/administration/users/{foreignUser}", new UpdateUserRequest("Stolen", true, [], 1), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/administration/users", new CreateUserRequest("No", "no@example.test", "Long-password-123!", [foreignRole]), Json)).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<PagedResponse<AuditResponse>>("/api/v1/audit?action=ForeignAction", Json))!.Items);
        Assert.DoesNotContain("foreign@example.test", await admin.GetStringAsync("/api/v1/administration/users"));
    }

    [Fact]
    public async Task Audit_filters_pagination_and_stale_user_versions_are_enforced()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var admin = await Login(factory);
        var user = await Post<UserResponse>(admin, "/api/v1/administration/users", new CreateUserRequest("Paged user", "paged@example.test", "Long-password-123!", []));
        await Put<UserResponse>(admin, $"/api/v1/administration/users/{user.Id}", new UpdateUserRequest("Renamed", true, [], user.Version));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/v1/administration/users/{user.Id}", new UpdateUserRequest("Stale", true, [], user.Version), Json)).StatusCode);
        var first = (await admin.GetFromJsonAsync<PagedResponse<AuditResponse>>("/api/v1/audit?entity=User&pageSize=1", Json))!;
        var second = (await admin.GetFromJsonAsync<PagedResponse<AuditResponse>>("/api/v1/audit?entity=User&pageSize=1&page=2", Json))!;
        Assert.Equal(2, first.TotalCount);
        Assert.NotEqual(Assert.Single(first.Items).Id, Assert.Single(second.Items).Id);
        var selected = (await admin.GetFromJsonAsync<PagedResponse<AuditResponse>>($"/api/v1/audit?action=UserUpdated&actorId={DevelopmentSeeder.DemoAdminUserId}", Json))!;
        Assert.Equal("Renamed", JsonDocument.Parse(Assert.Single(selected.Items).AfterJson!).RootElement.GetProperty("DisplayName").GetString());
        Assert.Empty((await admin.GetFromJsonAsync<PagedResponse<AuditResponse>>("/api/v1/audit?fromUtc=2100-01-01T00:00:00Z", Json))!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/audit?fromUtc=2100-01-01T00:00:00Z&toUtc=2000-01-01T00:00:00Z")).StatusCode);
    }

    [Fact]
    public async Task Upgrade_backfills_existing_administrators_once_and_preserves_existing_accounts()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var admin = await Login(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261017110655_OfflinePreparation");
            await migrator.MigrateAsync();
            Assert.Equal(1, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == DevelopmentSeeder.DemoAdminUserId)).AuthVersion);
            var role = await db.Roles.SingleAsync(r => r.Name == "Admin");
            Assert.True(await db.RolePermissions.AnyAsync(p => p.RoleId == role.Id && p.Permission == Permission.ManageUsers));
            Assert.True(await db.RolePermissions.AnyAsync(p => p.RoleId == role.Id && p.Permission == Permission.ViewAudit));
            // A later seed pass must not restore a permission deliberately removed by an administrator.
            db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(p => p.RoleId == role.Id && p.Permission == Permission.ViewAudit));
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(CancellationToken.None);
            Assert.False(await db.RolePermissions.AnyAsync(p => p.RoleId == role.Id && p.Permission == Permission.ViewAudit));
        }
        (await admin.GetAsync("/api/v1/administration/users")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/audit")).StatusCode);
    }

    [Fact]
    public async Task Legacy_tokens_default_to_generation_one_but_cannot_survive_password_reset()
    {
        await using var factory = await PostgresTestAppFactory.CreateAsync();
        using var admin = await Login(factory);
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            UserId = DevelopmentSeeder.DemoAdminUserId, TenantId = DevelopmentSeeder.DemoTenantId,
            Email = "admin@example.test", Permissions = new[] { "ManageSettings" }, ExpiresAtUnix = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()
        }));
        var signature = Encode(HMACSHA256.HashData(Encoding.UTF8.GetBytes("postgres-testing-signing-key"), Encoding.UTF8.GetBytes(payload)));
        using var legacy = factory.CreateClient();
        legacy.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", $"{payload}.{signature}");
        // New permissions come from current tenant roles, not from the legacy payload.
        (await legacy.GetAsync("/api/v1/administration/users")).EnsureSuccessStatusCode();
        var user = (await admin.GetFromJsonAsync<PagedResponse<UserResponse>>("/api/v1/administration/users", Json))!.Items.Single(u => u.Id == DevelopmentSeeder.DemoAdminUserId);
        await Post<UserResponse>(admin, $"/api/v1/administration/users/{user.Id}/password", new ResetPasswordRequest("Legacy-replacement-123!", user.Version));
        Assert.Equal(HttpStatusCode.Unauthorized, (await legacy.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<T> Post<T>(HttpClient client, string path, object body)
    { var response = await client.PostAsJsonAsync(path, body, Json); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }
    private static async Task<T> Put<T>(HttpClient client, string path, object body)
    { var response = await client.PutAsJsonAsync(path, body, Json); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>(Json))!; }
    private static async Task<HttpClient> Login(PostgresTestAppFactory factory, string email = "admin@example.test", string password = "ChangeMe123!")
    {
        var client = factory.CreateClient();
        var result = await Post<LoginResponse>(client, "/api/v1/auth/login", new LoginRequest(email, password));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
        return client;
    }
}
