using Microsoft.EntityFrameworkCore;
using SukoonDesk.Api.Domain;

namespace SukoonDesk.Api.Infrastructure;

public static class SchemaUpgrade
{
    public static async Task ApplyAsync(AppDbContext db)
    {
        if (!db.Database.IsSqlite()) return;

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('TenantMemberships');";
            await using var reader = await command.ExecuteReaderAsync();
            var hasUnitLabel = false;
            while (await reader.ReadAsync())
                hasUnitLabel |= string.Equals(reader.GetString(1), "UnitLabel", StringComparison.OrdinalIgnoreCase);
            if (!hasUnitLabel)
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE TenantMemberships ADD COLUMN UnitLabel TEXT NOT NULL DEFAULT '';");
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('AspNetUsers');";
            await using var reader = await command.ExecuteReaderAsync();
            var hasPlatformAdmin = false;
            while (await reader.ReadAsync())
                hasPlatformAdmin |= string.Equals(reader.GetString(1), "IsPlatformAdmin", StringComparison.OrdinalIgnoreCase);
            if (!hasPlatformAdmin)
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE AspNetUsers ADD COLUMN IsPlatformAdmin INTEGER NOT NULL DEFAULT 0;");
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('AspNetUsers');";
            await using var reader = await command.ExecuteReaderAsync();
            var hasMustChangePassword = false;
            while (await reader.ReadAsync())
                hasMustChangePassword |= string.Equals(reader.GetString(1), "MustChangePassword", StringComparison.OrdinalIgnoreCase);
            if (!hasMustChangePassword)
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE AspNetUsers ADD COLUMN MustChangePassword INTEGER NOT NULL DEFAULT 0;");
        }

