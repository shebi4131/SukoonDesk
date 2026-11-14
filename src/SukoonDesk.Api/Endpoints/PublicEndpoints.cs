using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SukoonDesk.Api.Domain;
using SukoonDesk.Api.Infrastructure;

namespace SukoonDesk.Api.Endpoints;

public static class PublicEndpoints
{
    private const string CookieName = "sukoondesk.public";
    private static readonly string[] AllowedImageTypes = ["image/jpeg", "image/png", "image/webp"];

    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public").AllowAnonymous().WithTags("Public complaints").RequireRateLimiting("public");
        group.MapGet("/societies", SearchSocietiesAsync);
        group.MapGet("/societies/{slug}", SocietyAsync);
        group.MapPost("/register", RegisterDirectAsync);
        group.MapPost("/sign-in", SignInDirectAsync);
        group.MapPost("/societies/{slug}/register", RegisterAsync);
        group.MapPost("/societies/{slug}/sign-in", SignInAsync);
        group.MapPost("/sign-out", SignOut);
        group.MapGet("/me", MeAsync);
        group.MapGet("/complaints", ListAsync);
        group.MapPost("/complaints", CreateComplaintAsync);
        group.MapGet("/complaints/{number}", GetComplaintAsync);
        group.MapPost("/complaints/{number}/comments", AddCommentAsync);
        group.MapPost("/complaints/{number}/attachments", UploadComplaintAttachmentAsync).DisableAntiforgery();
        group.MapPost("/complaints/{number}/comments/{commentId:guid}/attachments", UploadCommentAttachmentAsync).DisableAntiforgery();
        app.MapGet("/api/files/{id:guid}", DownloadAttachmentAsync).AllowAnonymous();
        return app;
    }

    private static async Task<IResult> SearchSocietiesAsync(string? query, AppDbContext db, IConfiguration configuration)
    {
        var term = query?.Trim().ToLowerInvariant() ?? string.Empty;
        var platformSlug = configuration["PlatformAdmin:Seed:SocietySlug"]?.Trim().ToLowerInvariant() ?? "sukoondesk-admin";
        var societiesQuery = db.Tenants.AsNoTracking()
            .Where(x => x.IsActive && x.SubscriptionStatus != SubscriptionStatus.Suspended && x.SubscriptionStatus != SubscriptionStatus.Expired && x.Slug != platformSlug);
        if (term.Length > 0)
            societiesQuery = societiesQuery.Where(x => x.Name.ToLower().Contains(term) || x.Slug.ToLower().Contains(term));
        var matches = await societiesQuery
            .OrderByDescending(x => x.Name.ToLower() == term || x.Slug == term)
            .ThenByDescending(x => x.Name.ToLower().StartsWith(term) || x.Slug.StartsWith(term))
            .ThenBy(x => x.Name)
            .Take(100)
            .Select(x => new { x.Id, x.Name, x.Slug })
            .ToListAsync();

        return Results.Ok(matches);
    }

    private static async Task<IResult> SocietyAsync(string slug, AppDbContext db, IConfiguration configuration)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug.ToLower() && x.IsActive);
        var platformSlug = configuration["PlatformAdmin:Seed:SocietySlug"]?.Trim().ToLowerInvariant() ?? "sukoondesk-admin";
        if (tenant is null || tenant.Slug == platformSlug || !TenantAccess.IsAvailable(tenant))
            return Results.NotFound(new { message = "Society not found." });
        var categories = await db.ComplaintCategories.AsNoTracking().Where(x => x.TenantId == tenant.Id && x.IsActive)
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Description, x.Color, x.DefaultSlaHours }).ToListAsync();
        var projects = await db.TenantProjects.AsNoTracking().Where(x => x.TenantId == tenant.Id && x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Code, x.Description }).ToListAsync();
        return Results.Ok(new { tenant.Id, tenant.Name, tenant.Slug, categories, projects });
    }

    private static async Task<IResult> RegisterAsync(string slug, PublicRegistrationRequest request, HttpContext http,
        AppDbContext db, UserManager<ApplicationUser> userManager, IDataProtectionProvider protection)
        => await RegisterCoreAsync(slug, request, db, userManager);

    private static async Task<IResult> RegisterDirectAsync(PublicRegistrationRequest request,
        AppDbContext db, UserManager<ApplicationUser> userManager)
        => await RegisterCoreAsync(request.SocietySlug ?? string.Empty, request, db, userManager);

    private static async Task<IResult> RegisterCoreAsync(string slug, PublicRegistrationRequest request,
        AppDbContext db, UserManager<ApplicationUser> userManager)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Slug == slug.ToLower() && x.IsActive);
        if (tenant is null) return Results.NotFound(new { message = "Society not found." });
        if (!TenantAccess.IsAvailable(tenant)) return Results.StatusCode(StatusCodes.Status402PaymentRequired);
        if (await db.PublicMembers.CountAsync(x => x.TenantId == tenant.Id && x.IsActive) >= tenant.ResidentLimit)
            return Results.Problem("This society has reached its resident allowance. Contact the society administrator.", statusCode: StatusCodes.Status403Forbidden);
        var fullName = request.FullName?.Trim() ?? string.Empty;
        var cnic = Digits(request.Cnic);
        var phone = Digits(request.Phone);
        var whatsappNumber = Digits(request.WhatsAppNumber);
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var plotNo = request.PlotNo?.Trim() ?? string.Empty;
        var streetNo = request.StreetNo?.Trim() ?? string.Empty;
        var errors = new Dictionary<string, string[]>();
        if (fullName.Length < 2) errors["fullName"] = ["Enter the member's full name."];
        if (cnic.Length != 13) errors["cnic"] = ["CNIC must contain exactly 13 digits."];
        if (phone.Length is < 10 or > 13) errors["phone"] = ["Enter a valid phone number including its country or local prefix."];
        if (Digits(whatsappNumber).Length is < 10 or > 13) errors["whatsAppNumber"] = ["Enter a valid WhatsApp number including its country or local prefix."];
        if (!new EmailAddressAttribute().IsValid(email)) errors["email"] = ["Enter a valid email address."];
        if (plotNo.Length == 0) errors["plotNo"] = ["Plot number is required."];
        if (streetNo.Length == 0) errors["streetNo"] = ["Street number is required."];
        var project = request.ProjectId.HasValue
            ? await db.TenantProjects.SingleOrDefaultAsync(x => x.Id == request.ProjectId && x.TenantId == tenant.Id && x.IsActive)
            : null;
        if (project is null) errors["projectId"] = ["Select a valid project or phase."];
        if (!request.NotifyByEmail && !request.NotifyByWhatsApp) errors["notifications"] = ["Select at least one update channel."];
        if (string.IsNullOrWhiteSpace(request.Password)) errors["password"] = ["Password is required."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (await db.PublicMembers.AnyAsync(x => x.Cnic == cnic))
            return Results.Conflict(new { message = "This CNIC is already registered. Sign in instead." });
        if (await db.PublicMembers.AnyAsync(x => x.Phone == phone))
            return Results.Conflict(new { message = "This phone number is already registered." });
        if (await db.PublicMembers.AnyAsync(x => x.WhatsAppNumber == whatsappNumber))
            return Results.Conflict(new { message = "This WhatsApp number is already registered." });
        if (await db.PublicMembers.AnyAsync(x => x.Email.ToLower() == email) || await userManager.FindByEmailAsync(email) is not null)
            return Results.Conflict(new { message = "This email address is already registered. Sign in instead." });

        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = fullName, PhoneNumber = phone, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, request.Password!);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
            return Results.ValidationProblem(result.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()));
        }
        var membership = new TenantMembership { TenantId = tenant.Id, UserId = user.Id, Status = MembershipStatus.Pending, UnitLabel = Address(plotNo, streetNo, request.Block, project!.Name) };
        var member = new PublicMember
        {
            TenantId = tenant.Id, Membership = membership, Cnic = cnic, FullName = fullName,
            Phone = phone, WhatsAppNumber = whatsappNumber, Email = email, PlotNo = plotNo, StreetNo = streetNo,
            Block = request.Block?.Trim() ?? string.Empty, ProjectId = project.Id,
            NotifyByEmail = request.NotifyByEmail, NotifyByWhatsApp = request.NotifyByWhatsApp
        };
        db.PublicMembers.Add(member);
        var residentRoleId = await db.TenantRoles.Where(x => x.TenantId == tenant.Id && x.Name == "Resident").Select(x => x.Id).FirstOrDefaultAsync();
        if (residentRoleId != Guid.Empty) db.MembershipRoles.Add(new MembershipRole { Membership = membership, RoleId = residentRoleId });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Accepted(value: new
        {
            pendingApproval = true,
            message = "Registration submitted successfully. Your account will be activated after society administrator approval, normally within 1 hour."
        });
    }

    private static async Task<IResult> SignInAsync(string slug, PublicSignInRequest request, HttpContext http,
        AppDbContext db, UserManager<ApplicationUser> userManager, IDataProtectionProvider protection)
        => await SignInCoreAsync(request.Identifier ?? request.Cnic, request.Password, slug, http, db, userManager, protection);

    private static async Task<IResult> SignInDirectAsync(PublicSignInRequest request, HttpContext http,
        AppDbContext db, UserManager<ApplicationUser> userManager, IDataProtectionProvider protection)
        => await SignInCoreAsync(request.Identifier ?? request.Cnic, request.Password, null, http, db, userManager, protection);

    private static async Task<IResult> SignInCoreAsync(string? rawIdentifier, string password, string? slug, HttpContext http,
        AppDbContext db, UserManager<ApplicationUser> userManager, IDataProtectionProvider protection)
    {
        var identifier = rawIdentifier?.Trim() ?? string.Empty;
        var cnic = Digits(identifier);
        var email = identifier.ToLowerInvariant();
        var members = await db.PublicMembers.Include(x => x.Tenant).Include(x => x.Project).Include(x => x.Membership).ThenInclude(x => x.User)
            .Where(x => x.IsActive && (x.Cnic == cnic || x.Email.ToLower() == email) && (slug == null || x.Tenant.Slug == slug.ToLower()))
            .Take(2).ToListAsync();
        if (members.Count != 1) return Results.Unauthorized();
        var member = members[0];
        if (member.Membership.Status == MembershipStatus.Pending)
            return Results.Json(new { message = "Your registration is awaiting administrator approval. Accounts are normally activated within 1 hour." }, statusCode: StatusCodes.Status403Forbidden);
        if (member.Membership.Status == MembershipStatus.Suspended)
            return Results.Json(new { message = "Your account is temporarily suspended. Contact your society administrator." }, statusCode: StatusCodes.Status403Forbidden);
        if (member.Membership.Status != MembershipStatus.Active || !TenantAccess.IsAvailable(member.Tenant) ||
            !await userManager.CheckPasswordAsync(member.Membership.User, password)) return Results.Unauthorized();
        SetCookie(http, protection, member);
        return Results.Ok(MemberPayload(member, member.Tenant));
    }

    private static IResult SignOut(HttpContext http)
    {
        http.Response.Cookies.Delete(CookieName);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(HttpContext http, AppDbContext db, IDataProtectionProvider protection)
    {
        var member = await CurrentMemberAsync(http, db, protection);
        return member is null ? Results.Unauthorized() : Results.Ok(MemberPayload(member, member.Tenant));
    }

    private static async Task<IResult> ListAsync(HttpContext http, AppDbContext db, IDataProtectionProvider protection)
    {
        var member = await CurrentMemberAsync(http, db, protection);
        if (member is null) return Results.Unauthorized();
        var rows = await db.Complaints.AsNoTracking().Where(x => x.TenantId == member.TenantId && x.CreatedByMembershipId == member.MembershipId)
            .Include(x => x.Category).Include(x => x.Attachments).ToListAsync();
        var items = rows.OrderByDescending(x => x.CreatedAt).Select(x => new
        {
            x.Number, x.Title, x.Status, x.Priority, x.Location, x.CreatedAt, x.UpdatedAt, x.DueAt,
            category = x.Category.Name, attachmentCount = x.Attachments.Count
        });
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateComplaintAsync(PublicComplaintRequest request, HttpContext http, AppDbContext db, IDataProtectionProvider protection, NotificationDispatcher notifications, CancellationToken cancellationToken)
    {
        var member = await CurrentMemberAsync(http, db, protection);
        if (member is null) return Results.Unauthorized();
        var now = DateTimeOffset.UtcNow;
        if (await TenantAccess.MonthlyComplaintCountAsync(db, member.TenantId, now, cancellationToken) >= member.Tenant.MonthlyComplaintLimit)
            return Results.Problem("This society has reached its monthly complaint allowance.", statusCode: StatusCodes.Status403Forbidden);
        var category = await db.ComplaintCategories.SingleOrDefaultAsync(x => x.Id == request.CategoryId && x.TenantId == member.TenantId && x.IsActive);
        if (category is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["categoryId"] = ["Select a valid category."] });
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["complaint"] = ["Title and description are required."] });

        var number = await UniqueNumberAsync(db, member.TenantId);
        var location = string.IsNullOrWhiteSpace(request.Location) ? Address(member) : request.Location.Trim();
        var rule = await db.AssignmentRules.Where(x => x.TenantId == member.TenantId && x.IsActive &&
                (x.CategoryId == null || x.CategoryId == category.Id) && (x.LocationContains == "" || location.Contains(x.LocationContains)))
            .OrderBy(x => x.Order).FirstOrDefaultAsync();
        var complaint = new Complaint
        {
            TenantId = member.TenantId, Number = number, Title = request.Title.Trim(), Description = request.Description.Trim(),
            Location = location, CategoryId = category.Id, Priority = request.Priority ?? category.DefaultPriority,
            CreatedByMembershipId = member.MembershipId, AssignedToMembershipId = rule?.SupervisorMembershipId,
            Status = rule is null ? ComplaintStatus.Submitted : ComplaintStatus.Assigned, DueAt = now.AddHours(category.DefaultSlaHours)
        };
        db.Complaints.Add(complaint);
        db.ComplaintStatusEvents.Add(new ComplaintStatusEvent
        {
            TenantId = member.TenantId, Complaint = complaint, ToStatus = complaint.Status,
            ChangedByMembershipId = member.MembershipId, Note = rule is null ? "Complaint submitted through the public desk" : $"Automatically assigned by rule: {rule.Name}"
        });
        if (rule is not null)
            await notifications.NotifyAsync(member.TenantId, rule.SupervisorMembershipId, "New public complaint assigned", $"{number}: {complaint.Title}", "assignment", $"/app/complaints/{complaint.Id}", true, true, cancellationToken);
        await db.SaveChangesAsync();
        return Results.Created($"/api/public/complaints/{number}", new { complaint.Number, complaint.Id, message = "Complaint registered successfully. Save this number to track progress." });
    }

    private static async Task<IResult> GetComplaintAsync(string number, HttpContext http, AppDbContext db, IDataProtectionProvider protection)
    {
        var member = await CurrentMemberAsync(http, db, protection);
        if (member is null) return Results.Unauthorized();
        var row = await db.Complaints.AsNoTracking().AsSplitQuery()
            .Where(x => x.TenantId == member.TenantId && x.Number == number && x.CreatedByMembershipId == member.MembershipId)
            .Include(x => x.Category).Include(x => x.Comments).ThenInclude(x => x.AuthorMembership).ThenInclude(x => x.User)
            .Include(x => x.Comments).ThenInclude(x => x.Attachments).Include(x => x.Attachments)
            .Include(x => x.StatusHistory).SingleOrDefaultAsync();
        if (row is null) return Results.NotFound(new { message = "Complaint not found for this member." });
        return Results.Ok(new
        {
            row.Number, row.Title, row.Description, row.Location, row.Priority, row.Status, row.DueAt, row.CreatedAt, row.UpdatedAt,
            category = row.Category.Name,
            attachments = row.Attachments.Where(x => x.CommentId == null).Select(AttachmentPayload),
            comments = row.Comments.Where(x => !x.IsInternal).OrderBy(x => x.CreatedAt).Select(x => new
            {
                x.Id, x.Body, x.CreatedAt, author = x.AuthorMembership.User.DisplayName,
                attachments = x.Attachments.Select(AttachmentPayload)
            }),
            history = row.StatusHistory.OrderBy(x => x.CreatedAt).Select(x => new { x.ToStatus, x.Note, x.CreatedAt })
        });
    }

    private static async Task<IResult> AddCommentAsync(string number, PublicCommentRequest request, HttpContext http, AppDbContext db, IDataProtectionProvider protection)
    {
        var member = await CurrentMemberAsync(http, db, protection);
        if (member is null) return Results.Unauthorized();
        var complaint = await db.Complaints.SingleOrDefaultAsync(x => x.TenantId == member.TenantId && x.Number == number && x.CreatedByMembershipId == member.MembershipId);
        if (complaint is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.Body)) return Results.BadRequest(new { message = "Reply cannot be empty." });
        var comment = new ComplaintComment { TenantId = member.TenantId, ComplaintId = complaint.Id, AuthorMembershipId = member.MembershipId, Body = request.Body.Trim() };
        db.ComplaintComments.Add(comment);
        complaint.UpdatedAt = DateTimeOffset.UtcNow;
        if (complaint.AssignedToMembershipId.HasValue) ComplaintEndpoints.AddNotification(db, member.TenantId, complaint.AssignedToMembershipId.Value, "Member replied", $"{number}: {complaint.Title}", "info", $"/app/complaints/{complaint.Id}");
        await db.SaveChangesAsync();
        return Results.Created($"/api/public/complaints/{number}/comments/{comment.Id}", new { comment.Id });
    }

    private static async Task<IResult> UploadComplaintAttachmentAsync(string number, IFormFile file, HttpContext http, AppDbContext db, IDataProtectionProvider protection, IFileStorage storage, CancellationToken cancellationToken)
    {
        var member = await CurrentMemberAsync(http, db, protection);
        if (member is null) return Results.Unauthorized();
        var complaint = await db.Complaints.SingleOrDefaultAsync(x => x.TenantId == member.TenantId && x.Number == number && x.CreatedByMembershipId == member.MembershipId);
        return complaint is null ? Results.NotFound() : await SaveAttachmentAsync(file, complaint, null, member.MembershipId, db, storage, cancellationToken);
    }

    private static async Task<IResult> UploadCommentAttachmentAsync(string number, Guid commentId, IFormFile file, HttpContext http, AppDbContext db, IDataProtectionProvider protection, IFileStorage storage, CancellationToken cancellationToken)
    {
        var member = await CurrentMemberAsync(http, db, protection);
        if (member is null) return Results.Unauthorized();
        var complaint = await db.Complaints.SingleOrDefaultAsync(x => x.TenantId == member.TenantId && x.Number == number && x.CreatedByMembershipId == member.MembershipId);
        var comment = complaint is null ? null : await db.ComplaintComments.SingleOrDefaultAsync(x => x.Id == commentId && x.ComplaintId == complaint.Id && x.AuthorMembershipId == member.MembershipId);
        return complaint is null || comment is null ? Results.NotFound() : await SaveAttachmentAsync(file, complaint, comment.Id, member.MembershipId, db, storage, cancellationToken);
    }

    internal static async Task<IResult> SaveAttachmentAsync(IFormFile file, Complaint complaint, Guid? commentId, Guid uploaderId, AppDbContext db, IFileStorage storage, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > 8 * 1024 * 1024) return Results.BadRequest(new { message = "Images must be between 1 byte and 8 MB." });
        if (!AllowedImageTypes.Contains(file.ContentType.ToLowerInvariant())) return Results.BadRequest(new { message = "Only JPG, PNG, and WebP images are supported." });
        var extension = file.ContentType.ToLowerInvariant() switch { "image/png" => ".png", "image/webp" => ".webp", _ => ".jpg" };
        var storedName = $"{Guid.NewGuid():N}{extension}";
        await using var stream = file.OpenReadStream();
        if (!await HasValidImageSignatureAsync(stream, file.ContentType, cancellationToken))
            return Results.BadRequest(new { message = "The uploaded file content is not a valid JPG, PNG, or WebP image." });
        stream.Position = 0;
        await storage.SaveAsync(StorageKeys.Complaint(complaint.TenantId, complaint.Id, storedName), stream, file.ContentType, cancellationToken);
        var attachment = new ComplaintAttachment
        {
            TenantId = complaint.TenantId, ComplaintId = complaint.Id, CommentId = commentId, UploaderMembershipId = uploaderId,
            FileName = Path.GetFileName(file.FileName), StoredName = storedName, ContentType = file.ContentType, Size = file.Length
        };
        db.ComplaintAttachments.Add(attachment);
        complaint.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/files/{attachment.Id}", AttachmentPayload(attachment));
    }

    private static async Task<IResult> DownloadAttachmentAsync(Guid id, HttpContext http, ClaimsPrincipal principal, AppDbContext db, IDataProtectionProvider protection, IFileStorage storage, CancellationToken cancellationToken)
    {
        var attachment = await db.ComplaintAttachments.Include(x => x.Complaint).SingleOrDefaultAsync(x => x.Id == id);
        if (attachment is null) return Results.NotFound();
        var internalTenant = principal.FindFirstValue("tenant_id");
        var internalAllowed = Guid.TryParse(internalTenant, out var tenantId) && tenantId == attachment.TenantId;
        var publicMember = internalAllowed ? null : await CurrentMemberAsync(http, db, protection);
        if (!internalAllowed && (publicMember is null || publicMember.TenantId != attachment.TenantId || publicMember.MembershipId != attachment.Complaint.CreatedByMembershipId)) return Results.Unauthorized();
        var stored = await storage.ReadAsync(StorageKeys.Complaint(attachment.TenantId, attachment.ComplaintId, attachment.StoredName), cancellationToken);
        return stored is null ? Results.NotFound() : Results.File(stored.Content, attachment.ContentType, attachment.FileName);
    }

    private static async Task<PublicMember?> CurrentMemberAsync(HttpContext http, AppDbContext db, IDataProtectionProvider protection)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName, out var value)) return null;
        try
        {
            var raw = protection.CreateProtector("SukoonDesk.PublicMember.v1").Unprotect(value);
            if (!Guid.TryParse(raw, out var id)) return null;
            var member = await db.PublicMembers.Include(x => x.Project).Include(x => x.Tenant).Include(x => x.Membership).ThenInclude(x => x.User)
                .SingleOrDefaultAsync(x => x.Id == id && x.IsActive && x.Membership.Status == MembershipStatus.Active);
            return member is not null && TenantAccess.IsAvailable(member.Tenant) ? member : null;
        }
        catch { return null; }
    }

    private static void SetCookie(HttpContext http, IDataProtectionProvider protection, PublicMember member)
    {
        var value = protection.CreateProtector("SukoonDesk.PublicMember.v1").Protect(member.Id.ToString());
        http.Response.Cookies.Append(CookieName, value, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = http.Request.IsHttps, Expires = DateTimeOffset.UtcNow.AddDays(30), IsEssential = true });
    }

    private static async Task<string> UniqueNumberAsync(AppDbContext db, Guid tenantId)
    {
        string number;
        do number = $"CMP-{DateTimeOffset.UtcNow.Year}-{Random.Shared.Next(100000, 999999)}";
        while (await db.Complaints.AnyAsync(x => x.TenantId == tenantId && x.Number == number));
        return number;
    }

    private static object MemberPayload(PublicMember member, Tenant tenant) => new
    {
        member.Id, member.FullName, member.Phone, member.WhatsAppNumber, member.Email, member.PlotNo, member.StreetNo, member.Block,
        member.ProjectId, project = member.Project == null ? null : new { member.Project.Id, member.Project.Name, member.Project.Code },
        member.NotifyByEmail, member.NotifyByWhatsApp,
        tenant = new { tenant.Id, tenant.Name, tenant.Slug }
    };
    private static object AttachmentPayload(ComplaintAttachment x) => new { x.Id, x.FileName, x.ContentType, x.Size, url = $"/api/files/{x.Id}" };
    private static async Task<bool> HasValidImageSignatureAsync(Stream stream, string contentType, CancellationToken cancellationToken)
    {
        var header = new byte[12];
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        if (contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)) return read >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff;
        if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)) return read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        return read >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8);
    }
    private static string Digits(string? value) => Regex.Replace(value ?? string.Empty, "[^0-9]", string.Empty);
    private static string Address(string plotNo, string streetNo, string? block, string project) => $"Plot {plotNo}, Street {streetNo}{(string.IsNullOrWhiteSpace(block) ? "" : $", Block {block.Trim()}")}, {project}";
    private static string Address(PublicMember x) => $"Plot {x.PlotNo}, Street {x.StreetNo}{(string.IsNullOrWhiteSpace(x.Block) ? "" : $", Block {x.Block}")}{(x.Project is null ? "" : $", {x.Project.Name}")}";
}

public sealed record PublicRegistrationRequest(string? SocietySlug, string? FullName, string? Cnic, string? Phone, string? WhatsAppNumber, string? Email, string? Password, string? PlotNo, string? StreetNo, string? Block, Guid? ProjectId, bool NotifyByEmail = true, bool NotifyByWhatsApp = false);
public sealed record PublicSignInRequest(string? Identifier, string? Cnic, string Password);
public sealed record PublicComplaintRequest(string Title, string Description, string? Location, Guid CategoryId, ComplaintPriority? Priority);
public sealed record PublicCommentRequest(string Body);
