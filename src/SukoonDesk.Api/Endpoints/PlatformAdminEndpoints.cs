using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SukoonDesk.Api.Infrastructure;

namespace SukoonDesk.Api.Endpoints;

public static class PlatformAdminEndpoints
{
    public static IEndpointRouteBuilder MapPlatformAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform").RequireAuthorization().WithTags("Platform administration");
        group.MapGet("/dashboard", DashboardAsync);
        group.MapPut("/tenants/{id:guid}/status", UpdateTenantStatusAsync);
        return app;
    }

    private static async Task<IResult> DashboardAsync(ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration)
    {
        if (!await IsPlatformAdminAsync(principal, db, configuration)) return Results.Forbid();
        var tenants = await db.Tenants.AsNoTracking().OrderByDescending(x => x.CreatedAt).Select(tenant => new
        {
            tenant.Id, tenant.Name, tenant.Slug, tenant.IsActive, tenant.CreatedAt,
            memberCount = db.TenantMemberships.Count(x => x.TenantId == tenant.Id && x.Status != Domain.MembershipStatus.Deleted),
            complaintCount = db.Complaints.Count(x => x.TenantId == tenant.Id),
            openComplaintCount = db.Complaints.Count(x => x.TenantId == tenant.Id && x.Status != Domain.ComplaintStatus.Resolved && x.Status != Domain.ComplaintStatus.Closed && x.Status != Domain.ComplaintStatus.Cancelled),
            administrator = db.MembershipRoles.Where(x => x.Membership.TenantId == tenant.Id && x.Role.Name == "Society Administrator").Select(x => new { x.Membership.User.DisplayName, x.Membership.User.Email }).FirstOrDefault()
        }).ToListAsync();
        return Results.Ok(new
        {
            totalTenants = tenants.Count,
            activeTenants = tenants.Count(x => x.IsActive),
            suspendedTenants = tenants.Count(x => !x.IsActive),
            totalMembers = tenants.Sum(x => x.memberCount),
            totalComplaints = tenants.Sum(x => x.complaintCount),
            openComplaints = tenants.Sum(x => x.openComplaintCount),
            tenants
        });
    }

    private static async Task<IResult> UpdateTenantStatusAsync(Guid id, TenantStatusRequest request, ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration)
    {
        if (!await IsPlatformAdminAsync(principal, db, configuration)) return Results.Forbid();
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == id);
        if (tenant is null) return Results.NotFound();
        tenant.IsActive = request.IsActive;
        var administrators = await db.MembershipRoles.Where(x => x.Membership.TenantId == id && x.Role.Name == "Society Administrator").Select(x => x.MembershipId).Distinct().ToListAsync();
        foreach (var membershipId in administrators)
            ComplaintEndpoints.AddNotification(db, id, membershipId, request.IsActive ? "Society activated" : "Society suspended", request.IsActive ? "Platform access has been restored." : "Platform access has been suspended by SukoonDesk administration.", request.IsActive ? "success" : "warning", "/app");
        await db.SaveChangesAsync();
        return Results.Ok(new { tenant.Id, tenant.IsActive, message = request.IsActive ? "Society activated." : "Society suspended." });
    }

    internal static async Task<bool> IsPlatformAdminAsync(ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return false;
        var user = await db.Users.Where(x => x.Id == userId && x.IsActive).Select(x => new { x.NormalizedEmail, x.IsPlatformAdmin }).SingleOrDefaultAsync();
        if (user is null) return false;
        if (user.IsPlatformAdmin) return true;
        var configured = configuration.GetSection("PlatformAdmin:Emails").Get<string[]>() ?? [];
        return configured.Any(x => string.Equals(x.Trim(), user.NormalizedEmail, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Trim().ToUpperInvariant(), user.NormalizedEmail, StringComparison.Ordinal));
    }
}

public sealed record TenantStatusRequest(bool IsActive);
