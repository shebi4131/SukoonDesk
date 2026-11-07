using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using SukoonDesk.Api.Domain;

namespace SukoonDesk.Api.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<PublicMember> PublicMembers => Set<PublicMember>();
    public DbSet<TenantRole> TenantRoles => Set<TenantRole>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<ComplaintCategory> ComplaintCategories => Set<ComplaintCategory>();
    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<ComplaintComment> ComplaintComments => Set<ComplaintComment>();
    public DbSet<ComplaintAttachment> ComplaintAttachments => Set<ComplaintAttachment>();
    public DbSet<ComplaintStatusEvent> ComplaintStatusEvents => Set<ComplaintStatusEvent>();
    public DbSet<ComplaintFeedback> ComplaintFeedback => Set<ComplaintFeedback>();
    public DbSet<AssignmentRule> AssignmentRules => Set<AssignmentRule>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Tenant>().HasIndex(x => x.Slug).IsUnique();
        builder.Entity<Tenant>().Property(x => x.Name).HasMaxLength(160);
        builder.Entity<Tenant>().Property(x => x.Slug).HasMaxLength(80);
        builder.Entity<TenantMembership>().HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        builder.Entity<PublicMember>().HasIndex(x => new { x.TenantId, x.Cnic }).IsUnique();
        builder.Entity<PublicMember>().HasIndex(x => x.MembershipId).IsUnique();
        builder.Entity<PublicMember>().HasOne(x => x.Membership).WithOne().HasForeignKey<PublicMember>(x => x.MembershipId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<TenantRole>().HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        builder.Entity<TenantRole>().Property(x => x.Name).HasMaxLength(100);
        builder.Entity<Permission>().HasIndex(x => x.Key).IsUnique();
        builder.Entity<Permission>().Property(x => x.Key).HasMaxLength(120);
        builder.Entity<RolePermission>().HasKey(x => new { x.RoleId, x.PermissionId });
        builder.Entity<MembershipRole>().HasKey(x => new { x.MembershipId, x.RoleId });
        builder.Entity<AppSetting>().HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
        builder.Entity<AppSetting>().Property(x => x.Key).HasMaxLength(120);

        builder.Entity<ComplaintCategory>().HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        builder.Entity<Complaint>().HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
        builder.Entity<Complaint>().HasIndex(x => new { x.TenantId, x.Status, x.DueAt });
        builder.Entity<Complaint>()
            .HasOne(x => x.AssignedToMembership)
            .WithMany()
            .HasForeignKey(x => x.AssignedToMembershipId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Complaint>()
            .HasOne(x => x.CreatedByMembership)
            .WithMany()
            .HasForeignKey(x => x.CreatedByMembershipId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ComplaintComment>().HasIndex(x => new { x.TenantId, x.ComplaintId, x.CreatedAt });
        builder.Entity<ComplaintComment>().HasOne(x => x.AuthorMembership).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ComplaintAttachment>().HasIndex(x => new { x.TenantId, x.ComplaintId });
        builder.Entity<ComplaintAttachment>().HasOne(x => x.Comment).WithMany(x => x.Attachments).HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ComplaintAttachment>().HasOne(x => x.UploaderMembership).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ComplaintStatusEvent>().HasIndex(x => new { x.TenantId, x.ComplaintId, x.CreatedAt });
        builder.Entity<ComplaintStatusEvent>().HasOne(x => x.ChangedByMembership).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ComplaintFeedback>().HasIndex(x => x.ComplaintId).IsUnique();
        builder.Entity<ComplaintFeedback>().HasOne(x => x.Membership).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<AssignmentRule>().HasIndex(x => new { x.TenantId, x.Order });
        builder.Entity<AssignmentRule>().HasOne(x => x.SupervisorMembership).WithMany().OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Notification>().HasIndex(x => new { x.TenantId, x.MembershipId, x.IsRead, x.CreatedAt });
    }
}
