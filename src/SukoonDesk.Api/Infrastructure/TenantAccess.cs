using SukoonDesk.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace SukoonDesk.Api.Infrastructure;

public static class TenantAccess
{
    public static bool IsAvailable(Tenant tenant, DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.UtcNow;
        if (!tenant.IsActive || tenant.SubscriptionStatus is SubscriptionStatus.Suspended or SubscriptionStatus.Expired) return false;
        if (tenant.SubscriptionStatus == SubscriptionStatus.Trial && tenant.TrialEndsAt.HasValue && tenant.TrialEndsAt < current) return false;
        if (tenant.SubscriptionEndsAt.HasValue && tenant.SubscriptionEndsAt < current && tenant.SubscriptionPlan != SubscriptionPlan.Perpetual) return false;
        return true;
    }

    public static async Task<int> MonthlyComplaintCountAsync(AppDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        if (!db.Database.IsSqlite())
            return await db.Complaints.CountAsync(x => x.TenantId == tenantId && x.CreatedAt >= monthStart, cancellationToken);

        // SQLite stores DateTimeOffset values as text and cannot translate relational comparisons for them.
        var createdDates = await db.Complaints.AsNoTracking().Where(x => x.TenantId == tenantId)
            .Select(x => x.CreatedAt).ToListAsync(cancellationToken);
        return createdDates.Count(x => x >= monthStart);
    }
}