        var statements = new[]
        {
            "CREATE TABLE IF NOT EXISTS DataProtectionKeys (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, FriendlyName TEXT NULL, Xml TEXT NULL);",
            """
            CREATE TABLE IF NOT EXISTS ComplaintCategories (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, Name TEXT NOT NULL,
                Description TEXT NOT NULL, Color TEXT NOT NULL, DefaultSlaHours INTEGER NOT NULL,
                DefaultPriority INTEGER NOT NULL, IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY (TenantId) REFERENCES Tenants(Id) ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS Complaints (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, Number TEXT NOT NULL,
                Title TEXT NOT NULL, Description TEXT NOT NULL, Location TEXT NOT NULL,
                Priority INTEGER NOT NULL, Status INTEGER NOT NULL, CategoryId TEXT NOT NULL,
                CreatedByMembershipId TEXT NOT NULL, AssignedToMembershipId TEXT NULL,
                DueAt TEXT NOT NULL, ResolvedAt TEXT NULL, ClosedAt TEXT NULL,
                CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (TenantId) REFERENCES Tenants(Id) ON DELETE CASCADE,
                FOREIGN KEY (CategoryId) REFERENCES ComplaintCategories(Id) ON DELETE CASCADE,
                FOREIGN KEY (CreatedByMembershipId) REFERENCES TenantMemberships(Id) ON DELETE RESTRICT,
                FOREIGN KEY (AssignedToMembershipId) REFERENCES TenantMemberships(Id) ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS ComplaintComments (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, ComplaintId TEXT NOT NULL,
                AuthorMembershipId TEXT NOT NULL, Body TEXT NOT NULL, IsInternal INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (ComplaintId) REFERENCES Complaints(Id) ON DELETE CASCADE,
                FOREIGN KEY (AuthorMembershipId) REFERENCES TenantMemberships(Id) ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS ComplaintStatusEvents (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, ComplaintId TEXT NOT NULL,
                FromStatus INTEGER NULL, ToStatus INTEGER NOT NULL, ChangedByMembershipId TEXT NOT NULL,
                Note TEXT NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY (ComplaintId) REFERENCES Complaints(Id) ON DELETE CASCADE,
                FOREIGN KEY (ChangedByMembershipId) REFERENCES TenantMemberships(Id) ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS ComplaintFeedback (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, ComplaintId TEXT NOT NULL,
                MembershipId TEXT NOT NULL, Rating INTEGER NOT NULL, Comment TEXT NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY (ComplaintId) REFERENCES Complaints(Id) ON DELETE CASCADE,
                FOREIGN KEY (MembershipId) REFERENCES TenantMemberships(Id) ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS AssignmentRules (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, Name TEXT NOT NULL,
                CategoryId TEXT NULL, LocationContains TEXT NOT NULL, SupervisorMembershipId TEXT NOT NULL,
                "Order" INTEGER NOT NULL, IsActive INTEGER NOT NULL,
                FOREIGN KEY (TenantId) REFERENCES Tenants(Id) ON DELETE CASCADE,
                FOREIGN KEY (CategoryId) REFERENCES ComplaintCategories(Id) ON DELETE CASCADE,
                FOREIGN KEY (SupervisorMembershipId) REFERENCES TenantMemberships(Id) ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS Notifications (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, MembershipId TEXT NOT NULL,
                Title TEXT NOT NULL, Message TEXT NOT NULL, Type TEXT NOT NULL, Link TEXT NOT NULL,
                IsRead INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY (MembershipId) REFERENCES TenantMemberships(Id) ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS PublicMembers (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, MembershipId TEXT NOT NULL,
                Cnic TEXT NOT NULL, FullName TEXT NOT NULL, Phone TEXT NOT NULL, Email TEXT NOT NULL,
                PlotNo TEXT NOT NULL, StreetNo TEXT NOT NULL, Block TEXT NOT NULL, Phase INTEGER NOT NULL,
                IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY (TenantId) REFERENCES Tenants(Id) ON DELETE CASCADE,
                FOREIGN KEY (MembershipId) REFERENCES TenantMemberships(Id) ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS ComplaintAttachments (
                Id TEXT NOT NULL PRIMARY KEY, TenantId TEXT NOT NULL, ComplaintId TEXT NOT NULL,
                CommentId TEXT NULL, UploaderMembershipId TEXT NOT NULL, FileName TEXT NOT NULL,
                StoredName TEXT NOT NULL, ContentType TEXT NOT NULL, Size INTEGER NOT NULL, CreatedAt TEXT NOT NULL,
                FOREIGN KEY (ComplaintId) REFERENCES Complaints(Id) ON DELETE CASCADE,
                FOREIGN KEY (CommentId) REFERENCES ComplaintComments(Id) ON DELETE CASCADE,
                FOREIGN KEY (UploaderMembershipId) REFERENCES TenantMemberships(Id) ON DELETE RESTRICT
            );
            """,
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ComplaintCategories_TenantId_Name ON ComplaintCategories(TenantId, Name);",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Complaints_TenantId_Number ON Complaints(TenantId, Number);",
            "CREATE INDEX IF NOT EXISTS IX_Complaints_TenantId_Status_DueAt ON Complaints(TenantId, Status, DueAt);",
            "CREATE INDEX IF NOT EXISTS IX_ComplaintComments_TenantId_ComplaintId_CreatedAt ON ComplaintComments(TenantId, ComplaintId, CreatedAt);",
            "CREATE INDEX IF NOT EXISTS IX_ComplaintStatusEvents_TenantId_ComplaintId_CreatedAt ON ComplaintStatusEvents(TenantId, ComplaintId, CreatedAt);",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_ComplaintFeedback_ComplaintId ON ComplaintFeedback(ComplaintId);",
            "CREATE INDEX IF NOT EXISTS IX_AssignmentRules_TenantId_Order ON AssignmentRules(TenantId, \"Order\");",
            "CREATE INDEX IF NOT EXISTS IX_Notifications_TenantId_MembershipId_IsRead_CreatedAt ON Notifications(TenantId, MembershipId, IsRead, CreatedAt);",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_PublicMembers_TenantId_Cnic ON PublicMembers(TenantId, Cnic);",
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_PublicMembers_MembershipId ON PublicMembers(MembershipId);",
            "CREATE INDEX IF NOT EXISTS IX_ComplaintAttachments_TenantId_ComplaintId ON ComplaintAttachments(TenantId, ComplaintId);"
        };

        foreach (var statement in statements) await db.Database.ExecuteSqlRawAsync(statement);
        await SeedDefaultCategoriesAsync(db);
    }

    private static async Task SeedDefaultCategoriesAsync(AppDbContext db)
    {
        var tenantIds = await db.Tenants.Select(x => x.Id).ToListAsync();
        foreach (var tenantId in tenantIds)
        {
            if (await db.ComplaintCategories.AnyAsync(x => x.TenantId == tenantId)) continue;
            db.ComplaintCategories.AddRange(SeedData.CreateDefaultCategories(tenantId));
        }
        await db.SaveChangesAsync();
    }

}
