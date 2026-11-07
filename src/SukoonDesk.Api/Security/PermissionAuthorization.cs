using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SukoonDesk.Api.Infrastructure;

namespace SukoonDesk.Api.Security;

public static class Permissions
{
    public const string DashboardView = "dashboard.view";
    public const string RolesView = "roles.view";
    public const string RolesManage = "roles.manage";
    public const string SettingsView = "settings.view";
    public const string SettingsManage = "settings.manage";
    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";
    public const string ComplaintsCreate = "complaints.create";
    public const string ComplaintsViewOwn = "complaints.view_own";
    public const string ComplaintsViewAll = "complaints.view_all";
    public const string ComplaintsAssign = "complaints.assign";
    public const string ComplaintsUpdateStatus = "complaints.update_status";
    public const string ComplaintsEdit = "complaints.edit";
    public const string ComplaintsDelete = "complaints.delete";
    public const string CommentsEdit = "comments.edit";
    public const string CommentsDelete = "comments.delete";
    public const string ReportsView = "reports.view";
    public const string ReportsExport = "reports.export";
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "Permission:";

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return base.GetPolicyAsync(policyName);

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[Prefix.Length..]))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    public static string For(string permission) => $"{Prefix}{permission}";
}

public sealed class PermissionHandler(AppDbContext db) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tenantIdValue = context.User.FindFirstValue("tenant_id");
        if (!Guid.TryParse(userIdValue, out var userId) || !Guid.TryParse(tenantIdValue, out var tenantId))
            return;

        var allowed = await db.MembershipRoles.AnyAsync(x =>
            x.Membership.UserId == userId &&
            x.Membership.TenantId == tenantId &&
            x.Membership.Status == Domain.MembershipStatus.Active &&
            x.Role.IsActive &&
            x.Role.TenantId == tenantId &&
            x.Role.Permissions.Any(rp => rp.Permission.Key == requirement.Permission));

        if (allowed) context.Succeed(requirement);
    }
}

public static class PermissionAuthorizationExtensions
{
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        return services;
    }
}
