using Microsoft.EntityFrameworkCore;
using SukoonDesk.Api.Domain;
using SukoonDesk.Api.Security;

namespace SukoonDesk.Api.Infrastructure;

public static class SeedData
{
    private static readonly (string Key, string Name, string Group, string Description)[] PermissionSeed =
    [
        (Permissions.DashboardView, "View dashboard", "Dashboard", "View society operational summaries."),
        (Permissions.RolesView, "View roles", "Access control", "View roles and permission assignments."),
        (Permissions.RolesManage, "Manage roles", "Access control", "Create, update and remove tenant roles."),
        (Permissions.SettingsView, "View settings", "Administration", "View society configuration."),
        (Permissions.SettingsManage, "Manage settings", "Administration", "Change society configuration."),
        (Permissions.UsersView, "View users", "People", "View residents and staff."),
        (Permissions.UsersManage, "Manage users", "People", "Approve, suspend and update memberships."),
        (Permissions.ComplaintsCreate, "Create complaints", "Complaints", "Create a new complaint."),
        (Permissions.ComplaintsViewOwn, "View own complaints", "Complaints", "View complaints created by the member."),
        (Permissions.ComplaintsViewAll, "View all complaints", "Complaints", "View all complaints in the society."),
        (Permissions.ComplaintsAssign, "Assign complaints", "Complaints", "Assign and reassign complaints."),
        (Permissions.ComplaintsUpdateStatus, "Update complaint status", "Complaints", "Move complaints through the workflow."),
        (Permissions.ComplaintsEdit, "Edit complaints", "Complaints", "Edit complaint details."),
        (Permissions.ComplaintsDelete, "Delete complaints", "Complaints", "Delete complaints and their attachments."),
        (Permissions.CommentsEdit, "Edit comments", "Complaints", "Edit complaint comments and notes."),
        (Permissions.CommentsDelete, "Delete comments", "Complaints", "Delete complaint comments and notes."),
        (Permissions.ReportsView, "View reports", "Reporting", "View society performance reports."),
        (Permissions.ReportsExport, "Export reports", "Reporting", "Export reports to supported formats.")
    ];

    public static async Task EnsureSeededAsync(AppDbContext db)
    {
        var existing = await db.Permissions.Select(x => x.Key).ToListAsync();
        db.Permissions.AddRange(PermissionSeed.Where(x => !existing.Contains(x.Key)).Select(x => new Permission
        {
            Key = x.Key,
            Name = x.Name,
            Group = x.Group,
            Description = x.Description
        }));
        await db.SaveChangesAsync();
    }

    public static async Task EnsureTenantOperationalRolesAsync(AppDbContext db)
    {
        var permissions = await db.Permissions.ToListAsync();
        var tenants = await db.Tenants.Select(x => x.Id).ToListAsync();
        foreach (var tenantId in tenants)
        {
            var roles = await db.TenantRoles.Include(x => x.Permissions).ThenInclude(x => x.Permission).Where(x => x.TenantId == tenantId).ToListAsync();
            var resident = EnsureSystemRole(db, roles, tenantId, permissions, "Resident", "Create and follow personal complaints.",
                [Permissions.ComplaintsCreate, Permissions.ComplaintsViewOwn]);
            if (resident is not null)
            {
                var dashboard = resident.Permissions.SingleOrDefault(x => x.Permission.Key == Permissions.DashboardView);
                if (dashboard is not null) db.RolePermissions.Remove(dashboard);
            }
            EnsureSystemRole(db, roles, tenantId, permissions, "Society Administrator", "Full access to society configuration and operations.", PermissionSeed.Select(x => x.Key).ToArray());
            EnsureSystemRole(db, roles, tenantId, permissions, "Supervisor", "Manage assigned complaints, replies, and status updates.",
                [Permissions.DashboardView, Permissions.ComplaintsViewAll, Permissions.ComplaintsAssign, Permissions.ComplaintsUpdateStatus, Permissions.ComplaintsEdit, Permissions.CommentsEdit, Permissions.CommentsDelete, Permissions.ReportsView]);
            EnsureSystemRole(db, roles, tenantId, permissions, "Manager", "Oversee people, all complaints, routing, and performance reports.",
                [Permissions.DashboardView, Permissions.UsersView, Permissions.UsersManage, Permissions.SettingsView, Permissions.ComplaintsViewAll, Permissions.ComplaintsAssign, Permissions.ComplaintsUpdateStatus, Permissions.ComplaintsEdit, Permissions.ComplaintsDelete, Permissions.CommentsEdit, Permissions.CommentsDelete, Permissions.ReportsView, Permissions.ReportsExport]);
        }
        await db.SaveChangesAsync();
    }

