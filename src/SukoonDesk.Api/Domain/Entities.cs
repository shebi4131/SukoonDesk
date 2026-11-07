using Microsoft.AspNetCore.Identity;

namespace SukoonDesk.Api.Domain;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public bool IsPlatformAdmin { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum MembershipStatus { Pending, Active, Suspended, Rejected, Deleted }
public enum SocietyPhase { JGI, JGII, KT, KTExt }

public sealed class TenantMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;
    public string UnitLabel { get; set; } = string.Empty;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PublicMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid MembershipId { get; set; }
    public TenantMembership Membership { get; set; } = null!;
    public required string Cnic { get; set; }
    public required string FullName { get; set; }
    public required string Phone { get; set; }
    public required string Email { get; set; }
    public required string PlotNo { get; set; }
    public required string StreetNo { get; set; }
    public string Block { get; set; } = string.Empty;
    public SocietyPhase Phase { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TenantRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<RolePermission> Permissions { get; set; } = [];
    public ICollection<MembershipRole> Members { get; set; } = [];
}

public sealed class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Group { get; set; }
    public string Description { get; set; } = string.Empty;
    public ICollection<RolePermission> Roles { get; set; } = [];
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public TenantRole Role { get; set; } = null!;
    public Guid PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}

public sealed class MembershipRole
{
    public Guid MembershipId { get; set; }
    public TenantMembership Membership { get; set; } = null!;
    public Guid RoleId { get; set; }
    public TenantRole Role { get; set; } = null!;
}

public sealed class AppSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum ComplaintPriority { Low, Normal, High, Urgent }
public enum ComplaintStatus { Submitted, Acknowledged, Assigned, InProgress, WaitingOnResident, Resolved, Closed, Reopened, Rejected, Cancelled }

public sealed class ComplaintCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public required string Name { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = "#155E75";
    public int DefaultSlaHours { get; set; } = 48;
    public ComplaintPriority DefaultPriority { get; set; } = ComplaintPriority.Normal;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Complaint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public required string Number { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public string Location { get; set; } = string.Empty;
    public ComplaintPriority Priority { get; set; } = ComplaintPriority.Normal;
    public ComplaintStatus Status { get; set; } = ComplaintStatus.Submitted;
    public Guid CategoryId { get; set; }
    public ComplaintCategory Category { get; set; } = null!;
    public Guid CreatedByMembershipId { get; set; }
    public TenantMembership CreatedByMembership { get; set; } = null!;
    public Guid? AssignedToMembershipId { get; set; }
    public TenantMembership? AssignedToMembership { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ComplaintComment> Comments { get; set; } = [];
    public ICollection<ComplaintAttachment> Attachments { get; set; } = [];
    public ICollection<ComplaintStatusEvent> StatusHistory { get; set; } = [];
    public ComplaintFeedback? Feedback { get; set; }
}

public sealed class ComplaintComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ComplaintId { get; set; }
    public Complaint Complaint { get; set; } = null!;
    public Guid AuthorMembershipId { get; set; }
    public TenantMembership AuthorMembership { get; set; } = null!;
    public required string Body { get; set; }
    public bool IsInternal { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ComplaintAttachment> Attachments { get; set; } = [];
}

public sealed class ComplaintAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ComplaintId { get; set; }
    public Complaint Complaint { get; set; } = null!;
    public Guid? CommentId { get; set; }
    public ComplaintComment? Comment { get; set; }
    public Guid UploaderMembershipId { get; set; }
    public TenantMembership UploaderMembership { get; set; } = null!;
    public required string FileName { get; set; }
    public required string StoredName { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ComplaintStatusEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ComplaintId { get; set; }
    public Complaint Complaint { get; set; } = null!;
    public ComplaintStatus? FromStatus { get; set; }
    public ComplaintStatus ToStatus { get; set; }
    public Guid ChangedByMembershipId { get; set; }
    public TenantMembership ChangedByMembership { get; set; } = null!;
    public string Note { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ComplaintFeedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ComplaintId { get; set; }
    public Complaint Complaint { get; set; } = null!;
    public Guid MembershipId { get; set; }
    public TenantMembership Membership { get; set; } = null!;
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AssignmentRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public required string Name { get; set; }
    public Guid? CategoryId { get; set; }
    public ComplaintCategory? Category { get; set; }
    public string LocationContains { get; set; } = string.Empty;
    public Guid SupervisorMembershipId { get; set; }
    public TenantMembership SupervisorMembership { get; set; } = null!;
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MembershipId { get; set; }
    public TenantMembership Membership { get; set; } = null!;
    public required string Title { get; set; }
    public required string Message { get; set; }
    public string Type { get; set; } = "info";
    public string Link { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
