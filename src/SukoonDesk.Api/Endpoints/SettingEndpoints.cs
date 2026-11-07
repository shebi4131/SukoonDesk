using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SukoonDesk.Api.Domain;
using SukoonDesk.Api.Infrastructure;
using SukoonDesk.Api.Security;

namespace SukoonDesk.Api.Endpoints;

public static partial class SettingEndpoints
{
    public static IEndpointRouteBuilder MapSettingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings/public", async (string? tenant, AppDbContext db, SettingService service) =>
        {
            var tenantId = tenant is null ? (Guid?)null : await db.Tenants
                .Where(x => x.Slug == tenant).Select(x => (Guid?)x.Id).SingleOrDefaultAsync();
            return Results.Ok(await service.GetPublicAsync(tenantId));
        }).AllowAnonymous().WithTags("Settings");

        var group = app.MapGroup("/api/settings").WithTags("Settings");
        group.MapGet("/", GetSettingsAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.SettingsView));
        group.MapPut("/{key}", UpsertSettingAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.SettingsManage));
        group.MapDelete("/{key}", DeleteSettingAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.SettingsManage));
        return app;
    }

    private static async Task<IResult> GetSettingsAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var tenantId = Guid.Parse(principal.FindFirstValue("tenant_id")!);
        var settings = await db.AppSettings.Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Key)
            .Select(x => new SettingResponse(x.Key, x.Value, x.Description, x.IsPublic, x.UpdatedAt))
            .ToListAsync();
        return Results.Ok(settings);
    }

    private static async Task<IResult> UpsertSettingAsync(string key, SettingRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        if (!SettingKeyPattern().IsMatch(key))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["key"] = ["Use letters, numbers, dots, dashes or underscores."] });
        if (request.Value.Length > 4000)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["value"] = ["Setting value is too long."] });

        var tenantId = Guid.Parse(principal.FindFirstValue("tenant_id")!);
        var setting = await db.AppSettings.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Key == key);
        if (setting is null)
        {
            setting = new AppSetting { TenantId = tenantId, Key = key, Value = request.Value };
            db.AppSettings.Add(setting);
        }
        setting.Value = request.Value;
        setting.Description = request.Description.Trim();
        setting.IsPublic = request.IsPublic;
        setting.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(new SettingResponse(setting.Key, setting.Value, setting.Description, setting.IsPublic, setting.UpdatedAt));
    }

    private static async Task<IResult> DeleteSettingAsync(string key, ClaimsPrincipal principal, AppDbContext db)
    {
        var tenantId = Guid.Parse(principal.FindFirstValue("tenant_id")!);
        var setting = await db.AppSettings.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Key == key);
        if (setting is null) return Results.NotFound();
        db.AppSettings.Remove(setting);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    [GeneratedRegex("^[a-zA-Z0-9._-]{2,120}$")]
    private static partial Regex SettingKeyPattern();
}

public sealed record SettingRequest(string Value, string Description, bool IsPublic);
public sealed record SettingResponse(string Key, string Value, string Description, bool IsPublic, DateTimeOffset UpdatedAt);