    private static TenantRole EnsureSystemRole(AppDbContext db, List<TenantRole> roles, Guid tenantId, List<Permission> permissions, string name, string description, string[] keys)
    {
        var role = roles.SingleOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            role = new TenantRole { TenantId = tenantId, Name = name, Description = description, IsSystem = true, IsActive = true };
            roles.Add(role);
            db.TenantRoles.Add(role);
        }
        else
        {
            role.Name = name;
            role.IsSystem = true;
            role.IsActive = true;
            if (string.IsNullOrWhiteSpace(role.Description)) role.Description = description;
        }

        var existingKeys = role.Permissions.Select(x => x.Permission.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        db.RolePermissions.AddRange(permissions.Where(x => keys.Contains(x.Key) && !existingKeys.Contains(x.Key))
            .Select(x => new RolePermission { Role = role, Permission = x }));
        return role;
    }

    public static async Task<(TenantRole Admin, TenantRole Resident)> CreateTenantRolesAsync(
        AppDbContext db, Guid tenantId)
    {
        var permissions = await db.Permissions.ToListAsync();
        var admin = new TenantRole
        {
            TenantId = tenantId,
            Name = "Society Administrator",
            Description = "Full access to society configuration and operations.",
            IsSystem = true,
            CanReceiveAssignments = true
        };
        var resident = new TenantRole
        {
            TenantId = tenantId,
            Name = "Resident",
            Description = "Create and follow personal complaints.",
            IsSystem = true
        };
        var supervisor = new TenantRole
        {
            TenantId = tenantId,
            Name = "Supervisor",
            Description = "Manage assigned complaints, replies, and status updates.",
            IsSystem = true,
            CanReceiveAssignments = true
        };
        var manager = new TenantRole
        {
            TenantId = tenantId,
            Name = "Manager",
            Description = "Oversee people, all complaints, routing, and performance reports.",
            IsSystem = true,
            CanReceiveAssignments = true
        };

        db.TenantRoles.AddRange(admin, resident, supervisor, manager);
        db.RolePermissions.AddRange(permissions.Select(x => new RolePermission { Role = admin, Permission = x }));
        var residentKeys = new[] { Permissions.ComplaintsCreate, Permissions.ComplaintsViewOwn };
        db.RolePermissions.AddRange(permissions.Where(x => residentKeys.Contains(x.Key))
            .Select(x => new RolePermission { Role = resident, Permission = x }));
        var supervisorKeys = new[] { Permissions.DashboardView, Permissions.ComplaintsViewAll, Permissions.ComplaintsAssign, Permissions.ComplaintsUpdateStatus, Permissions.ComplaintsEdit, Permissions.CommentsEdit, Permissions.CommentsDelete, Permissions.ReportsView };
        db.RolePermissions.AddRange(permissions.Where(x => supervisorKeys.Contains(x.Key)).Select(x => new RolePermission { Role = supervisor, Permission = x }));
        var managerKeys = new[] { Permissions.DashboardView, Permissions.UsersView, Permissions.UsersManage, Permissions.SettingsView, Permissions.ComplaintsViewAll, Permissions.ComplaintsAssign, Permissions.ComplaintsUpdateStatus, Permissions.ComplaintsEdit, Permissions.ComplaintsDelete, Permissions.CommentsEdit, Permissions.CommentsDelete, Permissions.ReportsView, Permissions.ReportsExport };
        db.RolePermissions.AddRange(permissions.Where(x => managerKeys.Contains(x.Key)).Select(x => new RolePermission { Role = manager, Permission = x }));
        return (admin, resident);
    }

    public static ComplaintCategory[] CreateDefaultCategories(Guid tenantId) =>
    [
        Category(tenantId, "Water & Plumbing", "#2563EB", 24, ComplaintPriority.High),
        Category(tenantId, "Electricity & Lighting", "#D97706", 24, ComplaintPriority.High),
        Category(tenantId, "Cleaning & Waste", "#15803D", 48, ComplaintPriority.Normal),
        Category(tenantId, "Security", "#B91C1C", 4, ComplaintPriority.Urgent),
        Category(tenantId, "Lift & Common Areas", "#7C3AED", 24, ComplaintPriority.High),
        Category(tenantId, "Other", "#64748B", 72, ComplaintPriority.Normal)
    ];

    private static ComplaintCategory Category(Guid tenantId, string name, string color, int sla, ComplaintPriority priority) =>
        new() { TenantId = tenantId, Name = name, Color = color, DefaultSlaHours = sla, DefaultPriority = priority };
}
