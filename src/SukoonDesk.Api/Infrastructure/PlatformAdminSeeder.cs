using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SukoonDesk.Api.Domain;

namespace SukoonDesk.Api.Infrastructure;

public static class PlatformAdminSeeder
{
    public static async Task EnsureSeededAsync(IServiceProvider services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>("PlatformAdmin:Seed:Enabled")) return;
        var email = configuration["PlatformAdmin:Seed:Email"]?.Trim();
        var password = configuration["PlatformAdmin:Seed:Password"];
        var slug = configuration["PlatformAdmin:Seed:SocietySlug"]?.Trim().ToLowerInvariant() ?? "sukoondesk-admin";
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Platform administrator seeding requires Email and Password.");
        if (!environment.IsDevelopment() && password == "Sukoon@123")
            throw new InvalidOperationException("The development platform administrator password cannot be used in production.");

        var db = services.GetRequiredService<AppDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Slug == slug);
        if (tenant is null)
        {
            tenant = new Tenant { Name = "SukoonDesk Administration", Slug = slug, IsActive = true };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            var (adminRole, _) = await SeedData.CreateTenantRolesAsync(db, tenant.Id);
            db.ComplaintCategories.AddRange(SeedData.CreateDefaultCategories(tenant.Id));
            await db.SaveChangesAsync();
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, DisplayName = "SukoonDesk Administrator", EmailConfirmed = true, IsActive = true, IsPlatformAdmin = true };
            var created = await userManager.CreateAsync(user, password);
            if (!created.Succeeded) throw new InvalidOperationException(string.Join("; ", created.Errors.Select(x => x.Description)));
        }
        else
        {
            user.IsPlatformAdmin = true;
            user.IsActive = true;
            user.EmailConfirmed = true;
            var updated = await userManager.UpdateAsync(user);
            if (!updated.Succeeded) throw new InvalidOperationException(string.Join("; ", updated.Errors.Select(x => x.Description)));

            if (!await userManager.CheckPasswordAsync(user, password))
            {
                var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
                var reset = await userManager.ResetPasswordAsync(user, resetToken, password);
                if (!reset.Succeeded) throw new InvalidOperationException(string.Join("; ", reset.Errors.Select(x => x.Description)));
            }
        }

        var membership = await db.TenantMemberships.SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.UserId == user.Id);
        if (membership is null)
        {
            membership = new TenantMembership { TenantId = tenant.Id, UserId = user.Id, Status = MembershipStatus.Active, UnitLabel = "Platform operations" };
            db.TenantMemberships.Add(membership);
            var role = await db.TenantRoles.SingleAsync(x => x.TenantId == tenant.Id && x.Name == "Society Administrator");
            db.MembershipRoles.Add(new MembershipRole { Membership = membership, RoleId = role.Id });
        }
        else membership.Status = MembershipStatus.Active;
        await db.SaveChangesAsync();
    }
}
