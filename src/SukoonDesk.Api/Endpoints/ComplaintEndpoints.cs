using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SukoonDesk.Api.Domain;
using SukoonDesk.Api.Infrastructure;
using SukoonDesk.Api.Security;

namespace SukoonDesk.Api.Endpoints;

public static class ComplaintEndpoints
{
    private static readonly Dictionary<ComplaintStatus, ComplaintStatus[]> AllowedTransitions = new()
    {
        [ComplaintStatus.Submitted] = [ComplaintStatus.Acknowledged, ComplaintStatus.Assigned, ComplaintStatus.InProgress, ComplaintStatus.Rejected, ComplaintStatus.Cancelled],
        [ComplaintStatus.Acknowledged] = [ComplaintStatus.Assigned, ComplaintStatus.InProgress, ComplaintStatus.Rejected],
        [ComplaintStatus.Assigned] = [ComplaintStatus.InProgress, ComplaintStatus.WaitingOnResident, ComplaintStatus.Resolved],
        [ComplaintStatus.InProgress] = [ComplaintStatus.WaitingOnResident, ComplaintStatus.Resolved],
        [ComplaintStatus.WaitingOnResident] = [ComplaintStatus.InProgress, ComplaintStatus.Resolved],
        [ComplaintStatus.Resolved] = [ComplaintStatus.Closed, ComplaintStatus.Reopened],
        [ComplaintStatus.Reopened] = [ComplaintStatus.Assigned, ComplaintStatus.InProgress, ComplaintStatus.Resolved],
        [ComplaintStatus.Closed] = [ComplaintStatus.Reopened]
    };

