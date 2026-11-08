using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SukoonDesk.Api.Domain;
using SukoonDesk.Api.Infrastructure;
using SukoonDesk.Api.Security;

namespace SukoonDesk.Api.Endpoints;

public static class InsightEndpoints
{
    public static IEndpointRouteBuilder MapInsightEndpoints(this IEndpointRouteBuilder app)
    {
        var notifications = app.MapGroup("/api/notifications").RequireAuthorization().WithTags("Notifications");
        notifications.MapGet("/", ListNotificationsAsync);
        notifications.MapPost("/{id:guid}/read", ReadNotificationAsync);
        notifications.MapPost("/read-all", ReadAllNotificationsAsync);
        app.MapGet("/api/reports/overview", ReportsAsync)
            .RequireAuthorization(PermissionPolicyProvider.For(Permissions.ReportsView)).WithTags("Reports");
        app.MapGet("/api/reports/export.pdf", ExportPdfAsync)
            .RequireAuthorization(PermissionPolicyProvider.For(Permissions.ReportsExport)).WithTags("Reports");
        return app;
    }

    private static async Task<IResult> ListNotificationsAsync(ClaimsPrincipal principal, AppDbContext db, int limit = 20)
    {
        var (tenantId, membershipId) = ComplaintEndpoints.Context(principal);
        string[] hiddenTitles = ["Society activated", "Society suspended"];
        var source = await db.Notifications.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.MembershipId == membershipId && !hiddenTitles.Contains(x.Title))
            .ToListAsync();
        var rows = source.OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 100))
            .Select(x => new { x.Id, x.Title, x.Message, x.Type, x.Link, x.IsRead, x.CreatedAt }).ToList();
        return Results.Ok(new { unreadCount = rows.Count(x => !x.IsRead), items = rows });
    }

    private static async Task<IResult> ReadNotificationAsync(Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, membershipId) = ComplaintEndpoints.Context(principal);
        var notification = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.MembershipId == membershipId);
        if (notification is null) return Results.NotFound();
        notification.IsRead = true;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ReadAllNotificationsAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, membershipId) = ComplaintEndpoints.Context(principal);
        await db.Notifications.Where(x => x.TenantId == tenantId && x.MembershipId == membershipId && !x.IsRead)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.IsRead, true));
        return Results.NoContent();
    }

    private static async Task<IResult> ReportsAsync(ClaimsPrincipal principal, AppDbContext db,
        string? from, string? to, string? status, Guid? categoryId, string? priority, string? phase)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        return Results.Ok(await BuildReportAsync(tenantId, db, from, to, status, categoryId, priority, phase));
    }

    private static async Task<IResult> ExportPdfAsync(ClaimsPrincipal principal, AppDbContext db,
        string? from, string? to, string? status, Guid? categoryId, string? priority, string? phase)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        var report = await BuildReportAsync(tenantId, db, from, to, status, categoryId, priority, phase);
        return Results.File(ReportPdfBuilder.Build(report), "application/pdf", $"SukoonDesk-report-{DateTime.UtcNow:yyyyMMdd}.pdf");
    }

    private static async Task<ReportData> BuildReportAsync(Guid tenantId, AppDbContext db,
        string? from, string? to, string? status, Guid? categoryId, string? priority, string? phase)
    {
        var now = DateTimeOffset.UtcNow;
        var start = DateOnly.TryParse(from, out var fromDate) ? new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)) : now.AddDays(-30);
        var end = DateOnly.TryParse(to, out var toDate) ? new DateTimeOffset(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)) : now.AddDays(1);
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == tenantId);
        var rows = await db.Complaints.AsNoTracking().Where(x => x.TenantId == tenantId)
            .Include(x => x.Category).Include(x => x.CreatedByMembership).ThenInclude(x => x.User)
            .Include(x => x.AssignedToMembership).ThenInclude(x => x!.User).ToListAsync();
        var publicMembers = await db.PublicMembers.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync();
        var publicByMembership = publicMembers.ToDictionary(x => x.MembershipId);
        var filtered = rows.Where(x => x.CreatedAt >= start && x.CreatedAt < end).ToList();
        if (Enum.TryParse<ComplaintStatus>(status, true, out var parsedStatus)) filtered = filtered.Where(x => x.Status == parsedStatus).ToList();
        if (categoryId.HasValue) filtered = filtered.Where(x => x.CategoryId == categoryId).ToList();
        if (Enum.TryParse<ComplaintPriority>(priority, true, out var parsedPriority)) filtered = filtered.Where(x => x.Priority == parsedPriority).ToList();
        if (Enum.TryParse<SocietyPhase>(phase, true, out var parsedPhase))
        {
            var memberships = publicMembers.Where(x => x.Phase == parsedPhase).Select(x => x.MembershipId).ToHashSet();
            filtered = filtered.Where(x => memberships.Contains(x.CreatedByMembershipId)).ToList();
        }

        var filteredIds = filtered.Select(x => x.Id).ToList();
        var feedback = await db.ComplaintFeedback.Where(x => filteredIds.Contains(x.ComplaintId)).Select(x => x.Rating).ToListAsync();
        var resolved = filtered.Where(x => x.ResolvedAt.HasValue).ToList();
        ComplaintStatus[] openStatuses = [ComplaintStatus.Submitted, ComplaintStatus.Acknowledged, ComplaintStatus.Assigned, ComplaintStatus.InProgress, ComplaintStatus.WaitingOnResident, ComplaintStatus.Reopened];
        var byStatus = filtered.GroupBy(x => x.Status).Select(x => new ReportSlice(x.Key.ToString(), x.Count())).OrderByDescending(x => x.Value).ToList();
        var byCategory = filtered.GroupBy(x => new { x.CategoryId, x.Category.Name }).Select(x => new ReportSlice(x.Key.Name, x.Count(), x.Key.CategoryId)).OrderByDescending(x => x.Value).ToList();
        var byPriority = filtered.GroupBy(x => x.Priority).Select(x => new ReportSlice(x.Key.ToString(), x.Count())).OrderByDescending(x => x.Value).ToList();
        var byPhase = filtered.Where(x => publicByMembership.ContainsKey(x.CreatedByMembershipId)).GroupBy(x => publicByMembership[x.CreatedByMembershipId].Phase)
            .Select(x => new ReportSlice(x.Key.ToString(), x.Count())).OrderByDescending(x => x.Value).ToList();
        var byAssignee = filtered.GroupBy(x => x.AssignedToMembership?.User.DisplayName ?? "Unassigned")
            .Select(x => new ReportSlice(x.Key, x.Count())).OrderByDescending(x => x.Value).ToList();
        var trend = Enumerable.Range(0, 12).Reverse().Select(offset =>
        {
            var weekStart = now.Date.AddDays(-(int)now.DayOfWeek).AddDays(-7 * offset);
            var weekEnd = weekStart.AddDays(7);
            return new ReportTrend(weekStart.ToString("dd MMM"), filtered.Count(x => x.CreatedAt >= weekStart && x.CreatedAt < weekEnd), filtered.Count(x => x.ResolvedAt >= weekStart && x.ResolvedAt < weekEnd));
        }).ToList();
        ReportSlice[] backlog =
        [
            new("0-2 days", filtered.Count(x => openStatuses.Contains(x.Status) && (now - x.CreatedAt).TotalDays < 3)),
            new("3-7 days", filtered.Count(x => openStatuses.Contains(x.Status) && (now - x.CreatedAt).TotalDays is >= 3 and < 8)),
            new("8-30 days", filtered.Count(x => openStatuses.Contains(x.Status) && (now - x.CreatedAt).TotalDays is >= 8 and < 31)),
            new("30+ days", filtered.Count(x => openStatuses.Contains(x.Status) && (now - x.CreatedAt).TotalDays >= 31))
        ];
        var recent = filtered.OrderByDescending(x => x.CreatedAt).Take(20).Select(x => new ReportRow(
            x.Number, x.Title, x.Category.Name, x.Status.ToString(), x.Priority.ToString(), x.CreatedByMembership.User.DisplayName,
            x.AssignedToMembership?.User.DisplayName ?? "Unassigned", x.CreatedAt, x.DueAt)).ToList();
        var categories = await db.ComplaintCategories.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync();
        var totals = new ReportTotals(filtered.Count, filtered.Count(x => openStatuses.Contains(x.Status)),
            filtered.Count(x => x.DueAt < now && openStatuses.Contains(x.Status)), resolved.Count,
            resolved.Count == 0 ? 0 : Math.Round(resolved.Count(x => x.ResolvedAt <= x.DueAt) * 100d / resolved.Count, 1),
            resolved.Count == 0 ? 0 : Math.Round(resolved.Average(x => (x.ResolvedAt!.Value - x.CreatedAt).TotalHours), 1),
            feedback.Count == 0 ? 0 : Math.Round(feedback.Average(), 1));
        return new ReportData(tenant.Name, start, end.AddDays(-1), totals, byStatus, byCategory, byPriority, byPhase, byAssignee, backlog, trend, recent,
            categories.Cast<object>().ToList(), Enum.GetNames<ComplaintStatus>(), Enum.GetNames<ComplaintPriority>(), Enum.GetNames<SocietyPhase>());
    }
}
