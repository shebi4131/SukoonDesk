using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SukoonDesk.Api.Domain;
using SukoonDesk.Api.Infrastructure;

namespace SukoonDesk.Api.Endpoints;

public static partial class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication").RequireRateLimiting("authentication");
        group.MapPost("/sign-up", SignUpAsync).AllowAnonymous();
        group.MapGet("/approve-society", ApproveSocietyAsync).AllowAnonymous();
        group.MapPost("/sign-in", SignInAsync).AllowAnonymous();
        group.MapPost("/forgot-password", ForgotPasswordAsync).AllowAnonymous();
        group.MapPost("/sign-out", SignOutAsync).RequireAuthorization();
        group.MapGet("/me", MeAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> SignUpAsync(
        SignUpRequest request,
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender,
        IDataProtectionProvider dataProtectionProvider,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (!emailSender.IsConfigured || string.IsNullOrWhiteSpace(emailSender.FromEmail))
            return Results.Problem("Society requests require configured email delivery and a sender/review email.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var slug = request.SocietySlug.Trim().ToLowerInvariant();
        if (await db.Tenants.AnyAsync(x => x.Slug == slug))
            return Results.Conflict(new { message = "That society URL is already in use." });
        if (await userManager.FindByEmailAsync(request.Email.Trim()) is not null)
            return Results.Conflict(new { message = "An account with this email already exists." });

        await using var transaction = await db.Database.BeginTransactionAsync();
        var tenant = new Tenant { Name = request.SocietyName.Trim(), Slug = slug, IsActive = false };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.FullName.Trim(),
            EmailConfirmed = true
        };
        var identityResult = await userManager.CreateAsync(user, request.Password);
        if (!identityResult.Succeeded)
        {
            await transaction.RollbackAsync();
            return Results.ValidationProblem(identityResult.Errors
                .GroupBy(x => x.Code)
                .ToDictionary(x => x.Key, x => x.Select(error => error.Description).ToArray()));
        }

        var membership = new TenantMembership { TenantId = tenant.Id, UserId = user.Id, Status = MembershipStatus.Pending };
        db.TenantMemberships.Add(membership);
        var (adminRole, _) = await SeedData.CreateTenantRolesAsync(db, tenant.Id);
        db.MembershipRoles.Add(new MembershipRole { Membership = membership, Role = adminRole });
        db.ComplaintCategories.AddRange(SeedData.CreateDefaultCategories(tenant.Id));
        db.AppSettings.AddRange(
            new AppSetting { TenantId = tenant.Id, Key = "society.name", Value = tenant.Name, IsPublic = true, Description = "Public society name" },
            new AppSetting { TenantId = tenant.Id, Key = "complaints.defaultSlaHours", Value = "48", Description = "Default complaint resolution SLA" },
            new AppSetting { TenantId = tenant.Id, Key = "registration.residentApproval", Value = "true", Description = "Require approval for resident registration" });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        var protector = dataProtectionProvider.CreateProtector("SukoonDesk.SocietyApproval.v1").ToTimeLimitedDataProtector();
        var token = protector.Protect($"{tenant.Id:N}|{user.Id:N}", TimeSpan.FromDays(7));
        var approvalUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/api/auth/approve-society?token={Uri.EscapeDataString(token)}";
        var safeName = System.Net.WebUtility.HtmlEncode(user.DisplayName);
        var safeSociety = System.Net.WebUtility.HtmlEncode(tenant.Name);
        var safeEmail = System.Net.WebUtility.HtmlEncode(user.Email);
        try
        {
            await emailSender.SendAsync(user.Email!, $"Your SukoonDesk request was received - {tenant.Name}", EmailCard("Society request received", $"Hello {safeName},<br><br>Your request to create <strong>{safeSociety}</strong> has been sent for review. Staff sign-in remains disabled until approval. We will email you as soon as it is approved."), cancellationToken);
            await emailSender.SendAsync(emailSender.FromEmail, $"Society approval required - {tenant.Name}", EmailCard("Review a new society", $"<strong>{safeSociety}</strong> was requested by {safeName} ({safeEmail}).<br><br><a href=\"{approvalUrl}\" style=\"display:inline-block;background:#155e75;color:#fff;text-decoration:none;padding:12px 20px;border-radius:9px;font-weight:700\">Approve society</a><br><br><span style=\"font-size:12px;color:#64748b\">This secure link expires in 7 days.</span>"), cancellationToken);
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("SocietyApproval").LogError(exception, "Society {TenantId} was created pending approval, but an approval email could not be delivered.", tenant.Id);
            return Results.Problem("Your request was saved, but the notification email could not be delivered. Please contact SukoonDesk support.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        return Results.Accepted(value: new { pendingApproval = true, message = "Your society request has been sent for approval. Check your email for confirmation." });
    }

    private static async Task<IResult> ApproveSocietyAsync(
        string token,
        AppDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        IEmailSender emailSender,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var protector = dataProtectionProvider.CreateProtector("SukoonDesk.SocietyApproval.v1").ToTimeLimitedDataProtector();
            var payload = protector.Unprotect(token, out _).Split('|');
            if (payload.Length != 2 || !Guid.TryParseExact(payload[0], "N", out var tenantId) || !Guid.TryParseExact(payload[1], "N", out var userId))
                return ApprovalPage("Invalid approval link", "This approval link is not valid.", false);
            var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
            var membership = await db.TenantMemberships.Include(x => x.User).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, cancellationToken);
            if (tenant is null || membership is null) return ApprovalPage("Request not found", "This society request no longer exists.", false);
            tenant.IsActive = true;
            membership.Status = MembershipStatus.Active;
            db.Notifications.Add(new Notification { TenantId = tenant.Id, MembershipId = membership.Id, Title = "Society approved", Message = $"{tenant.Name} is active. Welcome to your SukoonDesk workspace.", Type = "success", Link = "/app" });
            await db.SaveChangesAsync(cancellationToken);
            try
            {
                var safeName = System.Net.WebUtility.HtmlEncode(membership.User.DisplayName);
                var safeSociety = System.Net.WebUtility.HtmlEncode(tenant.Name);
                await emailSender.SendAsync(membership.User.Email!, $"Your SukoonDesk society is approved - {tenant.Name}", EmailCard("Society approved", $"Hello {safeName},<br><br><strong>{safeSociety}</strong> is approved. You can now use staff sign-in with your society URL, email, and password."), cancellationToken);
            }
            catch (Exception exception) { loggerFactory.CreateLogger("SocietyApproval").LogError(exception, "Approval email failed for tenant {TenantId}.", tenant.Id); }
            return ApprovalPage("Society approved", $"{System.Net.WebUtility.HtmlEncode(tenant.Name)} is now active. The administrator can sign in.", true);
        }
        catch (Exception) { return ApprovalPage("Approval link expired", "This link is invalid or has expired. Ask the requester to contact SukoonDesk support.", false); }
    }

    private static IResult ApprovalPage(string title, string message, bool success) => Results.Content($"""
        <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>{title}</title></head><body style="margin:0;background:#f4f7f8;font-family:Arial,sans-serif;color:#0f2f3a"><main style="min-height:100vh;display:grid;place-items:center;padding:24px"><section style="width:min(480px,100%);background:#fff;border:1px solid #dbe5e8;border-radius:18px;padding:36px;box-shadow:0 24px 60px rgba(15,47,58,.12);text-align:center"><div style="width:58px;height:58px;margin:auto;border-radius:16px;display:grid;place-items:center;background:{(success ? "#ecfdf5" : "#fef2f2")};color:{(success ? "#15803d" : "#b91c1c")};font-size:28px">{(success ? "✓" : "!")}</div><h1 style="margin:22px 0 10px">{title}</h1><p style="color:#64748b;line-height:1.6">{message}</p></section></main></body></html>
        """, "text/html");

    private static string EmailCard(string title, string body) => $"""
        <div style="font-family:Arial,sans-serif;max-width:560px;margin:auto;color:#0f2f3a"><div style="background:#155e75;color:white;padding:24px 28px;border-radius:14px 14px 0 0"><h1 style="margin:0;font-size:24px">SukoonDesk</h1><p style="margin:6px 0 0;color:#cffafe">{title}</p></div><div style="border:1px solid #dbe5e8;border-top:0;padding:28px;border-radius:0 0 14px 14px;line-height:1.6">{body}</div></div>
        """;

    private static async Task<IResult> SignInAsync(
        SignInRequest request,
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Slug == request.SocietySlug.Trim().ToLower());
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (tenant is null || user is null || !user.IsActive || !await userManager.CheckPasswordAsync(user, request.Password))
            return Results.Unauthorized();
        if (!tenant.IsActive) return Results.Json(new { message = "This society request is awaiting approval. We will email the administrator when it is approved." }, statusCode: StatusCodes.Status403Forbidden);

        var membership = await db.TenantMemberships.SingleOrDefaultAsync(x =>
            x.TenantId == tenant.Id && x.UserId == user.Id && x.Status == MembershipStatus.Active);
        if (membership is null) return Results.Unauthorized();

        await signInManager.SignOutAsync();
        await SignInForTenantAsync(signInManager, user, tenant, membership, request.RememberMe);
        return Results.Ok(await BuildCurrentUserAsync(db, user.Id, tenant.Id, configuration));
    }

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        const string successMessage = "If that email belongs to this society, a new temporary password has been sent.";
        if (!emailSender.IsConfigured)
            return Results.Problem("Email delivery is not configured. Add the SMTP settings before using password recovery.", statusCode: StatusCodes.Status503ServiceUnavailable);

        var slug = request.SocietySlug?.Trim().ToLowerInvariant() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;
        if (slug.Length < 3 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            return Results.Ok(new { message = successMessage });

        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug && x.IsActive, cancellationToken);
        if (tenant is null) return Results.Ok(new { message = successMessage });

        var normalizedEmail = userManager.NormalizeEmail(email);
        var membership = await db.TenantMemberships.Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.Status != MembershipStatus.Deleted && x.User.NormalizedEmail == normalizedEmail, cancellationToken);
        var publicMember = membership is null
            ? await db.PublicMembers.Include(x => x.Membership).ThenInclude(x => x.User)
                .SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.IsActive && x.Email.ToLower() == email.ToLower(), cancellationToken)
            : null;
        var user = membership?.User ?? publicMember?.Membership.User;
        if (user is null || !user.IsActive) return Results.Ok(new { message = successMessage });

        var temporaryPassword = GenerateTemporaryPassword();
        var oldPasswordHash = user.PasswordHash;
        var oldSecurityStamp = user.SecurityStamp;
        var oldConcurrencyStamp = user.ConcurrencyStamp;
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await userManager.ResetPasswordAsync(user, token, temporaryPassword);
        if (!reset.Succeeded)
            return Results.Problem("A temporary password could not be generated. Please contact your society administrator.", statusCode: StatusCodes.Status500InternalServerError);

        try
        {
            var safeName = System.Net.WebUtility.HtmlEncode(user.DisplayName);
            var safeSociety = System.Net.WebUtility.HtmlEncode(tenant.Name);
            var safePassword = System.Net.WebUtility.HtmlEncode(temporaryPassword);
            var html = $"""
                <div style="font-family:Arial,sans-serif;max-width:560px;margin:auto;color:#0f2f3a">
                  <div style="background:#155e75;color:white;padding:24px 28px;border-radius:14px 14px 0 0"><h1 style="margin:0;font-size:24px">SukoonDesk</h1><p style="margin:6px 0 0;color:#cffafe">Password recovery</p></div>
                  <div style="border:1px solid #dbe5e8;border-top:0;padding:28px;border-radius:0 0 14px 14px"><p>Hello {safeName},</p><p>A password reset was requested for your {safeSociety} account.</p><p style="margin:24px 0 8px;color:#64748b">Your new temporary password</p><div style="font:700 22px Consolas,monospace;letter-spacing:2px;background:#ecfeff;border:1px solid #a5f3fc;padding:16px;text-align:center;border-radius:10px">{safePassword}</div><p style="margin-top:24px">Sign in using this password. Keep it private and update it after signing in.</p><p style="font-size:12px;color:#64748b">If you did not request this, contact your society administrator immediately.</p></div>
                </div>
                """;
            await emailSender.SendAsync(email, $"Your SukoonDesk password - {tenant.Name}", html, cancellationToken);
        }
        catch (Exception exception)
        {
            await db.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(update => update
                .SetProperty(x => x.PasswordHash, oldPasswordHash)
                .SetProperty(x => x.SecurityStamp, oldSecurityStamp)
                .SetProperty(x => x.ConcurrencyStamp, oldConcurrencyStamp), cancellationToken);
            loggerFactory.CreateLogger("PasswordRecovery").LogError(exception, "Could not deliver password recovery email for tenant {TenantId}.", tenant.Id);
            return Results.Problem("The password email could not be delivered. Please try again or contact your administrator.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new { message = successMessage });
    }

    private static async Task<IResult> JoinSocietyAsync(
        JoinSocietyRequest request,
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Slug == request.SocietySlug.Trim().ToLower() && x.IsActive);
        if (tenant is null) return Results.NotFound(new { message = "Society not found." });
        if (await userManager.FindByEmailAsync(request.Email.Trim()) is not null)
            return Results.Conflict(new { message = "This email already has an account. Ask an administrator to add it to the society." });

        var user = new ApplicationUser { UserName = request.Email.Trim(), Email = request.Email.Trim(), DisplayName = request.FullName.Trim(), EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded) return Results.ValidationProblem(result.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()));

        var approvalSetting = await db.AppSettings.SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.Key == "registration.residentApproval");
        var requiresApproval = approvalSetting is null || !bool.TryParse(approvalSetting.Value, out var parsed) || parsed;
        var membership = new TenantMembership
        {
            TenantId = tenant.Id, UserId = user.Id, UnitLabel = request.UnitLabel.Trim(),
            Status = requiresApproval ? MembershipStatus.Pending : MembershipStatus.Active
        };
        db.TenantMemberships.Add(membership);
        var residentRole = await db.TenantRoles.SingleAsync(x => x.TenantId == tenant.Id && x.Name == "Resident");
        db.MembershipRoles.Add(new MembershipRole { Membership = membership, RoleId = residentRole.Id });
        await db.SaveChangesAsync();

        if (requiresApproval) return Results.Accepted(value: new { pendingApproval = true, message = "Registration submitted for society approval." });
        await SignInForTenantAsync(signInManager, user, tenant, membership);
        return Results.Ok(await BuildCurrentUserAsync(db, user.Id, tenant.Id, configuration));
    }

    private static async Task<IResult> SignOutAsync(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenantId))
            return Results.Unauthorized();
        return Results.Ok(await BuildCurrentUserAsync(db, userId, tenantId, configuration));
    }

    private static async Task SignInForTenantAsync(
        SignInManager<ApplicationUser> signInManager,
        ApplicationUser user,
        Tenant tenant,
        TenantMembership membership,
        bool persistent = true)
    {
        Claim[] claims =
        [
            new("tenant_id", tenant.Id.ToString()),
            new("tenant_slug", tenant.Slug),
            new("membership_id", membership.Id.ToString()),
            new("display_name", user.DisplayName)
        ];
        await signInManager.SignInWithClaimsAsync(user, persistent, claims);
    }

    private static async Task<object> BuildCurrentUserAsync(AppDbContext db, Guid userId, Guid tenantId, IConfiguration configuration)
    {
        var membership = await db.TenantMemberships
            .Include(x => x.User)
            .Include(x => x.Tenant)
            .SingleAsync(x => x.UserId == userId && x.TenantId == tenantId);
        var roles = await db.MembershipRoles
            .Where(x => x.MembershipId == membership.Id)
            .Select(x => x.Role.Name)
            .ToListAsync();
        var permissions = await db.MembershipRoles
            .Where(x => x.MembershipId == membership.Id)
            .SelectMany(x => x.Role.Permissions)
            .Select(x => x.Permission.Key)
            .Distinct()
            .ToListAsync();
        return new
        {
            id = membership.UserId,
            membershipId = membership.Id,
            membership.User.DisplayName,
            membership.User.Email,
            membership.User.MustChangePassword,
            isPlatformAdmin = await PlatformAdminEndpoints.IsPlatformAdminAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, membership.UserId.ToString())])), db, configuration),
            tenant = new { membership.TenantId, membership.Tenant.Name, membership.Tenant.Slug },
            roles,
            permissions
        };
    }

    private static Dictionary<string, string[]> Validate(SignUpRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.FullName)) errors["fullName"] = ["Full name is required."];
        if (string.IsNullOrWhiteSpace(request.SocietyName)) errors["societyName"] = ["Society name is required."];
        if (!SlugPattern().IsMatch(request.SocietySlug ?? string.Empty))
            errors["societySlug"] = ["Use 3–40 lowercase letters, numbers or hyphens."];
        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email))
            errors["email"] = ["Enter a valid email address."];
        return errors;
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,38}[a-z0-9])?$")]
    private static partial Regex SlugPattern();

    private static string GenerateTemporaryPassword()
    {
        const string lower = "abcdefghjkmnpqrstuvwxyz";
        const string upper = "ABCDEFGHJKMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string symbols = "!@#$%";
        const string all = lower + upper + digits + symbols;
        var characters = new List<char>
        {
            lower[RandomNumberGenerator.GetInt32(lower.Length)],
            upper[RandomNumberGenerator.GetInt32(upper.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)],
            symbols[RandomNumberGenerator.GetInt32(symbols.Length)]
        };
        while (characters.Count < 12) characters.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);
        for (var index = characters.Count - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (characters[index], characters[swap]) = (characters[swap], characters[index]);
        }
        return new string(characters.ToArray());
    }
}

public sealed record SignUpRequest(string FullName, string Email, string Password, string SocietyName, string SocietySlug);
public sealed record SignInRequest(string Email, string Password, string SocietySlug, bool RememberMe = true);
public sealed record JoinSocietyRequest(string FullName, string Email, string Password, string SocietySlug, string UnitLabel);
public sealed record ForgotPasswordRequest(string SocietySlug, string Email);
