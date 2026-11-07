using Microsoft.EntityFrameworkCore;

namespace SukoonDesk.Api.Infrastructure;

public sealed class SettingService(AppDbContext db, IConfiguration configuration)
{
    public async Task<Dictionary<string, string>> GetPublicAsync(Guid? tenantId = null)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["product.name"] = configuration["Product:Name"] ?? "SukoonDesk",
            ["product.tagline"] = configuration["Product:Tagline"] ?? "Every concern, resolved.",
            ["theme.primary"] = configuration["Product:Theme:Primary"] ?? "#155E75",
            ["theme.accent"] = configuration["Product:Theme:Accent"] ?? "#D97706"
        };

        var rows = await db.AppSettings
            .Where(x => x.IsPublic && (x.TenantId == null || x.TenantId == tenantId))
            .OrderBy(x => x.TenantId == null ? 0 : 1)
            .ToListAsync();
        foreach (var row in rows) settings[row.Key] = row.Value;
        return settings;
    }
}
