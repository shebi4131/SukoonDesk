using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SukoonDesk.Api.Domain;
using SukoonDesk.Api.Infrastructure;
using SukoonDesk.Api.Security;

namespace SukoonDesk.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard", DashboardAsync)
            .RequireAuthorization(PermissionPolicyProvider.For(Permissions.DashboardView)).WithTags("Dashboard");
        return app;
    }

    private static async Task<IResult> DashboardAsync(ClaimsPrincipal principal, AppDbContext db,
        string? from, string? to, string? status, Guid? categoryId, string? priority, string? phase)
    {
        if (!Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenantId)) return Results.Unauthorized();
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId);
        if (tenant is null) return Results.Unauthorized();
        var now = DateTimeOffset.UtcNow;
        var start = DateOnly.TryParse(from, out var startDate) ? startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : now.AddDays(-30).UtcDateTime;
        var end = DateOnly.TryParse(to, out var endDate) ? endDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : now.UtcDateTime.AddDays(1);
        var allRows = await db.Complaints.AsNoTracking().Where(x => x.TenantId == tenantId)
            .Include(x => x.Category).Include(x => x.AssignedToMembership).ThenInclude(x => x!.User)
            .Include(x => x.CreatedByMembership).ThenInclude(x => x.User).ToListAsync();
        var publicMembers = await db.PublicMembers.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync();
        var memberByMembership = publicMembers.ToDictionary(x => x.MembershipId);
        var filtered = allRows.Where(x => x.CreatedAt.UtcDateTime >= start && x.CreatedAt.UtcDateTime < end).ToList();
        if (Enum.TryParse<ComplaintStatus>(status, true, out var parsedStatus)) filtered = filtered.Where(x => x.Status == parsedStatus).ToList();
        if (categoryId.HasValue) filtered = filtered.Where(x => x.CategoryId == categoryId).ToList();
        if (Enum.TryParse<ComplaintPriority>(priority, true, out var parsedPriority)) filtered = filtered.Where(x => x.Priority == parsedPriority).ToList();
        if (Enum.TryParse<SocietyPhase>(phase, true, out var parsedPhase))
        {
            var phaseMemberships = publicMembers.Where(x => x.Phase == parsedPhase).Select(x => x.MembershipId).ToHashSet();
            filtered = filtered.Where(x => phaseMemberships.Contains(x.CreatedByMembershipId)).ToList();
        }

        var openStatuses = new[] { ComplaintStatus.Submitted, ComplaintStatus.Acknowledged, ComplaintStatus.Assigned, ComplaintStatus.InProgress, ComplaintStatus.WaitingOnResident, ComplaintStatus.Reopened };
        var resolved = filtered.Where(x => x.ResolvedAt.HasValue).ToList();
        var activeMembers = await db.TenantMemberships.CountAsync(x => x.TenantId == tenantId && x.Status == MembershipStatus.Active);
        var roleCount = await db.TenantRoles.CountAsync(x => x.TenantId == tenantId && x.IsActive);
        var activeStaff = await db.MembershipRoles.Where(x => x.Membership.TenantId == tenantId && x.Membership.Status == MembershipStatus.Active)
            .Select(x => x.MembershipId).Distinct().CountAsync();
        var ratings = await db.ComplaintFeedback.Where(x => x.TenantId == tenantId).Select(x => x.Rating).ToListAsync();
        var today = now.Date;
        var dueToday = filtered.Count(x => openStatuses.Contains(x.Status) && x.DueAt.Date == today);

        var trend = Enumerable.Range(0, 12).Reverse().Select(offset =>
        {
            var weekStart = now.Date.AddDays(-(int)now.DayOfWeek).AddDays(-7 * offset);
            var weekEnd = weekStart.AddDays(7);
            return new
            {
                label = weekStart.ToString("dd MMM"),
                created = filtered.Count(x => x.CreatedAt >= weekStart && x.CreatedAt < weekEnd),
                resolved = filtered.Count(x => x.ResolvedAt >= weekStart && x.ResolvedAt < weekEnd)
            };
        });
        var backlog = new[]
        {
            new { name = "0–2 days", value = filtered.Count(x => openStatuses.Contains(x.Status) && (now - x.CreatedAt).TotalDays < 3) },
            new { name = "3–7 days", value = filtered.Count(x => openStatuses.Contains(x.Status) && (now - x.CreatedAt).TotalDays is >= 3 and < 8) },
            new { name = "8–30 days", value = filtered.Count(x => openStatuses.Contains(x.Status) && (now - x.CreatedAt).TotalDays is >= 8 and < 31) },
            new { name = "30+ days", value = filtered.Count(x => openStatuses.Contains(x.Status) && (now - x.CreatedAt).TotalDays >= 31) }
        };
        var recent = filtered.OrderByDescending(x => x.CreatedAt).Take(8).Select(x => new
        {
            x.Id, x.Number, x.Title, x.Status, x.Priority, x.DueAt, x.CreatedAt,
            category = x.Category.Name, assignee = x.AssignedToMembership?.User.DisplayName,
            resident = x.CreatedByMembership.User.DisplayName,
            source = memberByMembership.ContainsKey(x.CreatedByMembershipId) ? "Public desk" : "Staff portal"
        });
        var categories = await db.ComplaintCategories.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync();

        return Results.Ok(new
        {
            society = tenant.Name,
            greeting = $"Welcome back, {principal.FindFirstValue("display_name")}",
            period = new { from = start.ToString("yyyy-MM-dd"), to = end.AddDays(-1).ToString("yyyy-MM-dd") },
            filters = new { categories, statuses = Enum.GetNames<ComplaintStatus>(), priorities = Enum.GetNames<ComplaintPriority>(), phases = Enum.GetNames<SocietyPhase>() },
            metrics = new
            {
                totalComplaints = filtered.Count,
                openComplaints = filtered.Count(x => openStatuses.Contains(x.Status)),
                unassigned = filtered.Count(x => openStatuses.Contains(x.Status) && !x.AssignedToMembershipId.HasValue),
                overdue = filtered.Count(x => openStatuses.Contains(x.Status) && x.DueAt < now),
                dueToday,
                resolvedThisMonth = resolved.Count,
                closed = filtered.Count(x => x.Status == ComplaintStatus.Closed),
                averageResolutionHours = resolved.Count == 0 ? 0 : Math.Round(resolved.Average(x => (x.ResolvedAt!.Value - x.CreatedAt).TotalHours), 1),
                slaCompliance = resolved.Count == 0 ? 0 : Math.Round(resolved.Count(x => x.ResolvedAt <= x.DueAt) * 100d / resolved.Count, 1),
                satisfaction = ratings.Count == 0 ? 0 : Math.Round(ratings.Average(), 1),
                activeMembers, publicMembers = publicMembers.Count, activeStaff, roleCount
            },
            charts = new
            {
                byStatus = filtered.GroupBy(x => x.Status).Select(x => new { name = x.Key.ToString(), value = x.Count() }).OrderByDescending(x => x.value),
                byCategory = filtered.GroupBy(x => x.Category.Name).Select(x => new { name = x.Key, value = x.Count() }).OrderByDescending(x => x.value),
                byPriority = filtered.GroupBy(x => x.Priority).Select(x => new { name = x.Key.ToString(), value = x.Count() }).OrderByDescending(x => x.value),
                byPhase = filtered.Where(x => memberByMembership.ContainsKey(x.CreatedByMembershipId)).GroupBy(x => memberByMembership[x.CreatedByMembershipId].Phase)
                    .Select(x => new { name = x.Key.ToString(), value = x.Count() }).OrderByDescending(x => x.value),
                byAssignee = filtered.GroupBy(x => x.AssignedToMembership?.User.DisplayName ?? "Unassigned").Select(x => new { name = x.Key, value = x.Count() }).OrderByDescending(x => x.value),
                trend, backlog
            },
            recent
        });
    }
}
