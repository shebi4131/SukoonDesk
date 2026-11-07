using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SukoonDesk.Api.Domain;
using SukoonDesk.Api.Infrastructure;
using SukoonDesk.Api.Security;

namespace SukoonDesk.Api.Endpoints;

public static class RbacEndpoints
{
    public static IEndpointRouteBuilder MapRbacEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rbac").WithTags("RBAC");
        group.MapGet("/permissions", GetPermissionsAsync)
            .RequireAuthorization(PermissionPolicyProvider.For(Permissions.RolesView));
        group.MapGet("/roles", GetRolesAsync)
            .RequireAuthorization(PermissionPolicyProvider.For(Permissions.RolesView));
        group.MapPost("/roles", CreateRoleAsync)
            .RequireAuthorization(PermissionPolicyProvider.For(Permissions.RolesManage));
        group.MapPut("/roles/{id:guid}", UpdateRoleAsync)
            .RequireAuthorization(PermissionPolicyProvider.For(Permissions.RolesManage));
        group.MapDelete("/roles/{id:guid}", DeleteRoleAsync)
            .RequireAuthorization(PermissionPolicyProvider.For(Permissions.RolesManage));
        return app;
    }

    private static Task<List<PermissionResponse>> GetPermissionsAsync(AppDbContext db) => db.Permissions
        .OrderBy(x => x.Group).ThenBy(x => x.Name)
        .Select(x => new PermissionResponse(x.Id, x.Key, x.Name, x.Group, x.Description))
        .ToListAsync();

    private static async Task<IResult> GetRolesAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var tenantId = TenantId(principal);
        var roles = await db.TenantRoles.Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.IsSystem).ThenBy(x => x.Name)
            .Select(x => new RoleResponse(
                x.Id, x.Name, x.Description, x.IsSystem, x.IsActive,
                x.Members.Count,
                x.Permissions.Select(p => p.Permission.Key).ToArray()))
            .ToListAsync();
        return Results.Ok(roles);
    }

    private static async Task<IResult> CreateRoleAsync(RoleRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var tenantId = TenantId(principal);
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Role name is required."] });
        if (await db.TenantRoles.AnyAsync(x => x.TenantId == tenantId && x.Name == request.Name.Trim()))
            return Results.Conflict(new { message = "A role with this name already exists." });

        var permissionIds = await db.Permissions.Where(x => request.PermissionKeys.Contains(x.Key)).Select(x => x.Id).ToListAsync();
        var role = new TenantRole
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            IsActive = request.IsActive
        };
        db.TenantRoles.Add(role);
        db.RolePermissions.AddRange(permissionIds.Select(x => new RolePermission { Role = role, PermissionId = x }));
        await db.SaveChangesAsync();
        return Results.Created($"/api/rbac/roles/{role.Id}", new { role.Id });
    }

    private static async Task<IResult> UpdateRoleAsync(Guid id, RoleRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var tenantId = TenantId(principal);
        var role = await db.TenantRoles.Include(x => x.Permissions)
            .SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (role is null) return Results.NotFound();
        if (role.IsSystem && !role.IsActive && request.IsActive)
            return Results.BadRequest(new { message = "System role state cannot be changed." });
        if (await db.TenantRoles.AnyAsync(x => x.TenantId == tenantId && x.Id != id && x.Name == request.Name.Trim()))
            return Results.Conflict(new { message = "A role with this name already exists." });

        role.Name = request.Name.Trim();
        role.Description = request.Description.Trim();
        if (!role.IsSystem) role.IsActive = request.IsActive;
        db.RolePermissions.RemoveRange(role.Permissions);
        var permissionIds = await db.Permissions.Where(x => request.PermissionKeys.Contains(x.Key)).Select(x => x.Id).ToListAsync();
        db.RolePermissions.AddRange(permissionIds.Select(x => new RolePermission { RoleId = role.Id, PermissionId = x }));
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteRoleAsync(Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var tenantId = TenantId(principal);
        var role = await db.TenantRoles.Include(x => x.Members).SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (role is null) return Results.NotFound();
        if (role.IsSystem) return Results.BadRequest(new { message = "System roles cannot be deleted." });
        if (role.Members.Count > 0) return Results.BadRequest(new { message = "Remove all members from this role first." });
        db.TenantRoles.Remove(role);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static Guid TenantId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("tenant_id")!);
}

public sealed record PermissionResponse(Guid Id, string Key, string Name, string Group, string Description);
public sealed record RoleResponse(Guid Id, string Name, string Description, bool IsSystem, bool IsActive, int MemberCount, string[] PermissionKeys);
public sealed record RoleRequest(string Name, string Description, bool IsActive, string[] PermissionKeys);