    public static IEndpointRouteBuilder MapComplaintEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/complaints").RequireAuthorization().WithTags("Complaints");
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.ComplaintsCreate));
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.ComplaintsEdit));
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.ComplaintsDelete));
        group.MapPost("/{id:guid}/comments", AddCommentAsync);
        group.MapPut("/{id:guid}/comments/{commentId:guid}", UpdateCommentAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.CommentsEdit));
        group.MapDelete("/{id:guid}/comments/{commentId:guid}", DeleteCommentAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.CommentsDelete));
        group.MapPost("/{id:guid}/attachments", UploadAttachmentAsync).DisableAntiforgery();
        group.MapPost("/{id:guid}/comments/{commentId:guid}/attachments", UploadCommentAttachmentAsync).DisableAntiforgery();
        group.MapPost("/{id:guid}/status", ChangeStatusAsync);
        group.MapPost("/{id:guid}/assign", AssignAsync).RequireAuthorization(PermissionPolicyProvider.For(Permissions.ComplaintsAssign));
        group.MapPost("/{id:guid}/feedback", SubmitFeedbackAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal, AppDbContext db, string? status, Guid? categoryId, string? search,
        int page = 1, int pageSize = 20)
    {
        var (tenantId, membershipId) = Context(principal);
        var canViewAll = await HasPermissionAsync(db, membershipId, Permissions.ComplaintsViewAll);
        var query = db.Complaints.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (!canViewAll) query = query.Where(x => x.CreatedByMembershipId == membershipId);
        if (Enum.TryParse<ComplaintStatus>(status, true, out var parsedStatus)) query = query.Where(x => x.Status == parsedStatus);
        if (categoryId.HasValue) query = query.Where(x => x.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Number.Contains(term) || x.Title.Contains(term) || x.Location.Contains(term));
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync();
        var rows = await query.Include(x => x.Category).Include(x => x.CreatedByMembership).ThenInclude(x => x.User)
            .Include(x => x.AssignedToMembership).ThenInclude(x => x!.User).ToListAsync();
        var items = rows.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id, x.Number, x.Title, x.Location, x.Priority, x.Status, x.DueAt, x.CreatedAt, x.UpdatedAt,
                category = new { x.Category.Id, x.Category.Name, x.Category.Color },
                resident = x.CreatedByMembership.User.DisplayName,
                assignedTo = x.AssignedToMembership == null ? null : x.AssignedToMembership.User.DisplayName,
                isOverdue = x.DueAt < DateTimeOffset.UtcNow && x.Status != ComplaintStatus.Resolved && x.Status != ComplaintStatus.Closed
            }).ToList();
        return Results.Ok(new { items, total, page, pageSize });
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, membershipId) = Context(principal);
        var row = await db.Complaints.AsNoTracking().AsSplitQuery().Where(x => x.Id == id && x.TenantId == tenantId)
            .Include(x => x.Category).Include(x => x.CreatedByMembership).ThenInclude(x => x.User)
            .Include(x => x.AssignedToMembership).ThenInclude(x => x!.User)
            .Include(x => x.Comments).ThenInclude(x => x.AuthorMembership).ThenInclude(x => x.User)
            .Include(x => x.Comments).ThenInclude(x => x.Attachments)
            .Include(x => x.Attachments)
            .Include(x => x.StatusHistory).ThenInclude(x => x.ChangedByMembership).ThenInclude(x => x.User)
            .Include(x => x.Feedback).SingleOrDefaultAsync();
        if (row is null) return Results.NotFound();
        var canViewAll = await HasPermissionAsync(db, membershipId, Permissions.ComplaintsViewAll);
        if (!canViewAll && row.CreatedByMembershipId != membershipId) return Results.Forbid();
        var complaint = new
        {
            row.Id, row.Number, row.Title, row.Description, row.Location, row.Priority, row.Status, row.DueAt,
            row.ResolvedAt, row.ClosedAt, row.CreatedAt, row.UpdatedAt, row.CreatedByMembershipId, row.AssignedToMembershipId,
            category = new { row.Category.Id, row.Category.Name, row.Category.Color, row.Category.DefaultSlaHours },
            resident = new { row.CreatedByMembership.Id, row.CreatedByMembership.User.DisplayName, row.CreatedByMembership.User.Email, row.CreatedByMembership.UnitLabel },
            assignedTo = row.AssignedToMembership == null ? null : new { row.AssignedToMembership.Id, row.AssignedToMembership.User.DisplayName, row.AssignedToMembership.User.Email },
            attachments = row.Attachments.Where(x => x.CommentId == null).Select(AttachmentPayload),
            comments = row.Comments.OrderBy(c => c.CreatedAt).Select(c => new { c.Id, c.Body, c.IsInternal, c.CreatedAt, author = c.AuthorMembership.User.DisplayName, c.AuthorMembershipId, attachments = c.Attachments.Select(AttachmentPayload) }),
            history = row.StatusHistory.OrderBy(h => h.CreatedAt).Select(h => new { h.Id, h.FromStatus, h.ToStatus, h.Note, h.CreatedAt, actor = h.ChangedByMembership.User.DisplayName }),
            feedback = row.Feedback == null ? null : new { row.Feedback.Rating, row.Feedback.Comment, row.Feedback.CreatedAt }
        };
        return Results.Ok(complaint);
    }

    private static async Task<IResult> CreateAsync(CreateComplaintRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["complaint"] = ["Title and description are required."] });
        var (tenantId, membershipId) = Context(principal);
        var category = await db.ComplaintCategories.SingleOrDefaultAsync(x => x.Id == request.CategoryId && x.TenantId == tenantId && x.IsActive);
        if (category is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["categoryId"] = ["Select a valid category."] });

        var now = DateTimeOffset.UtcNow;
        var createdDates = await db.Complaints.Where(x => x.TenantId == tenantId).Select(x => x.CreatedAt).ToListAsync();
        var sequence = createdDates.Count(x => x.Year == now.Year) + 1;
        var rule = await db.AssignmentRules.Include(x => x.SupervisorMembership)
            .Where(x => x.TenantId == tenantId && x.IsActive &&
                (x.CategoryId == null || x.CategoryId == category.Id) &&
                (x.LocationContains == "" || request.Location.Contains(x.LocationContains)))
            .OrderBy(x => x.Order).FirstOrDefaultAsync();
        var assignedId = rule?.SupervisorMembershipId;
        var complaint = new Complaint
        {
            TenantId = tenantId,
            Number = $"CMP-{now.Year}-{sequence:0000}",
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Location = request.Location.Trim(),
            CategoryId = category.Id,
            Priority = request.Priority ?? category.DefaultPriority,
            Status = assignedId.HasValue ? ComplaintStatus.Assigned : ComplaintStatus.Submitted,
            CreatedByMembershipId = membershipId,
            AssignedToMembershipId = assignedId,
            DueAt = now.AddHours(category.DefaultSlaHours)
        };
        db.Complaints.Add(complaint);
        db.ComplaintStatusEvents.Add(new ComplaintStatusEvent
        {
            TenantId = tenantId, Complaint = complaint, ToStatus = complaint.Status,
            ChangedByMembershipId = membershipId, Note = assignedId.HasValue ? $"Automatically assigned by rule: {rule!.Name}" : "Complaint submitted"
        });
        AddNotification(db, tenantId, membershipId, "Complaint received", $"{complaint.Number} has been submitted.", "success", $"/app/complaints/{complaint.Id}");
        if (assignedId.HasValue)
            AddNotification(db, tenantId, assignedId.Value, "New complaint assigned", $"{complaint.Number}: {complaint.Title}", "assignment", $"/app/complaints/{complaint.Id}");
        await db.SaveChangesAsync();
        return Results.Created($"/api/complaints/{complaint.Id}", new { complaint.Id, complaint.Number });
    }

    private static async Task<IResult> AddCommentAsync(Guid id, CommentRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Body)) return Results.BadRequest(new { message = "Comment cannot be empty." });
        var (tenantId, membershipId) = Context(principal);
        var complaint = await db.Complaints.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (complaint is null) return Results.NotFound();
        var staff = await HasPermissionAsync(db, membershipId, Permissions.ComplaintsViewAll);
        if (!staff && complaint.CreatedByMembershipId != membershipId) return Results.Forbid();
        var comment = new ComplaintComment { TenantId = tenantId, ComplaintId = id, AuthorMembershipId = membershipId, Body = request.Body.Trim(), IsInternal = staff && request.IsInternal };
        db.ComplaintComments.Add(comment);
        var recipient = membershipId == complaint.CreatedByMembershipId ? complaint.AssignedToMembershipId : complaint.CreatedByMembershipId;
        if (recipient.HasValue && !(staff && request.IsInternal))
            AddNotification(db, tenantId, recipient.Value, "New complaint reply", $"A reply was added to {complaint.Number}.", "comment", $"/app/complaints/{id}");
        complaint.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.Created($"/api/complaints/{id}/comments/{comment.Id}", new { comment.Id });
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateComplaintRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = Context(principal);
        var complaint = await db.Complaints.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        var category = await db.ComplaintCategories.SingleOrDefaultAsync(x => x.Id == request.CategoryId && x.TenantId == tenantId && x.IsActive);
        if (complaint is null || category is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description)) return Results.BadRequest(new { message = "Title and description are required." });
        complaint.Title = request.Title.Trim();
        complaint.Description = request.Description.Trim();
        complaint.Location = request.Location.Trim();
        complaint.CategoryId = category.Id;
        complaint.Priority = request.Priority;
        complaint.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal principal, AppDbContext db, IFileStorage storage, CancellationToken cancellationToken)
    {
        var (tenantId, _) = Context(principal);
        var complaint = await db.Complaints.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (complaint is null) return Results.NotFound();
        db.Complaints.Remove(complaint);
        await db.SaveChangesAsync();
        await storage.DeletePrefixAsync(StorageKeys.ComplaintPrefix(tenantId, id), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateCommentAsync(Guid id, Guid commentId, CommentRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, _) = Context(principal);
        var comment = await db.ComplaintComments.SingleOrDefaultAsync(x => x.Id == commentId && x.ComplaintId == id && x.TenantId == tenantId);
        if (comment is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.Body)) return Results.BadRequest(new { message = "Comment cannot be empty." });
        comment.Body = request.Body.Trim();
        comment.IsInternal = request.IsInternal;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteCommentAsync(Guid id, Guid commentId, ClaimsPrincipal principal, AppDbContext db, IFileStorage storage, CancellationToken cancellationToken)
    {
        var (tenantId, _) = Context(principal);
        var comment = await db.ComplaintComments.Include(x => x.Attachments).SingleOrDefaultAsync(x => x.Id == commentId && x.ComplaintId == id && x.TenantId == tenantId);
        if (comment is null) return Results.NotFound();
        foreach (var attachment in comment.Attachments)
        {
            await storage.DeleteAsync(StorageKeys.Complaint(tenantId, id, attachment.StoredName), cancellationToken);
        }
        db.ComplaintComments.Remove(comment);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> UploadAttachmentAsync(Guid id, IFormFile file, ClaimsPrincipal principal, AppDbContext db, IFileStorage storage, CancellationToken cancellationToken)
    {
        var (tenantId, membershipId) = Context(principal);
        var complaint = await db.Complaints.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (complaint is null) return Results.NotFound();
        var canViewAll = await HasPermissionAsync(db, membershipId, Permissions.ComplaintsViewAll);
        if (!canViewAll && complaint.CreatedByMembershipId != membershipId) return Results.Forbid();
        return await PublicEndpoints.SaveAttachmentAsync(file, complaint, null, membershipId, db, storage, cancellationToken);
    }

    private static async Task<IResult> UploadCommentAttachmentAsync(Guid id, Guid commentId, IFormFile file, ClaimsPrincipal principal, AppDbContext db, IFileStorage storage, CancellationToken cancellationToken)
    {
        var (tenantId, membershipId) = Context(principal);
        var complaint = await db.Complaints.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        var comment = complaint is null ? null : await db.ComplaintComments.SingleOrDefaultAsync(x => x.Id == commentId && x.ComplaintId == id);
        if (complaint is null || comment is null) return Results.NotFound();
        var canViewAll = await HasPermissionAsync(db, membershipId, Permissions.ComplaintsViewAll);
        if (!canViewAll && complaint.CreatedByMembershipId != membershipId) return Results.Forbid();
        return await PublicEndpoints.SaveAttachmentAsync(file, complaint, comment.Id, membershipId, db, storage, cancellationToken);
    }

    private static async Task<IResult> ChangeStatusAsync(Guid id, StatusRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, membershipId) = Context(principal);
        var complaint = await db.Complaints.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (complaint is null) return Results.NotFound();
        var staff = await HasPermissionAsync(db, membershipId, Permissions.ComplaintsUpdateStatus);
        var residentAction = complaint.CreatedByMembershipId == membershipId &&
            ((complaint.Status == ComplaintStatus.Resolved && request.Status is ComplaintStatus.Closed or ComplaintStatus.Reopened) ||
             (complaint.Status == ComplaintStatus.Closed && request.Status == ComplaintStatus.Reopened));
        if (!staff && !residentAction) return Results.Forbid();
        if (!AllowedTransitions.TryGetValue(complaint.Status, out var allowed) || !allowed.Contains(request.Status))
            return Results.BadRequest(new { message = $"Cannot move from {complaint.Status} to {request.Status}." });

        var previous = complaint.Status;
        complaint.Status = request.Status;
        complaint.UpdatedAt = DateTimeOffset.UtcNow;
        if (request.Status == ComplaintStatus.Resolved) complaint.ResolvedAt = complaint.UpdatedAt;
        if (request.Status == ComplaintStatus.Closed) complaint.ClosedAt = complaint.UpdatedAt;
        if (request.Status == ComplaintStatus.Reopened) { complaint.ResolvedAt = null; complaint.ClosedAt = null; }
        db.ComplaintStatusEvents.Add(new ComplaintStatusEvent { TenantId = tenantId, ComplaintId = id, FromStatus = previous, ToStatus = request.Status, ChangedByMembershipId = membershipId, Note = request.Note?.Trim() ?? string.Empty });
        if (complaint.CreatedByMembershipId != membershipId)
            AddNotification(db, tenantId, complaint.CreatedByMembershipId, "Complaint status updated", $"{complaint.Number} is now {Humanize(request.Status)}.", "status", $"/app/complaints/{id}");
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> AssignAsync(Guid id, AssignmentRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, membershipId) = Context(principal);
        var complaint = await db.Complaints.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        var assignee = await db.TenantMemberships.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == request.MembershipId && x.TenantId == tenantId && x.Status == MembershipStatus.Active);
        if (complaint is null || assignee is null) return Results.NotFound();
        var previous = complaint.Status;
        complaint.AssignedToMembershipId = assignee.Id;
        complaint.Status = complaint.Status is ComplaintStatus.Submitted or ComplaintStatus.Acknowledged ? ComplaintStatus.Assigned : complaint.Status;
        complaint.UpdatedAt = DateTimeOffset.UtcNow;
        db.ComplaintStatusEvents.Add(new ComplaintStatusEvent { TenantId = tenantId, ComplaintId = id, FromStatus = previous, ToStatus = complaint.Status, ChangedByMembershipId = membershipId, Note = $"Assigned to {assignee.User.DisplayName}. {request.Note}".Trim() });
        AddNotification(db, tenantId, assignee.Id, "Complaint assigned to you", $"{complaint.Number}: {complaint.Title}", "assignment", $"/app/complaints/{id}");
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> SubmitFeedbackAsync(Guid id, FeedbackRequest request, ClaimsPrincipal principal, AppDbContext db)
    {
        var (tenantId, membershipId) = Context(principal);
        var complaint = await db.Complaints.Include(x => x.Feedback).SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId);
        if (complaint is null) return Results.NotFound();
        if (complaint.CreatedByMembershipId != membershipId) return Results.Forbid();
        if (complaint.Status is not ComplaintStatus.Resolved and not ComplaintStatus.Closed) return Results.BadRequest(new { message = "Feedback is available after resolution." });
        if (complaint.Feedback is not null) return Results.Conflict(new { message = "Feedback has already been submitted." });
        if (request.Rating is < 1 or > 5) return Results.BadRequest(new { message = "Rating must be between 1 and 5." });
        db.ComplaintFeedback.Add(new ComplaintFeedback { TenantId = tenantId, ComplaintId = id, MembershipId = membershipId, Rating = request.Rating, Comment = request.Comment?.Trim() ?? string.Empty });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    internal static async Task<bool> HasPermissionAsync(AppDbContext db, Guid membershipId, string permission) =>
        await db.MembershipRoles.AnyAsync(x => x.MembershipId == membershipId && x.Role.IsActive && x.Role.Permissions.Any(p => p.Permission.Key == permission));

    internal static (Guid TenantId, Guid MembershipId) Context(ClaimsPrincipal principal)
    {
        Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenantId);
        Guid.TryParse(principal.FindFirstValue("membership_id"), out var membershipId);
        return (tenantId, membershipId);
    }

    internal static void AddNotification(AppDbContext db, Guid tenantId, Guid membershipId, string title, string message, string type, string link) =>
        db.Notifications.Add(new Notification { TenantId = tenantId, MembershipId = membershipId, Title = title, Message = message, Type = type, Link = link });

    private static string Humanize(ComplaintStatus status) => System.Text.RegularExpressions.Regex.Replace(status.ToString(), "([a-z])([A-Z])", "$1 $2");
    private static object AttachmentPayload(ComplaintAttachment x) => new { x.Id, x.FileName, x.ContentType, x.Size, url = $"/api/files/{x.Id}" };
}

public sealed record CreateComplaintRequest(string Title, string Description, string Location, Guid CategoryId, ComplaintPriority? Priority);
public sealed record UpdateComplaintRequest(string Title, string Description, string Location, Guid CategoryId, ComplaintPriority Priority);
public sealed record CommentRequest(string Body, bool IsInternal = false);
public sealed record StatusRequest(ComplaintStatus Status, string? Note);
public sealed record AssignmentRequest(Guid MembershipId, string? Note);
public sealed record FeedbackRequest(int Rating, string? Comment);
