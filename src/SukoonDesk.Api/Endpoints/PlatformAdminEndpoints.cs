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
        group.MapPut("/tenants/{id:guid}/subscription", UpdateSubscriptionAsync);
        return app;
    }

    private static async Task<IResult> DashboardAsync(ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration)
    {
        if (!await IsPlatformAdminAsync(principal, db, configuration)) return Results.Forbid();
        var platformTenantSlug = PlatformTenantSlug(configuration);
        var tenants = (await db.Tenants.AsNoTracking().Where(x => x.Slug != platformTenantSlug).Select(tenant => new
        {
            tenant.Id, tenant.Name, tenant.Slug, tenant.IsActive, tenant.CreatedAt,
            tenant.SubscriptionPlan, tenant.SubscriptionStatus, tenant.TrialEndsAt, tenant.SubscriptionEndsAt,
            tenant.ResidentLimit, tenant.StaffLimit, tenant.MonthlyComplaintLimit, tenant.BillingNotes,
            memberCount = db.TenantMemberships.Count(x => x.TenantId == tenant.Id && x.Status != Domain.MembershipStatus.Deleted),
            complaintCount = db.Complaints.Count(x => x.TenantId == tenant.Id),
            openComplaintCount = db.Complaints.Count(x => x.TenantId == tenant.Id && x.Status != Domain.ComplaintStatus.Resolved && x.Status != Domain.ComplaintStatus.Closed && x.Status != Domain.ComplaintStatus.Cancelled),
            administrator = db.MembershipRoles.Where(x => x.Membership.TenantId == tenant.Id && x.Role.Name == "Society Administrator").Select(x => new { x.Membership.User.DisplayName, x.Membership.User.Email }).FirstOrDefault()
        }).ToListAsync()).OrderByDescending(x => x.CreatedAt).ToList();
        return Results.Ok(new
        {
            totalTenants = tenants.Count,
            activeTenants = tenants.Count(x => x.IsActive),
            suspendedTenants = tenants.Count(x => !x.IsActive),
            totalMembers = tenants.Sum(x => x.memberCount),
            totalComplaints = tenants.Sum(x => x.complaintCount),
            openComplaints = tenants.Sum(x => x.openComplaintCount),
            trialTenants = tenants.Count(x => x.SubscriptionStatus == Domain.SubscriptionStatus.Trial),
            expiringSoon = tenants.Count(x => (x.TrialEndsAt ?? x.SubscriptionEndsAt) >= DateTimeOffset.UtcNow && (x.TrialEndsAt ?? x.SubscriptionEndsAt) <= DateTimeOffset.UtcNow.AddDays(14)),
            tenants
        });
    }

    private static async Task<IResult> UpdateTenantStatusAsync(Guid id, TenantStatusRequest request, ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration)
    {
        if (!await IsPlatformAdminAsync(principal, db, configuration)) return Results.Forbid();
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == id);
        if (tenant is null) return Results.NotFound();
        if (tenant.Slug == PlatformTenantSlug(configuration))
            return Results.BadRequest(new { message = "The internal SukoonDesk administration workspace cannot be suspended." });
        tenant.IsActive = request.IsActive;
        var administrators = await db.MembershipRoles.Where(x => x.Membership.TenantId == id && x.Role.Name == "Society Administrator").Select(x => x.MembershipId).Distinct().ToListAsync();
        if (request.IsActive)
        {
            await db.TenantMemberships
                .Where(x => administrators.Contains(x.Id) && x.Status == Domain.MembershipStatus.Pending)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, Domain.MembershipStatus.Active));
        }
        await db.SaveChangesAsync();
        return Results.Ok(new { tenant.Id, tenant.IsActive, message = request.IsActive ? "Society activated." : "Society suspended." });
    }

    private static async Task<IResult> UpdateSubscriptionAsync(Guid id, SubscriptionRequest request, ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration)
    {
        if (!await IsPlatformAdminAsync(principal, db, configuration)) return Results.Forbid();
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == id);
        if (tenant is null) return Results.NotFound();
        if (tenant.Slug == PlatformTenantSlug(configuration))
            return Results.BadRequest(new { message = "The internal administration workspace does not have a customer subscription." });
        if (request.ResidentLimit < 1 || request.StaffLimit < 1 || request.MonthlyComplaintLimit < 1)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["limits"] = ["All subscription limits must be at least 1."] });
        tenant.SubscriptionPlan = request.Plan;
        tenant.SubscriptionStatus = request.Status;
        tenant.TrialEndsAt = request.TrialEndsAt;
        tenant.SubscriptionEndsAt = request.SubscriptionEndsAt;
        tenant.ResidentLimit = request.ResidentLimit;
        tenant.StaffLimit = request.StaffLimit;
        tenant.MonthlyComplaintLimit = request.MonthlyComplaintLimit;
        tenant.BillingNotes = request.BillingNotes?.Trim() ?? string.Empty;
        tenant.IsActive = request.Status is not Domain.SubscriptionStatus.Suspended and not Domain.SubscriptionStatus.Expired;
        await db.SaveChangesAsync();
        return Results.Ok(new { message = "Subscription and allowance settings updated." });
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

    private static string PlatformTenantSlug(IConfiguration configuration) =>
        configuration["PlatformAdmin:Seed:SocietySlug"]?.Trim().ToLowerInvariant() ?? "sukoondesk-admin";
}

public sealed record TenantStatusRequest(bool IsActive);
public sealed record SubscriptionRequest(Domain.SubscriptionPlan Plan, Domain.SubscriptionStatus Status, DateTimeOffset? TrialEndsAt,
    DateTimeOffset? SubscriptionEndsAt, int ResidentLimit, int StaffLimit, int MonthlyComplaintLimit, string? BillingNotes);
