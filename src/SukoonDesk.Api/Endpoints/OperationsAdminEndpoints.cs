using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography;
using SukoonDesk.Api.Domain;
using SukoonDesk.Api.Infrastructure;
using SukoonDesk.Api.Security;

namespace SukoonDesk.Api.Endpoints;

public static class OperationsAdminEndpoints
{
    public static IEndpointRouteBuilder MapOperationsAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var categories = app.MapGroup("/api/categories").RequireAuthorization().WithTags("Categories");
        categories.MapGet("/", ListCategoriesAsync);
        categories.MapPost("/", CreateCategoryAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.SettingsManage));
        categories.MapPut("/{id:guid}", UpdateCategoryAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.SettingsManage));
        categories.MapDelete("/{id:guid}", DeleteCategoryAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.SettingsManage));

        var rules = app.MapGroup("/api/assignment-rules").RequireAuthorization(PermissionPolicyProvider.For(Permissions.SettingsManage)).WithTags("Assignment rules");
        rules.MapGet("/", ListRulesAsync);
        rules.MapPost("/", CreateRuleAsync);
        rules.MapPut("/{id:guid}", UpdateRuleAsync);
        rules.MapDelete("/{id:guid}", DeleteRuleAsync);

        var members = app.MapGroup("/api/members").RequireAuthorization().WithTags("Members");
        members.MapGet("/", ListMembersAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.UsersView));
        members.MapGet("/eligible-assignees", EligibleAssigneesAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.ComplaintsAssign));
        members.MapGet("/roles", ListAssignableRolesAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.UsersManage));
        members.MapPost("/staff", CreateStaffAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.UsersManage));
        members.MapPost("/{id:guid}/credentials/email", EmailStaffCredentialsAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.UsersManage));
        members.MapPut("/{id:guid}/status", UpdateMemberStatusAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.UsersManage));
        members.MapPut("/{id:guid}/roles", UpdateMemberRolesAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.UsersManage));
        members.MapDelete("/{id:guid}", DeleteMemberAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.UsersManage));
        return app;
    }

    private static async Task<IResult> ListCategoriesAsync(ClaimsPrincipal principal, AppDbContext db, bool includeInactive = false)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        var query = db.ComplaintCategories.Where(x => x.TenantId == tenantId);
        if (!includeInactive) query = query.Where(x => x.IsActive);
        return Results.Ok(await query.OrderBy(x => x.Name).Select(x => new
        {
            x.Id, x.Name, x.Description, x.Color, x.DefaultSlaHours, x.DefaultPriority, x.IsActive,
            complaintCount = db.Complaints.Count(c => c.CategoryId == x.Id)
        }).ToListAsync());
    }

    private static async Task<IResult> CreateCategoryAsync(CategoryRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { message = "Category name is required." });
        if (await db.ComplaintCategories.AnyAsync(x => x.TenantId == tenantId && x.Name == request.Name.Trim())) return Results.Conflict(new { message = "Category already exists." });
        var category = new ComplaintCategory { TenantId = tenantId, Name = request.Name.Trim(), Description = request.Description.Trim(), Color = request.Color, DefaultSlaHours = Math.Clamp(request.DefaultSlaHours, 1, 2160), DefaultPriority = request.DefaultPriority, IsActive = request.IsActive };
        db.ComplaintCategories.Add(category);
        await db.SaveChangesAsync();
        return Results.Created($"/api/categories/{category.Id}", new { category.Id });
    }

    private static async Task<IResult> UpdateCategoryAsync(Guid id, CategoryRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        var category = await db.ComplaintCategories.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (category is null) return Results.NotFound();
        category.Name = request.Name.Trim(); category.Description = request.Description.Trim(); category.Color = request.Color;
        category.DefaultSlaHours = Math.Clamp(request.DefaultSlaHours, 1, 2160); category.DefaultPriority = request.DefaultPriority; category.IsActive = request.IsActive;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteCategoryAsync(Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        var category = await db.ComplaintCategories.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (category is null) return Results.NotFound();
        if (await db.Complaints.AnyAsync(x => x.CategoryId == id)) return Results.BadRequest(new { message = "Deactivate categories that already have complaints." });
        db.ComplaintCategories.Remove(category); await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> ListRulesAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        return Results.Ok(await db.AssignmentRules.Where(x => x.TenantId == tenantId).OrderBy(x => x.Order).Select(x => new
        {
            x.Id, x.Name, x.CategoryId, category = x.Category == null ? null : x.Category.Name,
            x.LocationContains, x.SupervisorMembershipId, supervisor = x.SupervisorMembership.User.DisplayName, x.Order, x.IsActive
        }).ToListAsync());
    }

    private static async Task<IResult> CreateRuleAsync(RuleRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        if (!await IsEligibleAssigneeAsync(db, tenantId, request.SupervisorMembershipId))
            return Results.BadRequest(new { message = "Choose an active member whose role is enabled for complaint assignments." });
        var rule = new AssignmentRule { TenantId = tenantId, Name = request.Name.Trim(), CategoryId = request.CategoryId, LocationContains = request.LocationContains.Trim(), SupervisorMembershipId = request.SupervisorMembershipId, Order = request.Order, IsActive = request.IsActive };
        db.AssignmentRules.Add(rule); await db.SaveChangesAsync(); return Results.Created($"/api/assignment-rules/{rule.Id}", new { rule.Id });
    }

    private static async Task<IResult> UpdateRuleAsync(Guid id, RuleRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        var rule = await db.AssignmentRules.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (rule is null) return Results.NotFound();
        if (!await IsEligibleAssigneeAsync(db, tenantId, request.SupervisorMembershipId))
            return Results.BadRequest(new { message = "Choose an active member whose role is enabled for complaint assignments." });
        rule.Name = request.Name.Trim(); rule.CategoryId = request.CategoryId; rule.LocationContains = request.LocationContains.Trim(); rule.SupervisorMembershipId = request.SupervisorMembershipId; rule.Order = request.Order; rule.IsActive = request.IsActive;
        await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> DeleteRuleAsync(Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        var rule = await db.AssignmentRules.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (rule is null) return Results.NotFound(); db.AssignmentRules.Remove(rule); await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> ListMembersAsync(ClaimsPrincipal principal, AppDbContext db, string? status)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        var query = db.TenantMemberships.Where(x => x.TenantId == tenantId && x.Status != MembershipStatus.Deleted);
        if (Enum.TryParse<MembershipStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed);
        return Results.Ok(await query.OrderBy(x => x.User.DisplayName).Select(x => new
        {
            x.Id, x.UserId, x.User.DisplayName,
            email = db.PublicMembers.Where(p => p.MembershipId == x.Id).Select(p => p.Email).FirstOrDefault() ?? x.User.Email,
            cnic = db.PublicMembers.Where(p => p.MembershipId == x.Id).Select(p => p.Cnic).FirstOrDefault(),
            x.User.PhoneNumber, x.UnitLabel, x.Status, x.JoinedAt,
            kind = db.MembershipRoles.Any(r => r.MembershipId == x.Id && r.Role.Name != "Resident") ? "Team" : "Resident",
            roles = db.MembershipRoles.Where(r => r.MembershipId == x.Id).Select(r => new { r.Role.Id, r.Role.Name }).ToArray()
        }).ToListAsync());
    }

    private static async Task<IResult> EligibleAssigneesAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        return Results.Ok(await db.TenantMemberships
            .Where(x => x.TenantId == tenantId && x.Status == MembershipStatus.Active &&
                db.MembershipRoles.Any(role => role.MembershipId == x.Id && role.Role.IsActive && role.Role.CanReceiveAssignments))
            .OrderBy(x => x.User.DisplayName)
            .Select(x => new { x.Id, x.User.DisplayName, x.User.Email, x.UnitLabel,
                roles = x.User == null ? Array.Empty<string>() : db.MembershipRoles.Where(role => role.MembershipId == x.Id).Select(role => role.Role.Name).ToArray() })
            .ToListAsync());
    }

    private static Task<bool> IsEligibleAssigneeAsync(AppDbContext db, Guid tenantId, Guid membershipId) =>
        db.TenantMemberships.AnyAsync(x => x.Id == membershipId && x.TenantId == tenantId &&
            x.Status == MembershipStatus.Active && db.MembershipRoles.Any(role =>
                role.MembershipId == x.Id && role.Role.IsActive && role.Role.CanReceiveAssignments));

    private static async Task<IResult> ListAssignableRolesAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        return Results.Ok(await db.TenantRoles.Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Description, x.IsSystem, x.IsActive, x.CanReceiveAssignments, memberCount = x.Members.Count, permissionKeys = x.Permissions.Select(p => p.Permission.Key).ToArray() })
            .ToListAsync());
    }

    private static async Task<IResult> CreateStaffAsync(
        StaffRegistrationRequest request,
        ClaimsPrincipal principal,
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender,
        CancellationToken cancellationToken)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Email))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["staff"] = ["Full name and email are required."] });
        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Enter a valid email address."] });
        if (!emailSender.IsConfigured) return Results.Problem("Configure SMTP before registering staff so login details can be delivered.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var roleIds = await db.TenantRoles.Where(x => x.TenantId == tenantId && x.IsActive && request.RoleIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken);
        if (roleIds.Count == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["roleIds"] = ["Select at least one active role."] });
        var tenant = await db.Tenants.SingleAsync(x => x.Id == tenantId, cancellationToken);
        var staffCount = await db.TenantMemberships.CountAsync(x => x.TenantId == tenantId && x.Status != MembershipStatus.Deleted && !db.PublicMembers.Any(publicMember => publicMember.MembershipId == x.Id), cancellationToken);
        if (staffCount >= tenant.StaffLimit)
            return Results.Problem("This society has reached its staff allowance. Ask the SukoonDesk administrator to update the subscription.", statusCode: StatusCodes.Status403Forbidden);
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await userManager.FindByEmailAsync(email);
        var previousMembership = user is null ? null : await db.TenantMemberships.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == user.Id, cancellationToken);
        if (previousMembership is not null && previousMembership.Status != MembershipStatus.Deleted)
            return Results.Conflict(new { message = "This person already belongs to the society. Open their access drawer to change roles." });

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var temporaryPassword = GenerateStaffPassword(tenant.Name);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, DisplayName = request.FullName.Trim(), PhoneNumber = request.PhoneNumber?.Trim(), EmailConfirmed = true, MustChangePassword = true };
            var created = await userManager.CreateAsync(user, temporaryPassword);
            if (!created.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.ValidationProblem(created.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray()));
            }
        }
        else
        {
            user.DisplayName = request.FullName.Trim();
            user.PhoneNumber = request.PhoneNumber?.Trim();
            user.IsActive = true;
            user.MustChangePassword = true;
            var updated = await userManager.UpdateAsync(user);
            if (!updated.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.ValidationProblem(updated.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray()));
            }
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            var reset = await userManager.ResetPasswordAsync(user, resetToken, temporaryPassword);
            if (!reset.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.ValidationProblem(reset.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray()));
            }
        }

        var membership = previousMembership ?? new TenantMembership { TenantId = tenantId, UserId = user.Id };
        membership.UnitLabel = request.UnitLabel?.Trim() ?? string.Empty;
        membership.Status = MembershipStatus.Active;
        membership.JoinedAt = DateTimeOffset.UtcNow;
        if (previousMembership is null) db.TenantMemberships.Add(membership);
        else db.MembershipRoles.RemoveRange(await db.MembershipRoles.Where(x => x.MembershipId == membership.Id).ToListAsync(cancellationToken));
        db.MembershipRoles.AddRange(roleIds.Select(roleId => new MembershipRole { Membership = membership, RoleId = roleId }));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.Conflict(new { message = "This email already has a society membership. Refresh the people list and update its roles instead." });
        }

        var safeName = System.Net.WebUtility.HtmlEncode(user.DisplayName);
        var safeSociety = System.Net.WebUtility.HtmlEncode(tenant.Name);
        try
        {
            await emailSender.SendAsync(email, $"Your {tenant.Name} staff access on SukoonDesk", StaffCredentialsEmail(safeName, safeSociety, tenant.Slug, email, temporaryPassword), cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.Problem("The staff invitation email could not be delivered, so the registration was not saved.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        await transaction.CommitAsync(cancellationToken);
        return Results.Created($"/api/members/{membership.Id}", new { membership.Id, message = "Staff member registered and credentials emailed.", fullName = user.DisplayName, email, phoneNumber = user.PhoneNumber, societyName = tenant.Name, societySlug = tenant.Slug, temporaryPassword });
    }

    private static async Task<IResult> EmailStaffCredentialsAsync(Guid id, StaffCredentialsEmailRequest request, ClaimsPrincipal principal, AppDbContext db, IEmailSender emailSender, CancellationToken cancellationToken)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        if (!emailSender.IsConfigured) return Results.Problem("SMTP is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        var member = await db.TenantMemberships.Include(x => x.User).Include(x => x.Tenant).SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);
        if (member is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.TemporaryPassword)) return Results.BadRequest(new { message = "Temporary password is required." });
        await emailSender.SendAsync(member.User.Email!, $"Your {member.Tenant.Name} staff access on SukoonDesk", StaffCredentialsEmail(System.Net.WebUtility.HtmlEncode(member.User.DisplayName), System.Net.WebUtility.HtmlEncode(member.Tenant.Name), member.Tenant.Slug, member.User.Email!, request.TemporaryPassword), cancellationToken);
        return Results.Ok(new { message = "Credentials email sent successfully." });
    }

    private static string GenerateStaffPassword(string societyName)
    {
        var words = societyName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rawPrefix = words.Length > 1 ? string.Concat(words.Select(x => x[0])) : new string(societyName.Where(char.IsLetterOrDigit).Take(4).ToArray());
        var prefix = new string(rawPrefix.Where(char.IsLetterOrDigit).Take(4).ToArray()).ToUpperInvariant();
        if (prefix.Length == 0) prefix = "SK";
        const string lower = "abcdefghjkmnpqrstuvwxyz", digits = "23456789";
        var value = prefix + lower[RandomNumberGenerator.GetInt32(lower.Length)];
        while (value.Length < 8) value += digits[RandomNumberGenerator.GetInt32(digits.Length)];
        return value[..8];
    }

    private static string StaffCredentialsEmail(string safeName, string safeSociety, string societySlug, string email, string password) =>
        $"<div style=\"font-family:Arial,sans-serif;max-width:580px;margin:auto;color:#0f2f3a\"><div style=\"background:#155e75;color:white;padding:26px 30px;border-radius:16px 16px 0 0\"><h1 style=\"margin:0\">SukoonDesk</h1><p style=\"margin:8px 0 0;color:#cffafe\">Staff access credentials</p></div><div style=\"border:1px solid #dbe5e8;border-top:0;padding:30px;border-radius:0 0 16px 16px;line-height:1.6\"><p>Hello {safeName},</p><p>You have been added to <strong>{safeSociety}</strong>. Use these credentials on staff sign-in:</p><div style=\"background:#f0f9fa;border:1px solid #bae6eb;border-radius:12px;padding:18px 20px;margin:22px 0\"><div style=\"color:#64748b;font-size:12px\">Society URL</div><strong>{System.Net.WebUtility.HtmlEncode(societySlug)}</strong><div style=\"color:#64748b;font-size:12px;margin-top:12px\">Email</div><strong>{System.Net.WebUtility.HtmlEncode(email)}</strong><div style=\"color:#64748b;font-size:12px;margin-top:12px\">Temporary password</div><strong style=\"font:700 20px Consolas,monospace;color:#155e75;letter-spacing:1px\">{System.Net.WebUtility.HtmlEncode(password)}</strong></div><p style=\"font-size:13px;color:#64748b\">For security, sign in and change this password from Profile settings.</p></div></div>";

    private static async Task<IResult> UpdateMemberStatusAsync(Guid id, MemberStatusRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, currentMembershipId) = ComplaintEndpoints.Context(principal);
        if (id == currentMembershipId && request.Status != MembershipStatus.Active) return Results.BadRequest(new { message = "You cannot suspend your own membership." });
        var member = await db.TenantMemberships.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (member is null) return Results.NotFound(); member.Status = request.Status; await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> UpdateMemberRolesAsync(Guid id, MemberRolesRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = ComplaintEndpoints.Context(principal);
        var member = await db.TenantMemberships.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (member is null) return Results.NotFound();
        var validRoleIds = await db.TenantRoles.Where(x => x.TenantId == tenantId && request.RoleIds.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        var existing = await db.MembershipRoles.Where(x => x.MembershipId == id).ToListAsync();
        db.MembershipRoles.RemoveRange(existing); db.MembershipRoles.AddRange(validRoleIds.Select(roleId => new MembershipRole { MembershipId = id, RoleId = roleId }));
        await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> DeleteMemberAsync(Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, currentMembershipId) = ComplaintEndpoints.Context(principal);
        if (id == currentMembershipId) return Results.BadRequest(new { message = "You cannot delete your own membership." });
        var member = await db.TenantMemberships.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (member is null) return Results.NotFound();

        member.Status = MembershipStatus.Deleted;
        db.MembershipRoles.RemoveRange(await db.MembershipRoles.Where(x => x.MembershipId == id).ToListAsync());
        var publicMember = await db.PublicMembers.SingleOrDefaultAsync(x => x.MembershipId == id);
        if (publicMember is not null) publicMember.IsActive = false;
        await db.Complaints.Where(x => x.TenantId == tenantId && x.AssignedToMembershipId == id)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.AssignedToMembershipId, (Guid?)null));
        await db.AssignmentRules.Where(x => x.TenantId == tenantId && x.SupervisorMembershipId == id)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.IsActive, false));
        var hasAnotherMembership = await db.TenantMemberships.AnyAsync(x => x.UserId == member.UserId && x.Id != id && x.Status != MembershipStatus.Deleted);
        if (!hasAnotherMembership) member.User.IsActive = false;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}

public sealed record CategoryRequest(string Name, string Description, string Color, int DefaultSlaHours, ComplaintPriority DefaultPriority, bool IsActive);
public sealed record RuleRequest(string Name, Guid? CategoryId, string LocationContains, Guid SupervisorMembershipId, int Order, bool IsActive);
public sealed record MemberStatusRequest(MembershipStatus Status);
public sealed record MemberRolesRequest(Guid[] RoleIds);
public sealed record StaffRegistrationRequest(string FullName, string Email, string? PhoneNumber, string? UnitLabel, Guid[] RoleIds);
public sealed record StaffCredentialsEmailRequest(string TemporaryPassword);
